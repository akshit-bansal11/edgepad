// The 5.x film's beats, first half: demo5 lines 1-12 (Hook, The surface, Type and play, Present and capture) and reel5 lines 1-6, in
// STORYBOARD.md's order. Each entry is { h, s, lay, lap, act } as in beats.js; EP.acts5.demo[n] / EP.acts5.reel[n] are read by
// beats5.js. Acts set their own camera, turn, screen and laptop (pose and screen) at their start and never rely on an earlier act, except
// between their own consecutive lines. Every moving value is a function of film time, from word times alone: no state, no randomness.
// The reel's acts are the demo's, factored into the functions below and compressed.
(function () {
  const { ease, seg, lerp, clamp, fillRR, rr } = EP;
  const C = EP.cards;
  const { TOK } = EP;
  EP.acts5 = { demo: {}, reel: {} };
  const DEMO = EP.acts5.demo, REEL = EP.acts5.reel;

  // ---- layout-aware pieces (EP.F, EP.LAPS and EP.CAM exist once film.js has run, so they are read inside acts only) ----
  const tall = () => Boolean(EP.F.tall);
  // The laptop's screen rect per pose. In 16:9 these keep it clear of the headline, the sub-lines, the phone and the caption pill
  // (centred y 990): rest sits under a headline on `lap`, big is the left two thirds under a `top` headline, side the left of a sideways
  // phone on the right, split the laptop of the volume sync. The tall cuts use the director's presets.
  const POSE = { rest: { x: 600, y: 670, w: 640, a: 1 }, big: { x: 580, y: 530, w: 1000, a: 1 }, side: { x: 480, y: 570, w: 760, a: 1 }, split: { x: 640, y: 640, w: 720, a: 1 } };
  const TALL_POSE = { rest: 'rest', big: 'race', side: 'race', split: 'split' };
  const P = (name) => (tall() ? EP.LAPS[TALL_POSE[name]] : POSE[name]);
  // Cameras: the sideways phone on the right of the laptop (16:9 uses landRight, the laptop would sit under `land`), the same pushed in,
  // and the upright phone on the right clear of a `top` headline.
  // With the laptop up the tall cuts use the director's lap* presets (below the headline band); 16:9 keeps `plain`.
  const cam = (plain, lap) => (tall() ? lap : plain);
  const SIDE = () => cam('landRight', 'lapLand');
  const PUSH = () => cam({ x: 1360, y: 575, s: 1.12, r: -90 }, 'lapLand');
  const UP = () => cam({ x: 1500, y: 610, s: 1.0, r: 0 }, 'lapFoot');
  // A beat whose headline takes the top layout in 16:9 (a sideways phone, or the laptop big below it); the tall cuts use `lap` instead.
  const top = (b) => Object.defineProperty(b, 'lay', { get: () => (EP.F.tall ? undefined : 'top'), enumerable: true });

  // ---- small helpers ----
  // A point eased through keys [[t, x, y], ...], held before the first and after the last.
  const along = (keys, t) => {
    if (t <= keys[0][0]) return { x: keys[0][1], y: keys[0][2] };
    for (let i = 1; i < keys.length; i++) {
      const [a, ax, ay] = keys[i - 1], [b, bx, by] = keys[i];
      if (t <= b) {
        const k = ease(seg(t, a, b));
        return { x: lerp(ax, bx, k), y: lerp(ay, by, k) };
      }
    }
    const last = keys[keys.length - 1];
    return { x: last[1], y: last[2] };
  };
  // A chain of resizes [[time, value], ...] from `start`: the pair in play and how far through its 280 ms reflow it is. k0 is the
  // reflow before the first step (0 shows a screen's own free positions, 1 the settled `to`).
  const chain = (steps, start, k0 = 1) => (t) => {
    let from = start, to = start, k = k0;
    for (const [s, v] of steps) if (t >= s) {
      from = to;
      to = v;
      k = seg(t, s, s + 0.28);
    }
    return { from, to, k };
  };
  // Steps at the given [time, value] pairs, each at least 0.32 s after the one before so no reflow cuts another short.
  const spaced = (pairs) => {
    let last = -Infinity;
    return pairs.map(([t, v]) => [(last = Math.max(t, last + 0.32)), v]);
  };
  // `str` typed between start and end: the keys [[time, label], ...] the keyboard lights, and the text on the laptop at t.
  const typing = (str, start, end) => {
    const step = clamp((end - start) / str.length, 0.03, 0.11);
    const keys = [...str].map((ch, i) => [start + i * step, ch === ' ' ? 'Space' : ch === '\n' ? 'Enter' : ch]);
    return { keys, textAt: (t) => str.slice(0, keys.filter(([k]) => t >= k + 0.05).length) };
  };
  const attract = (t) => ({ mode: 'attract', tr: t });
  // Where a card of natural size cw x ch sits on the laptop's screen at pose p: centred, at most 70% of the screen wide, near its foot.
  const onScreen = (p, cw, ch) => {
    const s = Math.min(1, (0.7 * p.w) / cw);
    return { x: p.x - (cw * s) / 2, y: p.y + p.w / 3.2 - ch * s - 0.04 * p.w, s };
  };
  // The dp on the phone's pad as a point on the laptop's screen (a fraction of it).
  const padPtr = (p) => ({ x: 0.25 + 0.5 * (p.x / 390), y: 0.3 + 0.45 * (p.y / 844) });
  const GLIDE = (g) => [[g, 150, 340], [g + 0.25, 196, 380], [g + 0.5, 262, 450], [g + 0.7, 222, 540]];

  // ---- 1 and 2: the room, then the phone rises into the foreground ----
  // The room far across, the laptop on its stand playing the race in attract mode, the dolly under way at frame 0 and settling on
  // `across`; with o.tv the screen is mirrored onto the TV on "TV" and the projector's beam fades in on "projector".
  const hook = (L, D, o) => {
    D.lap(0, P('rest'), 0.01);
    D.lapScreen(0, 'race', attract);
    D.dolly.to(-1.2, L.w(o.across) + 0.4, 1);
    if (o.tv) {
      D.roomFx.tv.to(L.w('tv'), L.w('tv') + 0.4, 1);
      D.roomFx.beam.to(L.w('projector'), L.w('projector') + 0.4, 1);
    }
  };
  // The pull back (up to 1.4 s, landing on `word`): the room goes, the phone rises in a hand, the laptop stays small in the back.
  const pullBack = (L, D, word) => {
    const at = L.w(word), m0 = Math.max(at - 1.4, L.prev + 0.02);
    D.screen(m0, 'surface');
    D.move(m0, cam('hero', 'lapPhone'), at - m0);
    D.room.to(m0, at, 1);
    D.hand.to(m0, at, 1);
    D.dolly.to(m0, at, 0);
    D.lap(m0, P('rest'), at - m0);
    D.lapScreen(m0, 'race', attract);
  };
  // Demo 1. No headline for the first seconds: it arrives on "across".
  // The tall cuts keep line 1's headline at the top of the wall, clear of the TV, the beam and the stand (no `lap`).
  const lap169 = (b) => Object.defineProperty(b, 'lap', { get: () => !EP.F.tall, enumerable: true });
  DEMO[1] = lap169({ h: 'Across the *room.*@across', act: (L, D) => hook(L, D, { across: 'across', tv: true }) });
  DEMO[2] = { h: "You're *over here.*", lap: true, act: (L, D) => pullBack(L, D, 'here') };
  REEL[1] = {
    h: 'Across the *room.*',
    lap: true,
    act: (L, D) => {
      hook(L, D, { across: 'across' });
      pullBack(L, D, 'hand');
    },
  };

  // ---- 3: Meet Edgepad ----
  // The ident grows out of the phone on "This" and hands off to the top-left dial at the line's end; a glide on "trackpad", then the phone itself
  // turns sideways on "keyboard" and "gamepad" and back upright on "remote", and with o.os three dark OS chips rising under "Windows · Mac · Linux". The laptop fades out for the line: in 16:9
  // the chips take its place under the sub-lines, in the tall cuts the ident takes its band.
  const meet = (L, D, o) => {
    const t0 = L.t0, tp = L.w('trackpad'), kb = L.w('keyboard'), gp = L.w('gamepad'), rm = L.w('remote');
    // Poses per layout. 16:9: the ident above the headline, the sideways phone right of the headline and sub-lines (they wrap at 800 px
    // from x 150). Tall cuts (no laptop): the ident under the headline, the phone below it; with o.os (sub-lines and OS chips) the
    // ident sits under the chips row and the phone is lowered to make room, both upright and sideways.
    const wide916 = EP.F.head.y === 262;
    let upright = EP.CAM.hero, sideways = { x: 1450, y: 540, s: 0.85, r: -90 }, ident = { x: EP.F.head.x + 110, y: 120, size: 220 }, chips = null;
    if (tall() && wide916) {
      upright = o.os ? { x: 540, y: 1230, s: 0.9, r: 0 } : { x: 540, y: 1100, s: 1.1, r: 0 };
      sideways = o.os ? { x: 540, y: 1220, s: 1.0, r: -90 } : { x: 540, y: 1150, s: 1.1, r: -90 };
      ident = o.os ? { x: 540, y: 690, size: 200 } : { x: 540, y: 490, size: 220 };
      chips = { x: 80, y: 510, s: 1 };
    } else if (tall()) {
      upright = o.os ? { x: 540, y: 960, s: 0.85, r: 0 } : EP.CAM.hero;
      sideways = o.os ? { x: 540, y: 950, s: 0.95, r: -90 } : EP.CAM.land;
      ident = o.os ? { x: 540, y: 450, size: 180 } : { x: 540, y: 290, size: 200 };
      chips = { x: 72, y: 260, s: 1 };
    }
    D.move(t0 - 0.1, upright, 0.9);
    D.hand.to(t0, t0 + 0.4, 0);
    D.lap(t0 - 0.1, { ...P('rest'), a: 0 }, 0.4);
    D.ident(t0, ident);
    D.handoff(L.t1 - 0.25, 'VOL');
    D.screen(t0 - 0.1, 'surface');
    D.touch([[tp - 0.05, 160, 380], [tp + 0.25, 210, 420], [tp + 0.5, 250, 470]]);
    // The app shows these two sideways, so the phone itself turns (0.4 s, landing on the word) for the keyboard, stays turned as the
    // keyboard cross-fades (0.3 s) to the gamepad, and turns back upright (0.4 s, landing on "remote"). The hand is gone by then.
    D.turn.to(kb - 0.4, kb, 1);
    D.move(kb - 0.4, sideways, 0.4);
    D.screen(kb - 0.4, 'keyboard', { demo: false }, null, true, 0.3);
    D.screen(gp - 0.15, 'gamepad', {}, null, true, 0.3);
    D.turn.to(rm - 0.4, rm, 0);
    D.move(rm - 0.4, upright, 0.4);
    D.screen(rm - 0.3, 'surface', {}, null, false, 0.3);
    if (o.os) {
      const items = [{ name: 'Windows', at: L.w('windows') }, { name: 'macOS', at: L.w('mac') }, { name: 'Linux', at: L.w('linux') }];
      // 16:9: dark rows under the sub-lines. Tall: a row of chips between the sub-line and the ident.
      D.card(L.w('windows') - 0.2, L.t1 + 0.2, tall() ? C.chips(items) : C.rows(items, null, true), chips);
    }
  };
  // The headline breaks after "This is" in 16:9 only; the tall cuts keep it on one line so the ident clears it.
  const meetHead = (b) => Object.defineProperty(b, 'h', { get: () => (EP.F.tall ? 'This is *Edgepad 5.*' : 'This is | *Edgepad 5.*'), enumerable: true });
  DEMO[3] = meetHead({
    s: ['Free and *open source*@free', 'Trackpad · keyboard · *gamepad* · remote@trackpad', 'Windows · Mac · *Linux*@windows'],
    act: (L, D) => meet(L, D, { os: true }),
  });
  REEL[2] = meetHead({ act: (L, D) => meet(L, D, { os: false }) });

  // ---- 4 and 5: the trackpad, then the dials ----
  // The four rulers light in turn, clockwise from the top left, 90 ms apart, from `at`.
  const lightRulers = (D, at) => ['VOL', 'BRI', 'ZOOM', 'SCRUB'].forEach((kind, i) => D.armed.push([at + i * 0.09, at + 0.9, kind]));
  // A one-finger glide on the pad from `g`, the laptop's pointer on its desktop (shown from `at`) following it and zooming with the
  // ZOOM dial.
  const glide = (D, g, at) => {
    const keys = GLIDE(g);
    D.touch(keys);
    D.lapScreen(at, 'desktop', (t) => ({ pointer: padPtr(along(keys, t)), zoom: 1 + 0.2 * D.zoom.at(t) }));
  };
  DEMO[4] = {
    h: ['The middle is a *trackpad.*', 'The corners are *dials.*@corners'],
    lap: true,
    act: (L, D) => {
      const m = L.w('multitouch'), tp = L.w('trackpad'), c = L.w('corners');
      D.screen(L.t0 - 0.2, 'surface');
      D.move(L.t0 - 0.2, cam('hero', 'lapPhone'), 0.6);
      D.lap(L.t0 - 0.2, P('rest'), 0.5);
      glide(D, m, L.t0 - 0.1);
      // The two-finger scroll once the glide has lifted.
      const s = Math.max(tp, m + 0.8);
      D.touch([[s, 160, 380], [s + 0.15, 160, 380], [s + 0.6, 160, 540], [s + 0.7, 160, 540]], false);
      D.touch([[s, 232, 380], [s + 0.15, 232, 380], [s + 0.6, 232, 540], [s + 0.7, 232, 540]], false);
      D.move(c - 0.52, cam('tl', 'lapTl'), 0.52);
      lightRulers(D, c);
    },
  };
  // Each dial in turn: the camera lands on its corner as it is named, a finger slides it along its edge, the laptop shows the level
  // (Volume, Brightness on the laptop's screen), the desktop zooms 1.0 -> 1.6 with the ZOOM dial and a clip scrubs with the SCRUB dial.
  // w = { vol, bri, zoom, sc } are the word times; the first move takes `lead` seconds.
  const dials = (L, D, w, lead = 0.52) => {
    const span = (a, b) => Math.max(a + 0.3, Math.min(a + 0.9, b - 0.6));
    D.move(w.vol - lead, cam('tl', 'lapTl'), lead);
    D.move(w.bri - 0.52, cam('tr', 'lapTr'), 0.52);
    D.move(w.zoom - 0.52, cam('br', 'lapBr'), 0.52);
    D.move(w.sc - 0.52, cam('bl', 'lapBl'), 0.52);
    D.dial('VOL', w.vol, span(w.vol, w.bri), 63);
    D.dial('BRI', w.bri, span(w.bri, w.zoom), 82);
    D.dial('ZOOM', w.zoom, span(w.zoom, w.sc), 3);
    D.scrub(w.sc + 0.05, w.sc + 0.8, 74);
    const pos = onScreen(P('rest'), 448, 135);
    D.card(w.vol - 0.2, w.bri - 0.45, C.level('Volume', (t) => D.vol.at(t)), pos);
    D.card(w.bri - 0.1, w.zoom - 0.35, C.level('Brightness', (t) => D.bri.at(t)), pos);
    D.lapScreen(w.sc - 0.05, 'video', (t) => ({ pos: 0.25 + 0.5 * (D.scrubOff.at(t) / 74) }));
  };
  DEMO[5] = {
    h: 'Slide to *set.*',
    s: ['*Volume*@volume', '*Brightness*@brightness', '*Zoom*@zoom', '*Scrub*@scrub', 'Four become *eight*@split'],
    lap: true,
    act: (L, D) => {
      D.screen(L.t0 - 0.3, 'surface');
      D.lap(L.t0 - 0.3, P('rest'), 0.4);
      D.lapScreen(L.t0 - 0.1, 'desktop', (t) => ({ pointer: { x: 0.5, y: 0.45 }, zoom: 1 + 0.2 * D.zoom.at(t) }));
      dials(L, D, { vol: L.w('volume'), bri: L.w('brightness'), zoom: L.w('zoom'), sc: L.w('scrub') });
      // "Split the corners": Settings > Corners over the phone, the switch flips, and the surface shows eight halves.
      const sp = L.w('split'), cr = L.w('corners'), bc = L.w('become'), eight = L.w('eight');
      D.move(sp - 0.6, cam('hero', 'lapPhone'), 0.6);
      D.screen(sp - 0.35, 'corners', {}, [[sp - 0.35, 0.3], [cr + 0.2, 1.0], [bc - 0.3, 1.5]]);
      D.screen(bc - 0.3, 'surface');
      D.split.to(bc - 0.3, bc + 0.1, 1);
      D.extra.to(eight - 0.3, eight + 0.1, 1);
    },
  };
  REEL[3] = {
    h: 'Dials on every *corner.*@a',
    lap: true,
    act: (L, D) => {
      D.screen(L.t0 - 0.2, 'surface');
      D.move(L.t0 - 0.2, cam('hero', 'lapPhone'), 0.6);
      D.lap(L.t0 - 0.2, P('rest'), 0.5);
      glide(D, L.w('multitouch'), L.t0 - 0.1);
      lightRulers(D, L.w('corner'));
      dials(L, D, { vol: L.w('volume'), bri: L.w('brightness'), zoom: L.w('zoom'), sc: L.w('scrub') }, 0.8);
    },
  };

  // ---- 6: volume syncs both ways ----
  // The laptop's volume flyout is dragged by a pointer from 30 to 70 from `from` to `to`; the phone's volume ruler follows it 120 ms
  // behind. The camera shows the laptop and the phone's top-left corner side by side. With o.split the four dials are eight (as the line
  // before left them) and go back to four as the line ends.
  const sync = (L, D, o) => {
    const p = P('split'), a = L.w(o.from) - 0.2, b = L.w(o.to) + 0.3;
    const pos = onScreen(p, 520, 120);
    const knob = (v) => ({ x: pos.x + pos.s * (88 + 3.32 * v), y: pos.y + pos.s * 72 });
    D.move(L.t0 - 0.3, cam({ x: 1449, y: 924, s: 1.3, r: 0 }, 'lapTl'), 0.9);
    D.lap(L.t0 - 0.3, p, 0.9);
    D.lapScreen(L.t0 - 0.3, 'desktop', {});
    D.vol.to(L.t0 - 0.35, L.t0 - 0.05, 30);
    D.dial('VOL', a + 0.12, b + 0.12, 70, false);
    // The laptop leads the phone by 120 ms. The flyout covers the laptop's own cursor, so the card carries a pointer of its own that
    // glides in to the knob and rides it.
    const flyout = C.winVolume((t) => D.vol.at(t + 0.12));
    D.card(L.t0 + 0.1, L.next - 0.1, (ctx, t, al, at) => {
      flyout(ctx, t, al, at);
      const k = knob(D.vol.at(t + 0.12)), enter = ease(seg(t, a - 0.6, a));
      ctx.save();
      ctx.globalAlpha *= al;
      ctx.translate(k.x + 60 * (1 - enter) * at.s, k.y + 80 * (1 - enter) * at.s + (1 - al) * 16);
      ctx.scale(1.5 * at.s, 1.5 * at.s);
      ctx.beginPath();
      ctx.moveTo(0, 0);
      ctx.lineTo(0, 17);
      ctx.lineTo(4.5, 13);
      ctx.lineTo(8, 21);
      ctx.lineTo(11, 19.5);
      ctx.lineTo(7.5, 12);
      ctx.lineTo(13, 12);
      ctx.closePath();
      ctx.fillStyle = TOK.INK;
      ctx.fill();
      ctx.lineWidth = 1.5;
      ctx.strokeStyle = TOK.PHONE;
      ctx.stroke();
      ctx.restore();
    }, pos);
    if (o.split) {
      D.split.to(L.t0 - 0.35, L.t0 - 0.05, 1);
      D.extra.to(L.t0 - 0.35, L.t0 - 0.05, 1);
      D.extra.to(L.next - 0.35, L.next + 0.05, 0);
      D.split.to(L.next - 0.2, L.next + 0.5, 0);
    }
  };
  DEMO[6] = { h: 'Volume *syncs* both ways.@change', lap: true, act: (L, D) => sync(L, D, { from: 'laptop', to: 'moves', split: true }) };
  REEL[4] = { h: '*Syncs* both ways.', lap: true, act: (L, D) => sync(L, D, { from: 'laptop', to: 'follows', split: false }) };

  // ---- 7: Tap Controls ----
  // The camera lands on the top row as "Controls" is said and the popup opens from the button within 300 ms; it then eases out to the
  // whole phone so the tiles show. Keyboard is tapped (screens.js controls: lt 2.2) as the next line begins.
  DEMO[7] = {
    h: 'Tap *Controls.*',
    lap: true,
    act: (L, D) => {
      const c = L.w('controls');
      D.turn.to(L.t0 - 0.3, L.t0, 0);
      D.lap(L.t0 - 0.3, P('rest'), 0.5);
      D.lapScreen(L.t0 - 0.3, 'desktop', {});
      D.move(Math.max(c - 0.7, L.prev + 0.02), cam('top', 'lapPhone'), Math.min(0.7, c - L.prev - 0.02));
      D.move(c + 0.3, cam('right', 'lapPhone'), 0.9);
      D.screen(c - 0.15, 'controls', {}, [[c - 0.15, 0], [c + 0.45, 0.6], [L.next - 0.05, 2.2]]);
    },
  };

  // ---- 8: the keyboard ----
  // The phone turns (600 ms) to the keyboard at 75%: Ctrl latched, Shift held (the 4.0 choreography). On the second "full" Keyboard
  // settings > Size picks 100% and the board morphs to it (280 ms), then 80, 65 and 60 from "compact"; text types into the laptop's note
  // while the board reflows. The tall cuts show the same.
  const NOTE = 'Meeting notes\n- dials on every corner';
  DEMO[8] = top({
    h: 'A full *keyboard.*',
    s: ['*Six* sizes@six', 'Full, with a *number pad*@number', 'Down to *60%*@compact'],
    lap: true,
    act: (L, D) => {
      const t = L.t0 - 0.1, f2 = L.w('full', 2) - 0.1, k2 = f2 + 0.75;
      D.turn.to(t, t + 0.6, 1);
      D.move(t, SIDE(), 0.6);
      D.lap(t, P('side'), 0.9);
      D.screen(t, 'keyboard', {}, [[t, 0.3]], true);
      D.screen(f2, 'keyboardSettings', (tt) => ({ sel: lerp(3, 0, ease(seg(tt, f2 + 0.35, f2 + 0.65))), tap: [0.3, 0] }), [[f2, 0]], true);
      const steps = spaced([[k2 + 0.35, 100], [L.w('compact') - 0.05, 80], [L.w('sixty') - 0.05, 65], [L.w('percent') - 0.05, 60]]);
      const typed = typing(NOTE, k2 + 0.4, L.t1 - 0.3);
      const rel = typed.keys.map(([a, label]) => [a - k2, label]);
      const board = chain(steps, 75);
      D.screen(k2, 'keyboard', (tt) => ({ ...board(tt), demo: false, keys: rel }), [[k2, 0]], true);
      D.lapScreen(t, 'notes', (tt) => ({ text: typed.textAt(tt), caret: true }));
    },
  });
  REEL[5] = top({
    h: 'Keyboard. *Gamepad.*',
    lap: true,
    act: (L, D) => {
      const t = L.t0 - 0.1, six = L.w('six'), g = L.w('gamepad');
      D.turn.to(t, t + 0.6, 1);
      D.move(t, SIDE(), 0.6);
      D.lap(t, P('side'), 0.9);
      const board = chain(spaced([[six - 0.05, 100], [L.w('sizes') + 0.1, 60]]), 75);
      const typed = typing('Meeting notes', t + 0.7, g - 0.4);
      const rel = typed.keys.map(([a, label]) => [a - t, label]);
      D.screen(t, 'keyboard', (tt) => ({ ...board(tt), demo: false, keys: rel }), [[t, 0]], true);
      D.lapScreen(t, 'notes', (tt) => ({ text: typed.textAt(tt), caret: true }));
      // Then the gamepad and the race at 0.85 x, held through the 3 s of silence after the line.
      D.screen(g - 0.25, 'gamepad', (tt) => ({ preset: 'xbox', input: EP.game.input(D.raceAt(tt)) }), null, true, 0.4);
      D.race(g, 0.85);
      D.lapScreen(g - 0.25, 'race', { mode: 'race' });
      D.lap(g - 0.25, P('big'), 0.9);
      D.move(g - 0.5, cam('raceSplit', 'lapLand'), 0.8);
    },
  });

  // ---- 9: the gamepad and the race ----
  // The pad shows the Xbox preset with the game's own input table (EP.game.input), so the stick and triggers move as the car does; the
  // race starts on "two" (the green light) while the camera lands on the raceSplit framing, the laptop big behind the sideways phone.
  // The headline dims to 40% over the 2 s hold after the line.
  DEMO[9] = top({
    h: 'A real *gamepad.*@a',
    s: ['Two sticks · *analog* triggers@analog', 'Windows and *Linux*@windows', 'Seen as an *Xbox controller*@xbox'],
    lap: true,
    act: (L, D) => {
      const two = L.w('two');
      D.turn.to(L.t0 - 0.2, L.t0 + 0.4, 1);
      D.move(L.t0 - 0.2, SIDE(), 0.4);
      D.screen(L.t0 - 0.2, 'gamepad', (t) => ({ preset: 'xbox', input: EP.game.input(D.raceAt(t)) }), null, true, 0.4);
      D.race(two);
      D.lapScreen(L.t0 - 0.2, 'race', { mode: 'race' });
      D.lap(L.t0 - 0.2, P('big'), 0.9);
      D.move(two - 0.6, cam('raceSplit', 'lapLand'), 0.6);
      D.dim(L.t1, L.next - 0.3);
    },
  });

  // ---- 10: the layout editor ----
  // The editor on the Xbox preset: RT is picked up and dragged to the centre lines (lt 0.6-1.7, on "drag"), the options list opens at
  // "start", and the checked layout moves Racing -> Platformer -> Shooter on their words, the controls reflowing in 280 ms each. The
  // race still runs on the laptop beside the pushed-in phone.
  DEMO[10] = top({
    h: 'Your *layout.*@drag',
    s: ['*Drag* anything@drag', 'Racing · Platformer · *Shooter*@racing'],
    lap: true,
    act: (L, D) => {
      const t = L.t0 - 0.2, dr = L.w('drag'), st = L.w('start');
      D.turn.to(t, t + 0.4, 1);
      D.move(t, PUSH(), 0.9);
      D.lap(t, P('side'), 0.9);
      D.lapScreen(t, 'race', { mode: 'race' });
      const steps = spaced([[L.w('racing'), 'racing'], [L.w('platformer'), 'platformer'], [L.w('shooter'), 'shooter']]);
      const names = ['xbox', 'xboxKeys', 'platformer', 'racing', 'shooter'];
      const layout = chain(steps, 'xbox', 0);
      D.screen(t, 'gamepadLayout', (tt) => {
        const m = layout(tt);
        return { ...m, drag: tt < steps[1][0] - 0.01, list: ease(seg(tt, st, st + 0.3)), pick: lerp(names.indexOf(m.from), names.indexOf(m.to), ease(m.k)) };
      }, [[t, 0.2], [dr, 0.6], [dr + 1.0, 1.6], [dr + 1.15, 1.7]], true);
    },
  });

  // ---- 11 and 12: present, then capture ----
  // The laser: the head at the thumb's point and a trail of the same path sampled back over 250 ms, oldest first (fractions of the slide).
  const laserAt = (path, l0, l1, t) => {
    if (t < l0 || t > l1) return null;
    const head = along(path, t);
    const trail = Array.from({ length: 9 }, (_, i) => {
      const age = 1 - i / 8, q = along(path, Math.max(l0, t - 0.25 * age));
      return { x: q.x, y: q.y, age };
    });
    return { x: head.x, y: head.y, trail };
  };
  // The phone upright on the Presenter screen (Next tapped at o.next, the slide changing in 300 ms) and a thumb on the pad from o.l0 to
  // o.l1 drawing the laser on the laptop's slide deck. o.start is when the screens change over.
  const present = (L, D, o) => {
    const s0 = o.start, len = Math.max(1, o.l1 - o.l0), end = o.l0 + len;
    const path = [[0.2, 0.45], [0.42, 0.6], [0.62, 0.42], [0.8, 0.58], [0.55, 0.72]].map(([x, y], i) => [o.l0 + (len * i) / 4, x, y]);
    D.turn.to(s0, s0 + 0.6, 0);
    D.move(s0, UP(), 0.9);
    D.lap(s0, P('big'), 0.9);
    D.screen(s0, 'presenter', (t) => ({ next: [o.next - s0], thumb: t >= o.l0 && t <= end ? along(path, t) : null }), [[s0, 0]]);
    D.lapScreen(s0, 'slides', (t) => ({ slide: seg(t, o.next, o.next + 0.3), laser: laserAt(path, o.l0, end, t) }));
  };
  // The thumbnail that flies from the laptop's screen to the phone.
  const thumb = (ctx) => {
    fillRR(ctx, -64, -40, 128, 80, 10, TOK.PILL);
    rr(ctx, -64, -40, 128, 80, 10);
    ctx.lineWidth = 2;
    ctx.strokeStyle = TOK.INK;
    ctx.stroke();
    fillRR(ctx, -48, -24, 70, 10, 5, TOK.BLUE);
    fillRR(ctx, -48, -4, 96, 8, 4, TOK.GREY);
    fillRR(ctx, -48, 12, 60, 8, 4, TOK.GREY);
  };
  // Capture: Region tapped, the plain drag on the pad growing the laptop's region rectangle to match, Full screen (an 80 ms white flash),
  // Record (a red dot pulsing top right), and on `ph` the thumbnail arcs from the laptop into the phone. o = { t0, full, rec, ph }.
  const capture = (L, D, o) => {
    D.turn.to(o.t0, o.t0 + 0.4, 0);
    D.move(o.t0, UP(), 0.6);
    D.lap(o.t0, P('big'), 0.9);
    // Region tapped (lt 1.0), the drag (1.9-3.4) done 0.9 s before Full screen so the rectangle holds 0.8 s, and gone 0.1 s before it.
    const g1 = Math.max(o.full - 0.9, o.t0 + 0.5), g0 = Math.max(o.t0 + 0.25, g1 - 0.6);
    D.screen(o.t0, 'capture', { phone: true }, [[o.t0, 0.4], [g0, 1.9], [g1, 3.4], [o.full, 4.0], [o.rec, 5.0]]);
    const entry = D.screens[D.screens.length - 1];
    D.lapScreen(o.t0, 'slides', (t) => {
      const lt = EP.warp(t, entry.anchors), k = ease(seg(lt, 2.2, 3.4));
      return {
        slide: 1,
        region: lt >= 1.9 && t < o.full - 0.1 ? { x0: 0.2, y0: 0.25, x1: lerp(0.22, 0.78, k), y1: lerp(0.28, 0.72, k) } : null,
        flash: Math.max(0, 1 - Math.abs(t - (o.full + 0.04)) / 0.04),
        rec: ease(seg(t, o.rec, o.rec + 0.2)),
      };
    });
    D.fly(o.ph, 'laptop', 'phone', thumb);
  };
  DEMO[11] = top({
    h: 'Present from *anywhere.*',
    s: ['*Next* and previous@presenter', 'A *laser* that follows you@laser'],
    lap: true,
    act: (L, D) => present(L, D, { start: L.t0 - 0.2, next: L.w('steps'), l0: L.w('laser'), l1: L.t1 }),
  });
  DEMO[12] = top({
    h: '*Capture* it.@capture',
    s: ['A *region*@region', 'The *full screen*@full', 'A *recording*@recording', 'Straight to your *phone*@phone'],
    lap: true,
    act: (L, D) => capture(L, D, { t0: L.t0 - 0.35, full: L.w('full'), rec: L.w('recording'), ph: L.w('phone') }),
  });
  // Reel 6: the presenter (Next on "presenter", the laser from "laser") then the capture, compressed into one line.
  REEL[6] = top({
    h: '*Present.* Capture.',
    lap: true,
    act: (L, D) => {
      const ss = L.w('screenshots'), rec = L.w('recordings');
      present(L, D, { start: L.t0 - 0.3, next: L.w('presenter') + 0.3, l0: L.w('laser'), l1: ss - 1.3 });
      capture(L, D, { t0: ss - 1.3, full: ss, rec, ph: L.w('phone') });
    },
  });
})();
