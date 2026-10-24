// The director: one cut (window.CUT) from its narration timing (window.TIMING, written by voice.py) and its beats
// (window.BEATS, ui/beats.js). It builds the page's text as DOM revealed by one paused GSAP timeline, and draws the
// phone, the laptop cards and the ground on one canvas from a proxy tween's onUpdate. Every frame is a pure function
// of the timeline's time, so HyperFrames can seek to any frame in any order.
(function () {
  const { lerp, clamp, ease, seg, rgba, text, fillRR, rr } = EP;
  const CUT = window.CUT;
  const W = CUT.w, H = CUT.h, TALL = H > W;
  const BASE = CUT.base || '';
  const INK = '#16171A', BLUE = '#0068D6', GREY = '#6B6E76', GROUND = '#F2F3F7';

  // ---- layout per format: where the headline, the sub-line, the caption pill and the laptop cards sit ----
  const F = TALL
    ? { head: { x: 80, y: 150, size: 100, max: 920 }, top: { x: 80, y: 150, size: 100, max: 920 }, sub: { size: 52, gap: 40, min: 0 }, topSub: 52, cap: { cx: 540, cy: 1792, size: 46 }, card: { x: 80, y: 470 } }
    : { head: { x: 150, y: 250, size: 110, max: 800 }, top: { x: 360, y: 42, size: 96, max: 1300 }, sub: { size: 56, gap: 52, min: 488 }, topSub: 44, cap: { cx: 550, cy: 990, size: 44 }, card: { x: 150, y: 640 } };

  // Camera presets: the phone's centre in page pixels, its scale in pixels per dp, and its turn in degrees.
  const CAM = TALL
    ? {
        off: { x: 540, y: 2600, s: 1.2, r: 0 }, right: { x: 540, y: 1205, s: 1.2, r: 0 }, hero: { x: 540, y: 1240, s: 1.3, r: 0 },
        tl: { x: 820, y: 1430, s: 2.0, r: 0 }, tr: { x: 260, y: 1430, s: 2.0, r: 0 }, br: { x: 300, y: 820, s: 2.0, r: 0 }, bl: { x: 780, y: 820, s: 2.0, r: 0 },
        top: { x: 540, y: 1500, s: 1.7, r: 0 }, media: { x: 540, y: 1000, s: 1.6, r: 0 }, settings: { x: 540, y: 1460, s: 1.6, r: 0 },
        land: { x: 540, y: 1150, s: 1.18, r: -90 }, landRight: { x: 540, y: 1200, s: 1.1, r: -90 }, end: { x: 540, y: 1380, s: 0.92, r: 0 }, wide: { x: 540, y: 1230, s: 1.14, r: 0 },
      }
    : {
        off: { x: 1300, y: 1700, s: 1.08, r: 0 }, right: { x: 1300, y: 548, s: 1.08, r: 0 }, hero: { x: 1310, y: 520, s: 1.22, r: 0 },
        tl: { x: 1515, y: 1047, s: 1.79, r: 0 }, tr: { x: 1160, y: 1047, s: 1.79, r: 0 }, br: { x: 1200, y: 40, s: 1.79, r: 0 }, bl: { x: 1515, y: 40, s: 1.79, r: 0 },
        top: { x: 1483, y: 942, s: 1.68, r: 0 }, media: { x: 1320, y: 410, s: 1.45, r: 0 }, settings: { x: 1300, y: 700, s: 1.55, r: 0 },
        land: { x: 1040, y: 615, s: 1.55, r: -90 }, landRight: { x: 1330, y: 560, s: 1.0, r: -90 }, end: { x: 1350, y: 545, s: 0.845, r: 0 }, wide: { x: 1300, y: 548, s: 1.0, r: 0 },
      };
  EP.CAM = CAM;

  // ---- timing: voice.py's, or a stand-in from the script so a still renders on a machine that has never run it ----
  const norm = (s) => s.toLowerCase().replace(/[^a-z0-9]/g, '');
  function standIn() {
    const x = new XMLHttpRequest();
    x.open('GET', `${BASE}scripts/${CUT.id}.json`, false);
    x.send();
    const script = JSON.parse(x.responseText);
    let at = 0.5;
    const lines = [], chapters = [];
    script.chapters.forEach((ch, ci) => {
      chapters.push({ title: ch.title, start: Math.max(0, at - 0.5) });
      ch.lines.forEach((t) => {
        const words = t.split(/\s+/).map((tok) => {
          const d = 0.12 + norm(tok).length * 0.052 + (/[.,?!]$/.test(tok) ? 0.18 : 0);
          const w = [tok, at, at + d - 0.04];
          at += d;
          return w;
        });
        lines.push({ chapter: ci, text: t, start: words[0][1], end: words[words.length - 1][2], words });
        at += 0.47;
      });
      at += 0.5;
    });
    return { id: CUT.id, duration: at + 2.5, chapters, lines, standIn: true };
  }
  const T = CUT.still ? { id: CUT.id, duration: 1, lines: [], chapters: [] } : window.TIMING || standIn();
  const LINES = T.lines;
  const DUR = CUT.still ? 1 : T.duration;
  window.FILM = { duration: DUR, timing: T };

  // L(i): one line's clock. w('word', n) is when its nth spoken word starting with those letters begins.
  function L(i) {
    const line = LINES[i];
    const find = (word, n = 1) => {
      const k = norm(word);
      let seen = 0;
      for (const w of line.words) if (norm(w[0]).startsWith(k) && ++seen === n) return w;
      return null;
    };
    return {
      i,
      t0: line.start,
      t1: line.end,
      next: i + 1 < LINES.length ? LINES[i + 1].start : DUR,
      w: (word, n) => (find(word, n) || [0, line.start])[1],
      we: (word, n) => (find(word, n) || [0, 0, line.end])[2],
      words: line.words,
    };
  }

  // ---- the director's tracks: everything on the canvas is read from these at time t ----
  const Track = EP.Track;
  const D = {
    cam: new Track(CUT.startCam ? CAM[CUT.startCam] : CAM.off),
    turn: new Track(0),
    theme: new Track(0),
    bg: new Track(0),
    vol: new Track(31),
    bri: new Track(55),
    zoom: new Track(0),
    split: new Track(0),
    extra: new Track(0),
    hint: new Track(0),
    mediaA: new Track(1),
    mediaY: new Track(0.78),
    screens: [{ t: -1, key: 'surface', land: false, o: {}, anchors: null }],
    armed: [],
    modes: [[-1, 'normal']],
    presses: [],
    fingers: [],
    cards: [],
    media: [[-1, { app: 'YouTube', title: 'Mountain Lake Timelapse', dur: 612, pos: 298, playing: true }]],
    shaping: [],
    scrubOff: new Track(0),
    // Seconds added to the playing position by a finger on the scrub dial, between t0 and t1.
    scrub(t0, t1, delta) {
      const m = mediaAt(t0);
      this.scrubOff.to(t0, t1, this.scrubOff.at(t0) + delta);
      this.armed.push([t0 - 0.12, t1 + 0.18, 'SCRUB']);
      this.fingers.push({ dial: 'SCRUB', t0, t1, from: 0, to: (delta / m.dur) * 100, trail: true });
    },
    // One more anchor on the latest screen: film time t shows that screen's own time lt.
    anchor(t, lt) {
      const s = this.screens[this.screens.length - 1];
      s.anchors.push([t, lt]);
      s.anchors.sort((a, b) => a[0] - b[0]);
    },
    // A camera move to a preset (or a {x,y,s,r}) over dur seconds from t.
    move(t, preset, dur = 0.9) {
      this.cam.to(t, t + dur, typeof preset === 'string' ? CAM[preset] : preset);
    },
    // The phone shows `key` from t. anchors map film time to the screen's own clock, [[t, lt], ...].
    screen(t, key, o = {}, anchors = null, land = false) {
      this.screens.push({ t, key, o, anchors: anchors || [[t, 0]], land });
      this.screens.sort((a, b) => a.t - b.t);
    },
    mode(t, m) {
      this.modes.push([t, m]);
      this.modes.sort((a, b) => a[0] - b[0]);
    },
    press(t, name) {
      this.presses.push([t, name]);
    },
    // A finger on the surface: keys [[t, x, y], ...] in the surface's dp, down from the first key to the last.
    touch(keys, trail = true) {
      this.fingers.push({ keys, trail });
    },
    tap(t, x, y, hold = 0.14) {
      this.fingers.push({ keys: [[t, x, y], [t + hold, x, y]], trail: false });
    },
    // A dial slid by a finger: the level eases from its value to `to` between t0 and t1 while it is armed.
    dial(kind, t0, t1, to, finger = true) {
      const tr = { VOL: this.vol, BRI: this.bri, ZOOM: this.zoom }[kind];
      const from = tr.at(t0);
      tr.to(t0, t1, to);
      this.armed.push([t0 - 0.12, t1 + 0.18, kind]);
      if (finger) this.fingers.push({ dial: kind, t0, t1, from, to, trail: true });
    },
    card(t0, t1, draw) {
      this.cards.push({ t0, t1, draw });
    },
    play(t, change) {
      const prev = mediaAt(t);
      this.media.push([t, { ...prev, ...change, base: this.scrubOff.at(t) }]);
      this.media.sort((a, b) => a[0] - b[0]);
    },
  };
  window.DIRECTOR = D;

  // Media: the track playing at t, its position running on while it plays.
  function mediaAt(t) {
    let ev = D.media[0];
    for (const e of D.media) if (e[0] <= t) ev = e;
    const m = ev[1];
    const pos = clamp(m.pos + (m.playing ? t - Math.max(ev[0], 0) : 0) + D.scrubOff.at(t) - (m.base || 0), 0, m.dur);
    return { ...m, pos };
  }
  const fmt = (s) => `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, '0')}`;
  const discrete = (list, t) => {
    let v = list[0][1];
    for (const [at, x] of list) if (at <= t) v = x;
    return v;
  };

  // ---- run the beats: each line's act writes into the tracks ----
  const BEATS = (window.BEATS && window.BEATS[CUT.id]) || [];
  if (!CUT.still && BEATS.length !== LINES.length) console.error(`beats: ${BEATS.length} for ${LINES.length} lines in ${CUT.id}`);
  BEATS.forEach((b, i) => {
    if (b && b.act && LINES[i]) b.act(L(i), D);
  });
  if (CUT.setup) CUT.setup(D);

  // ---- the surface at t ----
  const PER = { p: new EP.Perimeter(390, 844, 44), l: new EP.Perimeter(844, 390, 44) };
  const P4 = [0, 0, 0, 0];
  const CORNER = { VOL: 0, BRI: 1, ZOOM: 2, SCRUB: 3 };
  function onEdge(per, s, inset = 26) {
    per.point(s, P4);
    return [P4[0] + P4[2] * inset, P4[1] + P4[3] * inset];
  }
  function surfaceState(t, pal, land) {
    const per = land ? PER.l : PER.p;
    const S = { pal, radius: 44, dials: {}, fingers: [], trail: [] };
    const split = D.split.at(t);
    S.split = split;
    S.extra = D.extra.at(t);
    S.hint = D.hint.at(t);
    S.mode = discrete(D.modes, t);
    const b = D.bg.at(t);
    S.bg = b > 0 ? { k: b, from: '#0F2A4A', to: '#3A1C5C', angle: 60 } : null;
    const armed = (k) => D.armed.some(([a, z, kind]) => kind === k && t >= a && t <= z);
    const vol = D.vol.at(t), bri = D.bri.at(t), zoom = D.zoom.at(t);
    S.dials.VOL = { ruler: EP.levelToRuler(vol), text: String(Math.round(vol)), armed: armed('VOL') };
    S.dials.BRI = { ruler: EP.levelToRuler(bri), text: String(Math.round(bri)), armed: armed('BRI') };
    S.dials.ZOOM = { ruler: zoom * 22, text: zoom > 0.5 ? `+${Math.round(zoom)}` : '0', armed: armed('ZOOM') };
    S.dials.ZOOM2 = { ruler: 0, text: '0', armed: false };
    S.dials.APPS = { ruler: 0, text: '', armed: false };
    S.dials.MIC = { ruler: EP.levelToRuler(62), text: '62', armed: false };
    S.dials.RATE = { ruler: 0, text: '120 Hz', armed: false };
    const m = mediaAt(t);
    S.media = { a: D.mediaA.at(t), playing: m.playing, app: m.app, title: m.title, sub: `${m.app} · ${fmt(m.pos)} of ${fmt(m.dur)}`, at: [0.5, D.mediaY.at(t)], transport: [0.5, 0.9], press: 0 };
    for (const [pt] of D.presses.filter((p) => p[1] === 'play')) S.media.press = Math.max(S.media.press, tri(t, pt, 0.3));
    S.dials.SCRUB = { ruler: EP.levelToRuler((m.pos / m.dur) * 100), text: fmt(m.pos), armed: armed('SCRUB') };
    S.press = null;
    for (const [pt, name] of D.presses) {
      const k = tri(t, pt, 0.32);
      if (k > 0 && name !== 'play') S.press = { name, k };
    }
    // Fingers: plain paths, and fingers riding a dial along the edge as its level moves.
    let trailFn = null;
    for (const f of D.fingers) {
      if (f.dial) {
        if (t < f.t0 - 0.1 || t > f.t1 + 0.12) continue;
        const kind = f.dial;
        const slot = { VOL: 0, BRI: 3, ZOOM: 4, SCRUB: 7 }[kind];
        const c = lerp(per.cornerMid[CORNER[kind]], per.slotAt(slot, 50), D.split.at(f.t0));
        const dl = kind === 'ZOOM' ? 22 : 1 / (0.25 * 1.4);
        const fn = (tt) => {
          const k = EP.easeInOut((tt - f.t0) / (f.t1 - f.t0));
          const [x, y] = onEdge(per, c + (lerp(f.from, f.to, k) - f.from) * dl);
          const a = ease((tt - (f.t0 - 0.1)) / 0.08) * (1 - ease((tt - f.t1) / 0.1));
          return a > 0 ? { x, y, a } : null;
        };
        const p = fn(t);
        if (p) {
          S.fingers.push(p);
          trailFn = fn;
        }
      } else {
        const p = EP.path(t, f.keys, true);
        if (p) {
          S.fingers.push(p);
          if (f.trail) trailFn = (tt) => EP.path(tt, f.keys, true);
        }
      }
    }
    if (S.fingers.length === 1 && trailFn) S.trail = EP.trailOf((tt) => trailFn(tt) || S.fingers[0], t);
    S.shaping = D.shaping.some(([a, z]) => t >= a && t <= z);
    return S;
  }
  const tri = (t, t0, d) => {
    const x = (t - t0) / d;
    return x < 0 || x > 1 ? 0 : Math.sin(x * Math.PI);
  };

  // ---- the phone: 390 x 844 dp of screen in a 12 dp bezel, its screens cross-faded ----
  function layers(t, land) {
    const fits = D.screens.filter((s) => s.land === land);
    let c = -1;
    fits.forEach((s, i) => {
      if (s.t <= t) c = i;
    });
    if (c < 0) return land ? [] : [{ s: D.screens[0], a: 1 }];
    const cur = fits[c];
    const a = ease(seg(t, cur.t, cur.t + 0.35));
    return a < 1 && c > 0 ? [{ s: fits[c - 1], a: 1 }, { s: cur, a }] : [{ s: cur, a: 1 }];
  }
  function drawScreen(ctx, w, h, t, pal, l, land) {
    ctx.save();
    ctx.globalAlpha *= l.a;
    const s = l.s;
    if (s.key === 'surface') EP.drawSurface(ctx, w, h, surfaceState(t, pal, land));
    else {
      ctx.fillStyle = rgba(pal.bg);
      ctx.fillRect(0, 0, w, h);
      EP.screens[s.key](ctx, w, h, pal, EP.warp(t, s.anchors), s.o);
      if (!land) EP.statusBar(ctx, w, pal);
    }
    ctx.restore();
  }
  function drawPhone(ctx, t) {
    const cam = D.cam.at(t);
    const turn = D.turn.at(t);
    const pal = EP.palette(D.theme.at(t));
    ctx.save();
    ctx.translate(cam.x, cam.y);
    ctx.scale(cam.s, cam.s);
    ctx.rotate((cam.r * Math.PI) / 180);
    ctx.translate(-207, -434);
    // Shadow under the body, soft and long, as the reference lights it.
    ctx.save();
    ctx.shadowColor = 'rgba(28,34,56,0.30)';
    ctx.shadowBlur = 70 * cam.s;
    ctx.shadowOffsetY = 30 * cam.s;
    fillRR(ctx, 0, 0, 414, 868, 58, '#0B0B0E');
    ctx.restore();
    // Side keys on the right edge, then the frame's rim.
    fillRR(ctx, 412, 196, 4, 74, 2, '#26272C');
    fillRR(ctx, 412, 300, 4, 116, 2, '#26272C');
    fillRR(ctx, 0, 0, 414, 868, 58, '#0B0B0E');
    ctx.lineWidth = 2;
    ctx.strokeStyle = '#34363D';
    rr(ctx, 1, 1, 412, 866, 57);
    ctx.stroke();
    ctx.save();
    rr(ctx, 12, 12, 390, 844, 46);
    ctx.clip();
    const sideways = ease(seg(turn, 0.35, 0.65));
    ctx.save();
    ctx.translate(12, 12);
    if (sideways < 1) for (const l of layers(t, false)) drawScreen(ctx, 390, 844, t, pal, { ...l, a: l.a * (1 - sideways) }, false);
    ctx.restore();
    if (sideways > 0) {
      ctx.save();
      ctx.translate(12 + 390, 12);
      ctx.rotate(Math.PI / 2);
      for (const l of layers(t, true)) drawScreen(ctx, 844, 390, t, pal, { ...l, a: l.a * sideways }, true);
      ctx.restore();
    }
    ctx.restore();
    // The punch-hole camera.
    ctx.fillStyle = '#121318';
    ctx.beginPath();
    ctx.arc(207, 30, 6.5, 0, Math.PI * 2);
    ctx.fill();
    ctx.fillStyle = 'rgba(60,80,140,0.5)';
    ctx.beginPath();
    ctx.arc(205.5, 28.5, 2, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
  }

  // ---- the frame ----
  const canvas = document.getElementById('film-canvas');
  canvas.width = W;
  canvas.height = H;
  const ctx = canvas.getContext('2d');
  function drawFrame(t) {
    ctx.save();
    ctx.fillStyle = GROUND;
    ctx.fillRect(0, 0, W, H);
    const g = TALL ? ctx.createRadialGradient(W * 0.5, H * 0.52, 0, W * 0.5, H * 0.52, W * 1.0) : ctx.createRadialGradient(W * 0.44, H * 0.6, 0, W * 0.44, H * 0.6, W * 0.46);
    g.addColorStop(0, 'rgba(255,255,255,0.95)');
    g.addColorStop(1, 'rgba(255,255,255,0)');
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, W, H);
    drawPhone(ctx, t);
    for (const c of D.cards) {
      const a = ease(seg(t, c.t0, c.t0 + 0.4)) * (1 - ease(seg(t, c.t1 - 0.35, c.t1)));
      if (a > 0) c.draw(ctx, t, a, F.card);
    }
    ctx.restore();
  }
  window.drawFrame = drawFrame;

  // ---- the DOM text ----
  const layer = document.getElementById('film-text');
  const mctx = document.createElement('canvas').getContext('2d');
  const widthOf = (s, weight, size, track = 0) => {
    mctx.font = `${weight} ${size}px Lato`;
    mctx.letterSpacing = `${track * size}px`;
    return mctx.measureText(s).width;
  };
  // "Every corner is a *dial.*" -> words with a keyword flag; the stars may wrap one word or several.
  function parse(s) {
    let key = false;
    return s.split(/\s+/).filter(Boolean).map((tok) => {
      let k = key;
      if (tok.startsWith('*')) {
        tok = tok.slice(1);
        k = key = true;
      }
      if (tok.endsWith('*')) {
        tok = tok.slice(0, -1);
        key = false;
      }
      return { w: tok, k };
    });
  }
  // Greedy wrap at maxWidth, measured in the face it is set in.
  function wrap(words, weight, size, max, track) {
    const lines = [[]];
    for (const wd of words) {
      // A lone | in the text is a line break the writer asked for.
      if (wd.w === '|') {
        lines.push([]);
        continue;
      }
      const cur = lines[lines.length - 1];
      const next = [...cur, wd].map((x) => x.w).join(' ');
      if (cur.length && widthOf(next, weight, size, track) > max) lines.push([wd]);
      else cur.push(wd);
    }
    return lines;
  }
  // When each word appears: on its spoken word in this line where there is one, else just after the one before.
  function times(words, line, from) {
    let p = 0;
    const out = words.map((wd) => {
      const n = norm(wd.w);
      for (let j = p; j < line.words.length; j++) {
        if (n && norm(line.words[j][0]) === n) {
          p = j + 1;
          return line.words[j][1];
        }
      }
      return null;
    });
    let last = from - 0.1;
    for (let i = 0; i < out.length; i++) {
      if (out[i] === null || out[i] < last) out[i] = last + 0.1;
      last = out[i];
    }
    return out;
  }
  function block(cls, x, y) {
    const el = document.createElement('div');
    el.className = cls;
    el.style.left = `${x}px`;
    el.style.top = `${y}px`;
    layer.appendChild(el);
    return el;
  }
  function words(el, lines) {
    const spans = [];
    lines.forEach((ln) => {
      const row = document.createElement('div');
      row.className = 'ln';
      ln.forEach((wd, i) => {
        const s = document.createElement('span');
        s.className = wd.k ? 'w k' : 'w';
        s.textContent = wd.w;
        row.appendChild(s);
        if (i < ln.length - 1) row.appendChild(document.createTextNode(' '));
        spans.push(s);
      });
      el.appendChild(row);
    });
    return spans;
  }
  const reveal = (tl, spans, ts) =>
    spans.forEach((s, i) => tl.fromTo(s, { opacity: 0, y: 26, filter: 'blur(14px)' }, { opacity: 1, y: 0, filter: 'blur(0px)', duration: 0.55, ease: 'power3.out' }, Math.max(0, ts[i] - 0.06)));
  const leave = (tl, el, t) => tl.to(el, { opacity: 0, y: -14, filter: 'blur(8px)', duration: 0.32, ease: 'power2.in' }, Math.max(0, t - 0.3));

  function buildText(tl) {
    let head = null; // { el, lines, layout }
    let subEnd = []; // subs to retire at the next line
    BEATS.forEach((b, i) => {
      const line = LINES[i];
      if (!b || !line) return;
      const lay = b.lay === 'top' ? F.top : F.head;
      const subSize = b.lay === 'top' ? F.topSub : F.sub.size;
      if (b.end) {
        if (head) leave(tl, head.el, line.start - 0.05);
        for (const s of subEnd) leave(tl, s, line.start - 0.02);
        head = null;
        subEnd = [];
        return buildEnd(tl, b, i);
      }
      if (b.h !== undefined && b.h !== null) {
        if (head) leave(tl, head.el, line.start - 0.05);
        head = null;
        if (b.h) {
          const ws = parse(b.h);
          const lines = wrap(ws, 900, lay.size, lay.max, -0.01);
          const el = block('hl', lay.x, lay.y);
          el.style.fontSize = `${lay.size}px`;
          reveal(tl, words(el, lines), times(ws.filter((x) => x.w !== '|'), line, line.start));
          head = { el, n: lines.length, lay, size: lay.size };
        }
      }
      for (const s of subEnd) leave(tl, s, line.start - 0.02);
      subEnd = [];
      const subs = b.s === undefined || b.s === null ? [] : Array.isArray(b.s) ? b.s : [b.s];
      const top = head ? head.lay.y + head.n * head.size * 1.0 + (b.lay === 'top' ? 18 : F.sub.gap) : lay.y;
      const y = b.lay === 'top' ? top : Math.max(F.sub.min, top);
      subs.forEach((raw, k) => {
        const [s, anchor] = raw.split('@');
        const ws = parse(s).filter((x) => x.w !== '|');
        const el = block('sub', lay.x + (b.mark ? subSize * 1.45 : 0), y);
        el.style.fontSize = `${subSize}px`;
        const from = anchor ? L(i).w(anchor) : line.start;
        reveal(tl, words(el, wrap(ws, 700, subSize, lay.max, 0)), anchor ? ws.map((_, j) => from + j * 0.09) : times(ws, line, from));
        if (k + 1 < subs.length) {
          const nxt = subs[k + 1].split('@')[1];
          leave(tl, el, L(i).w(nxt));
        } else subEnd.push(el);
      });
      if (b.mark) {
        // The mark beside "Meet Edgepad": the dark tile, as the end card shows it.
        const im = document.createElement('img');
        im.src = `${BASE}brand/edgepad-dark.svg`;
        im.className = 'mk';
        im.style.width = im.style.height = `${Math.round(subSize * 1.15)}px`;
        const el = block('mkb', lay.x, y - 4);
        el.appendChild(im);
        const at = L(i).w(b.mark);
        tl.fromTo(im, { opacity: 0, scale: 0.6, filter: 'blur(10px)' }, { opacity: 1, scale: 1, filter: 'blur(0px)', duration: 0.6, ease: 'back.out(1.6)' }, at - 0.15);
        subEnd.push(el);
      }
    });
    buildCaptions(tl);
  }

  // The end card: the mark, "Edgepad", "Free & open source." and the GitHub button, each on its spoken word.
  function buildEnd(tl, b, i) {
    const l = L(i);
    const x = F.head.x, y0 = TALL ? 250 : 286;
    const el = block('end', x, y0);
    el.innerHTML = `<img class="end-mark" src="${BASE}brand/edgepad-dark.svg" alt=""><div class="end-name"><span class="w">Edgepad</span></div><div class="end-tag"><span class="w">Free</span> <span class="w">&amp;</span> <span class="w k">open</span> <span class="w k">source.</span></div><div class="end-btn"><svg viewBox="0 0 24 24" aria-hidden="true"><path fill="#fff" d="${window.EP_BRANDS.github}"/></svg><span>github.com/akshit-bansal11/edgepad</span></div>`;
    const [mark, name, tag, btn] = el.children;
    tl.fromTo(mark, { opacity: 0, scale: 0.7, filter: 'blur(12px)' }, { opacity: 1, scale: 1, filter: 'blur(0px)', duration: 0.7, ease: 'back.out(1.5)' }, l.t0 - 0.02);
    reveal(tl, [name.firstChild], [l.w('edgepad')]);
    const tw = tag.querySelectorAll('.w');
    reveal(tl, [...tw], [l.w('free'), l.w('free') + 0.12, l.w('open'), l.w('source')]);
    tl.fromTo(btn, { opacity: 0, y: 24, scale: 0.94 }, { opacity: 1, y: 0, scale: 1, duration: 0.6, ease: 'power3.out' }, l.w('github') - 0.1);
  }

  // The caption pill: two to four words at a time around the voice. Spoken words in ink, the one being said in
  // blue, the ones to come in grey; the pill is sized to its words and centred on the format's caption point.
  function buildCaptions(tl) {
    const size = F.cap.size;
    LINES.forEach((line, li) => {
      const chunks = [];
      let cur = [];
      line.words.forEach((w, i) => {
        cur.push(w);
        const last = i === line.words.length - 1;
        const left = line.words.length - 1 - i;
        if (last || cur.length >= 4 || (/[.,?!:]$/.test(w[0]) && cur.length >= 2 && left !== 1)) {
          chunks.push(cur);
          cur = [];
        }
      });
      // A lone last word joins the chunk before it.
      if (chunks.length > 1 && chunks[chunks.length - 1].length === 1 && chunks[chunks.length - 2].length < 4) chunks[chunks.length - 2].push(...chunks.pop());
      chunks.forEach((c, ci) => {
        const label = c.map((w) => w[0]).join(' ');
        const width = widthOf(label, 700, size) + 64;
        const el = block('pill', F.cap.cx - width / 2, F.cap.cy - (size * 1.5) / 2);
        el.style.width = `${width}px`;
        el.style.fontSize = `${size}px`;
        el.style.height = `${size * 1.5}px`;
        el.style.lineHeight = `${size * 1.5}px`;
        const spans = c.map((w, i) => {
          const s = document.createElement('span');
          s.textContent = w[0];
          el.appendChild(s);
          if (i < c.length - 1) el.appendChild(document.createTextNode(' '));
          return s;
        });
        const start = c[0][1];
        const nextChunk = ci + 1 < chunks.length ? chunks[ci + 1][0][1] : li + 1 < LINES.length ? LINES[li + 1].start : DUR;
        const end = Math.min(nextChunk - 0.03, c[c.length - 1][2] + 0.45);
        tl.fromTo(el, { opacity: 0, y: 10 }, { opacity: 1, y: 0, duration: 0.16, ease: 'power2.out' }, Math.max(0, start - 0.1));
        tl.to(el, { opacity: 0, duration: 0.12, ease: 'power1.in' }, Math.max(start, end - 0.12));
        spans.forEach((s, i) => {
          tl.set(s, { color: BLUE }, c[i][1]);
          if (i + 1 < spans.length) tl.set(s, { color: INK }, c[i + 1][1]);
        });
      });
    });
  }

  // ---- laptop cards, drawn on the canvas in page pixels (EP.cards, ui/beats.js) ----

  // ---- build, then register: fonts and pictures first, so no frame is drawn before they are there ----
  window.__filmReady = (async () => {
    await Promise.all([400, 700, 900].map((w) => document.fonts.load(`${w} 40px Lato`)));
    await EP.loadImages(`${BASE}brand/`);
    const tl = gsap.timeline({ paused: true });
    if (!CUT.still) buildText(tl);
    else CUT.still(layer, F);
    const clock = { t: 0 };
    tl.to(clock, { t: DUR, duration: DUR, ease: 'none', onUpdate: () => drawFrame(clock.t) }, 0);
    drawFrame(0);
    window.__timelines = window.__timelines || {};
    window.__timelines[CUT.id] = tl;
    const q = new URLSearchParams(location.search);
    // A still for checking by eye: ?t=seconds, or ?line=N&f=0..1 for a point through line N (0-based).
    const at = q.has('line') ? ((l) => l.start + parseFloat(q.get('f') || '0.5') * (l.end - l.start))(LINES[+q.get('line')]) : parseFloat(q.get('t'));
    if (q.has('t') || q.has('line')) {
      const t = at;
      tl.seek(t, false);
      drawFrame(t);
    }
  })().catch((e) => {
    // A failed build would otherwise be a silent blank frame: say why where a screenshot or the DOM shows it.
    document.documentElement.dataset.filmError = String(e && e.stack ? e.stack : e);
    throw e;
  });
})();
