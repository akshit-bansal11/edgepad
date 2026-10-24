// The one shot: a single phone, a headline and a caption. Every frame is a pure function of t.
(function () {
  const { lerp, ease, easeOut, seg, palette, rgba, text, measure, fillRR, rr, path } = EP;
  const q = new URLSearchParams(location.search);
  const aspect = q.get('aspect') || '16x9';
  const GIF = aspect === 'gif';

  // Each aspect is laid out on its own: the headline box, where the phone stands, and the caption.
  const LAYOUT = {
    '16x9': { W: 1920, H: 1080, head: { x: 130, y: 250, w: 700 }, hs: 80, ss: 34, phone: { cx: 1330, cy: 500, sp: 1.05, sl: 1.15 }, cap: { y: 1010, size: 38, w: 1500 } },
    '9x16': { W: 1080, H: 1920, head: { x: 80, y: 120, w: 920 }, hs: 84, ss: 38, phone: { cx: 540, cy: 1010, sp: 1.4, sl: 1.08 }, cap: { y: 1740, size: 40, w: 920 } },
    '4x5': { W: 1080, H: 1350, head: { x: 80, y: 70, w: 920 }, hs: 66, ss: 30, phone: { cx: 540, cy: 765, sp: 1.0, sl: 1.1 }, cap: { y: 1262, size: 36, w: 920 } },
    gif: { W: 880, H: 660, phone: { cx: 440, cy: 464, sp: 1.0, sl: 1.0 } },
  }[aspect];
  const canvas = document.getElementById('c');
  canvas.width = LAYOUT.W;
  canvas.height = LAYOUT.H;
  const ctx = canvas.getContext('2d');

  // ---- what each scene shows: its screen on the phone ('S' both ways up, 'P' upright only, 'L' sideways only) and its headline ----
  const SCENES = {
    hook: { type: 'S', screen: 'surface', h: 'Your laptop is across the room.', sub: ['Your phone is in your hand.'] },
    what: { type: 'S', screen: 'surface', h: ['Trackpad.', 'Remote.', 'Control panel.'], at: [0.35, 0.55, 0.78], sub: ['Edgepad turns your phone into all three.'] },
    pair: { type: 'P', screen: 'devices', h: 'Paired once, over Bluetooth.', sub: ['No account. No Wi-Fi. No cloud.'] },
    trackpad: { type: 'S', screen: 'surface', h: 'The whole screen is a trackpad.', sub: ['Tap to click.', 'Two fingers to scroll.', 'Pinch to zoom.'] },
    dials: { type: 'S', screen: 'surface', h: 'The edges are dials.', sub: ['Slide along a corner to change the volume or the brightness.'] },
    split: { type: 'S', screen: 'surface', h: 'Split a corner in two.', sub: ['Up to eight dials.'] },
    media: { type: 'S', screen: 'surface', h: 'What is playing, right there.', sub: ['Play, pause and skip.'] },
    macros: { type: 'P', screen: 'macros', h: 'Macros in a deck.', sub: ['Drag to reorder. Pick the grid. Tap to open an app, a file or a website.'] },
    controls: { type: 'P', screen: 'controls', h: 'Controls holds the rest.', sub: [] },
    keyboard: { type: 'L', screen: 'keyboard', h: 'A full keyboard.', sub: ['Modifier keys for shortcuts.'] },
    annotate: { type: 'P', screen: 'annotate', h: 'Annotate and Sign.', sub: ['A pen on the laptop’s screen.', 'A signature, saved as an image.'] },
    capture: { type: 'P', screen: 'capture', h: 'Capture.', sub: ['A screenshot or a recording, with the laptop’s own tool.'] },
    gamepad: { type: 'L', screen: 'gamepad', h: 'A gamepad.', sub: ['For games on Windows and Linux.'] },
    pin: { type: 'P', screen: 'pin', h: 'Choose how much a phone is trusted.', sub: ['A yes, once.', 'A six-digit PIN the first time.', 'The PIN every time.'], levels: true },
    look: { type: 'S', screen: 'surface', h: 'Light or dark.', sub: ['Upright or sideways.'] },
    platforms: { type: 'S', screen: 'surface', h: 'Windows, Mac and Linux.', sub: ['Mac and Linux are still in testing.'] },
    close: { type: 'S', screen: 'surface', end: true },
  };

  // ---- timing: out/timing.json from voice.py, or a stand-in worked out from the script so stills render without it ----
  function getJSON(url) {
    return new Promise((ok, bad) => {
      const x = new XMLHttpRequest();
      x.open('GET', url);
      x.onload = () => ((x.status === 200 || x.status === 0) && x.responseText ? ok(JSON.parse(x.responseText)) : bad(new Error(url)));
      x.onerror = () => bad(new Error(url));
      x.send();
    });
  }
  function fixture(script) {
    let at = 0;
    const lines = script.map((l, i) => {
      const lead = i === 0 ? 0.6 : 0.25;
      const speech = l.say.length * 0.068;
      const start = at + lead;
      at += Math.max(lead + speech + 0.7, l.hold || 0);
      return { id: l.id, scene: l.scene, start, end: start + speech, caption: l.caption };
    });
    return { duration: at, lines };
  }
  const GIF_TIMING = { duration: 7.2, lines: [{ id: 'trackpad', scene: 'trackpad', start: 0.25, end: 3.2, caption: '' }, { id: 'dials', scene: 'dials', start: 3.75, end: 7, caption: '' }] };

  let timing, scenes = [];
  const T = {}; // scene id -> start second; a scene that is not in the cut starts far in the future
  const start = (id) => (id in T ? T[id] : 1e9);
  window.ready = (async () => {
    await Promise.all([400, 700, 900].map((w) => document.fonts.load(`${w} 20px Lato`)));
    if (GIF) timing = GIF_TIMING;
    else timing = await getJSON('out/timing.json').catch(() => getJSON('script.json').then(fixture));
    scenes = timing.lines.map((l, i) => ({ ...SCENES[l.scene], id: l.scene, line: l, S: i === 0 ? 0 : l.start - 0.25 }));
    scenes.forEach((s, i) => {
      s.E = i + 1 < scenes.length ? scenes[i + 1].S : timing.duration;
      T[s.id] = s.S;
    });
    window.duration = timing.duration;
    if (q.has('t')) window.renderAt(parseFloat(q.get('t')));
  })();

  // ---- keyframes: [time, value] pairs eased between, for the phone's turn and the theme ----
  function keyed(kf, t) {
    if (!kf.length || t <= kf[0][0]) return kf.length ? kf[0][1] : 0;
    for (let i = 0; i + 1 < kf.length; i++) {
      if (t < kf[i + 1][0]) return lerp(kf[i][1], kf[i + 1][1], ease((t - kf[i][0]) / (kf[i + 1][0] - kf[i][0])));
    }
    return kf[kf.length - 1][1];
  }
  const turns = () => [[0, 0], [start('keyboard'), 0], [start('keyboard') + 0.9, 1], [start('annotate'), 1], [start('annotate') + 0.8, 0], [start('gamepad'), 0], [start('gamepad') + 0.9, 1], [start('pin'), 1], [start('pin') + 0.8, 0], [start('look') + 2.4, 0], [start('look') + 3.3, 1], [start('look') + 4.7, 1], [start('look') + 5.6, 0]];
  const themes = () => [[0, 0], [start('look') + 0.6, 0], [start('look') + 1.5, 1], [start('look') + 4.6, 1], [start('look') + 5.4, 0]];

  // ---- the surface: its state at time t, from the scene starts ----
  const PER = new EP.Perimeter(390, 844, 44);
  const fmt = (s) => `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, '0')}`;
  const P = [0, 0, 0, 0];
  // A finger on a dial, 30 dp in from the edge, at path length s.
  const onEdge = (s) => {
    PER.point(s, P);
    return [P[0] + P[2] * 30, P[1] + P[3] * 30];
  };

  function surfaceState(t, pal) {
    const tp = start('trackpad'), dl = start('dials'), sp = start('split'), md = start('media'), mc = start('macros');
    const S = { pal, radius: 44, dials: {}, fingers: [], trail: [] };
    S.hint = ease(seg(t, 0.6, 1.3)) * (1 - ease(seg(t, dl - 0.2, dl + 0.5)));
    S.split = ease(seg(t, sp + 0.5, sp + 1.7)) * (1 - ease(seg(t, md, md + 0.9)));
    // Volume slides down the left edge (counter-clockwise lowers it), brightness down the right edge (clockwise raises it).
    const volD = 108 * ease(seg(t, dl + 0.9, dl + 2.4)), briD = 103 * ease(seg(t, dl + 3.6, dl + 5.2));
    const volOn = t > dl + 0.7 && t < dl + 2.65, briOn = t > dl + 3.4 && t < dl + 5.45;
    const vol = EP.levelToRuler(70) - volD, bri = EP.levelToRuler(40) + briD;
    S.dials.VOL = { ruler: vol, text: String(Math.round(vol * 0.35)), armed: volOn };
    S.dials.BRI = { ruler: bri, text: String(Math.round(bri * 0.35)), armed: briOn };
    S.dials.ZOOM = { ruler: 30, text: '0', armed: false };
    S.dials.APPS = { ruler: 20, text: '', armed: false };
    S.dials.MIC = { ruler: EP.levelToRuler(62), text: '62', armed: false };
    S.dials.RATE = { ruler: 60, text: '60 Hz', armed: false };
    const shown = ease(seg(t, md + 0.3, md + 0.9));
    const pos = Math.min(227, 72 + Math.max(0, t - md));
    S.media = shown > 0 ? { a: shown, playing: !(t > md + 2.0 && t < md + 3.4), title: 'Night Drive', sub: `Music Player · ${fmt(pos)} of 3:48` } : null;
    S.dials.SCRUB = shown > 0 ? { ruler: EP.levelToRuler((pos / 228) * 100), text: fmt(pos), armed: false } : { ruler: 0, text: '0%', armed: false };

    const fingers = [];
    const add = (p) => p && fingers.push(p);
    // Trackpad: one finger glides, taps; two scroll; two pinch.
    const glide = [[tp + 0.5, 150, 330], [tp + 0.9, 205, 372], [tp + 1.4, 262, 440], [tp + 1.9, 214, 520]];
    const one = path(t, glide);
    if (one) {
      S.trail = EP.trailOf((tt) => path(tt, glide) || one, t, 14, 0.022);
      add(one);
    }
    add(path(t, [[tp + 2.5, 195, 470], [tp + 2.8, 195, 470]]));
    add(path(t, [[tp + 3.3, 160, 380], [tp + 3.6, 160, 380], [tp + 4.5, 160, 520], [tp + 4.8, 160, 520]]));
    add(path(t, [[tp + 3.3, 232, 380], [tp + 3.6, 232, 380], [tp + 4.5, 232, 520], [tp + 4.8, 232, 520]]));
    if (t > tp + 5.0 && t < dl) {
      add(path(t, [[tp + 5.3, 172, 452], [tp + 5.6, 172, 452], [tp + 6.6, 118, 392], [tp + 6.9, 118, 392]]));
      add(path(t, [[tp + 5.3, 220, 452], [tp + 5.6, 220, 452], [tp + 6.6, 272, 512], [tp + 6.9, 272, 512]]));
    }
    // Dials.
    const lv = PER.lengthAt(0), rv = PER.lengthAt(1);
    add(path(t, [[dl + 0.6, ...onEdge(lv)], [dl + 0.9, ...onEdge(lv)], [dl + 2.4, ...onEdge(lv - 108)], [dl + 2.65, ...onEdge(lv - 108)]]));
    add(path(t, [[dl + 3.3, ...onEdge(rv)], [dl + 3.6, ...onEdge(rv)], [dl + 5.2, ...onEdge(rv + 103)], [dl + 5.45, ...onEdge(rv + 103)]]));
    // Play / pause, then reaching for the macro button as the deck opens.
    const play = [195, 0.9 * 844];
    add(path(t, [[md + 1.8, ...play], [md + 2.1, ...play]]));
    add(path(t, [[md + 3.2, ...play], [md + 3.5, ...play]]));
    const grid = [195 + 48, 40];
    add(path(t, [[mc - 1.1, 300, 300], [mc - 0.5, ...grid], [mc - 0.05, ...grid]]));
    S.press = { name: 'grid', k: tri(t, mc - 0.6, 0.5) };
    S.fingers = fingers;
    if (S.trail.length && fingers.length !== 1) S.trail = [];
    return S;
  }
  const tri = (t, t0, d) => {
    const x = (t - t0) / d;
    return x < 0 || x > 1 ? 0 : Math.sin(x * Math.PI);
  };

  // ---- which screens are on the phone at t, for each orientation ----
  const sceneAt = (t) => {
    let i = 0;
    while (i + 1 < scenes.length && t >= scenes[i + 1].S) i++;
    return i;
  };
  function layersFor(t, i, upright) {
    const fits = (j) => (upright ? scenes[j].type !== 'L' : scenes[j].type !== 'P');
    let c = i;
    while (c >= 0 && !fits(c)) c--;
    if (c < 0) return [];
    let p = c - 1;
    while (p >= 0 && !fits(p)) p--;
    const cur = { key: scenes[c].screen, lt: t - scenes[c].S, a: 1 };
    if (p < 0 || scenes[p].screen === cur.key) return [cur];
    cur.a = ease(seg(t, scenes[c].S, scenes[c].S + 0.45));
    return cur.a < 1 ? [{ key: scenes[p].screen, lt: t - scenes[p].S, a: 1 }, cur] : [cur];
  }
  function drawLayers(layers, w, h, t, pal, alpha) {
    const base = ctx.globalAlpha;
    for (const l of layers) {
      ctx.save();
      ctx.globalAlpha = base * alpha * l.a;
      if (l.key === 'surface') EP.drawSurface(ctx, w, h, surfaceState(t, pal));
      else {
        ctx.fillStyle = rgba(pal.bg);
        ctx.fillRect(0, 0, w, h);
        EP.screens[l.key](ctx, w, h, pal, l.lt);
      }
      ctx.restore();
    }
  }

  // ---- the phone: 390 x 844 dp of screen in a 12 dp bezel ----
  function drawPhone(t, i) {
    const { cx, cy, sp, sl } = LAYOUT.phone;
    const turn = keyed(turns(), t);
    const pal = palette(keyed(themes(), t));
    const enter = easeOut(seg(t, 0, 1.1));
    const scale = lerp(sp, sl, turn) * lerp(0.93, 1, enter);
    ctx.save();
    ctx.globalAlpha = ease(seg(t, 0, 0.7));
    ctx.translate(cx, cy + Math.sin(t * 0.8) * 3);
    ctx.scale(scale, scale);
    ctx.rotate((-turn * Math.PI) / 2);
    ctx.translate(-207, -434);
    ctx.shadowColor = 'rgba(30,36,60,0.28)';
    ctx.shadowBlur = 60;
    ctx.shadowOffsetY = 26;
    fillRR(ctx, 0, 0, 414, 868, 58, '#0C0C0F');
    ctx.shadowColor = 'transparent';
    ctx.lineWidth = 2;
    ctx.strokeStyle = '#33353C';
    rr(ctx, 1, 1, 412, 866, 57);
    ctx.stroke();
    ctx.save();
    rr(ctx, 12, 12, 390, 844, 46);
    ctx.clip();
    const land = ease(seg(turn, 0.35, 0.65));
    ctx.save();
    ctx.translate(12, 12);
    drawLayers(layersFor(t, i, true), 390, 844, t, pal, 1 - land);
    ctx.restore();
    if (land > 0) {
      ctx.save();
      ctx.translate(12 + 390, 12);
      ctx.rotate(Math.PI / 2);
      drawLayers(layersFor(t, i, false), 844, 390, t, pal, land);
      ctx.restore();
    }
    ctx.restore();
    ctx.fillStyle = '#16171B';
    ctx.beginPath();
    ctx.arc(207, 34, 6, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
  }

  // ---- text on the page ----
  function wrap(s, size, w, weight, maxWidth) {
    const out = [];
    let line = '';
    for (const word of s.split(' ')) {
      const next = line ? `${line} ${word}` : word;
      if (line && measure(ctx, next, size, weight) > maxWidth) {
        out.push(line);
        line = word;
      } else line = next;
    }
    return out.concat(line);
  }
  function headline(s, t) {
    const { x, y, w } = LAYOUT.head;
    const { hs, ss } = LAYOUT;
    const inA = ease(seg(t, s.S + 0.12, s.S + 0.7)), outA = 1 - ease(seg(t, s.E - 0.4, s.E));
    const a = inA * outA;
    if (a <= 0) return;
    ctx.save();
    const dy = (1 - inA) * 22;
    if (s.end) {
      const tile = hs * 2.3;
      EP.mark(ctx, x, y + dy - 40, tile, false, a);
      text(ctx, 'Edgepad', x, y + dy - 40 + tile + hs * 1.1 + 24, { size: hs * 1.5, w: 'Black', color: EP.page.ink, a, track: -0.01 });
      text(ctx, 'Free and open source.', x, y + dy - 40 + tile + hs * 1.1 + 24 + ss * 1.9, { size: ss * 1.3, color: EP.page.dim, a });
      text(ctx, 'github.com/akshit-bansal11/edgepad', x, y + dy - 40 + tile + hs * 1.1 + 24 + ss * 3.5, { size: ss * 1.15, w: 'Bold', color: EP.page.accent, a });
      ctx.restore();
      return;
    }
    const heads = Array.isArray(s.h) ? s.h : [s.h];
    let yy = y + dy;
    heads.forEach((h, k) => {
      const reveal = s.at ? ease(seg(t, s.line.start + s.at[k] * (s.line.end - s.line.start) - 0.2, s.line.start + s.at[k] * (s.line.end - s.line.start) + 0.25)) : 1;
      for (const line of wrap(h, hs, w, 'Black', w)) {
        text(ctx, line, x, yy + hs, { size: hs, w: 'Black', color: EP.page.ink, a: a * reveal, track: -0.01 });
        yy += hs * 1.08;
      }
    });
    yy += ss * 0.5;
    s.sub.forEach((line, k) => {
      const lit = s.levels ? ease(seg(t, s.S + 1.2 + k * 1.9, s.S + 1.6 + k * 1.9)) : 1;
      const col = s.levels ? (lit > 0.5 ? EP.page.accent : EP.page.dim) : EP.page.dim;
      for (const l of wrap(line, ss, w, 'Regular', w)) {
        text(ctx, l, x, yy + ss, { size: ss, w: s.levels ? 'Bold' : 'Regular', color: col, a: a * (s.levels ? 0.45 + 0.55 * lit : 1) });
        yy += ss * 1.4;
      }
      yy += s.levels ? ss * 0.3 : 0;
    });
    ctx.restore();
  }
  function caption(t) {
    const line = timing.lines.find((l) => t > l.start - 0.1 && t < l.end + 0.4);
    if (!line) return;
    const { y, size, w } = LAYOUT.cap;
    const a = ease(seg(t, line.start - 0.1, line.start + 0.15)) * (1 - ease(seg(t, line.end + 0.1, line.end + 0.4)));
    const lines = wrap(line.caption, size, w, 'Bold', w);
    lines.forEach((l, i) => text(ctx, l, LAYOUT.W / 2, y + (i - (lines.length - 1) / 2) * size * 1.3, { size, w: 'Bold', color: EP.page.ink, align: 'center', base: 'middle', a }));
  }

  window.renderAt = (t) => {
    const i = sceneAt(t);
    ctx.fillStyle = EP.page.ground;
    ctx.fillRect(0, 0, LAYOUT.W, LAYOUT.H);
    if (!GIF) scenes.forEach((s) => headline(s, t));
    drawPhone(t, i);
    if (!GIF) caption(t);
  };
})();
