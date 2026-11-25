// The 5.x film's beats, second half: demo5 lines 13-24 (Move things, For developers, Small things, Straight talk, Next, Get it)
// and reel5 lines 7-14, keyed by their 1-based number in STORYBOARD.md (beats5.js lists them). Loads after beats5a.js, which creates
// EP.acts5. Every act sets, from its own start, everything it needs (the camera, the phone's turn and screen, the laptop's pose and
// screen) and never leans on what an earlier act left. Each act is one function of word times, so a reel line reuses the demo's
// act at its own times. Everything time-varying is a function of film time t, from EP.ease / EP.seg / EP.lerp.
(function () {
  const { ease, seg, lerp, clamp, fillRR, rr, text, measure } = EP;
  const { PILL, INK, BLUE } = EP.TOK;
  const { demo, reel } = EP.acts5;
  const ramp = (t, a, b) => ease(seg(t, a, b));

  // ---- where the phone, the laptop and the cards stand, per layout (read when an act runs, once EP.F is set) ----
  // 16:9 keeps the phone on the right and the laptop back left; the tall cuts stack them (laptop above, phone below the headline).
  const lay = () => (!EP.F.tall ? '169' : EP.F.cap.cy > 1500 ? '916' : '34');
  const ASIDE = { '169': { x: 1350, y: 910, s: 0.5, r: 0 }, '916': { x: 900, y: 1440, s: 0.46, r: 0 }, '34': { x: 900, y: 1118, s: 0.48, r: 0 } }; // stepped aside
  const SIDE = { x: 1560, y: 700, s: 0.8, r: 0 }; // 16:9 only: small at the right of a centred laptop
  const CARDS = () => ({ '169': EP.F.talk, '916': { x: 72, y: 1260, s: 0.62 }, '34': { x: 72, y: 975, s: 0.55 } })[lay()];
  const phoneAt = (name) => (EP.F.tall ? 'lapPhone' : name);
  // 'rest' is too low for the caption pill with a headline above it, so 16:9 takes a slightly smaller, higher pose.
  const LAP = () => (EP.F.tall ? EP.LAPS.rest : { ...EP.LAPS.rest, x: 600, y: 708, w: 580 });
  const LAP_CENTRE = () => (EP.F.tall ? EP.LAPS.rest : { ...EP.LAPS.rest, x: 960, y: 620, w: 780 });

  // An act's opening state: the phone upright at `cam`, the laptop at `lap`, both arriving as the line begins.
  const stage = (L, D, cam, lap = LAP()) => {
    const t = L.t0 - 0.3;
    D.move(t, cam, 0.9);
    D.turn.to(t, t + 0.6, 0);
    D.lap(t, lap, 0.9);
  };
  // Screen anchors that hold the screen's own clock at 0 until `lt` seconds before `at`, then run it in step: its `lt` lands on `at`.
  const lead = (s, at, lt) => [[s, 0], [Math.max(s + 0.02, at - lt), 0], [at, lt]];
  // The next line's end, so an act can carry a mark across two lines.
  const nextEnd = (L) => window.FILM.timing.lines[L.i + 1].end;

  // ---- Move things: clipboard (ClipboardSheet.kt) ----
  const JUST_NOW = 'Just now'; // strings.xml clip_history_just_now
  const NOTE_LINE = '- dials on every corner';
  const NOTE_TEXT = `Meeting notes\n${NOTE_LINE}`;
  const ROWS = [
    { text: NOTE_LINE, src: 'laptop', age: JUST_NOW },
    { text: 'Meeting notes', src: 'phone', age: JUST_NOW },
    { text: 'Q3 roadmap', src: 'laptop', age: JUST_NOW },
    { text: 'acme/website', src: 'phone', age: JUST_NOW },
  ];
  // The text chip M-FLY carries: the copied line in a pill, centred on the origin.
  const chip = (label) => (ctx) => {
    const w = measure(ctx, label, 24, 'Bold') + 44;
    fillRR(ctx, -w / 2, -26, w, 52, 26, PILL);
    rr(ctx, -w / 2, -26, w, 52, 26);
    ctx.lineWidth = 1.5;
    ctx.strokeStyle = BLUE;
    ctx.stroke();
    text(ctx, label, 0, 8, { size: 24, w: 'Bold', color: INK, align: 'center' });
  };
  // A line is selected and copied in the note window (laptop 'notes'), a chip flies to the phone, which shows it, and one flies back.
  // T: start (the phone's surface), sel (the line is selected), fly (laptop to phone, lands +0.4), back (phone to laptop), hist (the
  // sheet opens on four rows), and for the full act dlg1 / dlg1End (the sheet with the one arrived row, open until dlg1End).
  // Returns the note window's `o`, a function of t, for the caller to hand to D.lapScreen.
  function clipAct(D, T) {
    D.fly(T.fly, 'laptop', 'phone', chip(NOTE_LINE));
    D.fly(T.back, 'phone', 'laptop', chip(NOTE_LINE));
    D.screen(T.start, 'surface');
    if (T.dlg1 !== undefined) {
      D.screen(T.dlg1, 'clipboard', (t) => ({ open: ramp(t, T.dlg1, T.dlg1 + 0.3), rows: ROWS.slice(0, 1) }), [[T.dlg1, 0]]);
      D.screen(T.dlg1End, 'surface');
    }
    D.screen(T.hist, 'clipboard', (t) => ({ open: ramp(t, T.hist, T.hist + 0.3), rows: ROWS }), [[T.hist, 0]]);
    return (t) => ({
      text: NOTE_TEXT,
      sel: ramp(t, T.sel, T.sel + 0.3),
      copied: ramp(t, T.sel + 0.35, T.sel + 0.6) * (1 - ramp(t, T.fly, T.fly + 0.3)),
      pasted: ramp(t, T.back + 0.4, T.back + 0.7),
    });
  }

  // ---- Move things: sending files (XferCard.kt, TransferToast.cs) ----
  const NAMES = ['trip-photos.zip', 'slides.pdf', 'demo.mp4'];
  const PAUSED_AT = 58; // percent the second file has reached when Pause is pressed
  // The transfer at t. T: a (the first file starts), one (it is done, the second starts), watch (the second is at 41%), pause (Pause is
  // pressed and the bar holds), resume, f2 (the second is done, the third starts), f3 (the third is done, then "Sent 3 files").
  function xferAt(T, t) {
    const paused = ramp(t, T.pause, T.pause + 0.12) * (1 - ramp(t, T.resume, T.resume + 0.12));
    const done = ramp(t, T.f3, T.f3 + 0.3);
    if (t < T.one) return { index: 1, pct: 100 * ramp(t, T.a, T.one - 0.05), paused, done };
    if (t < T.f2) {
      const pct = t < T.watch ? 41 * ramp(t, T.one, T.watch) : t < T.pause ? lerp(41, PAUSED_AT, seg(t, T.watch, T.pause)) : t < T.resume ? PAUSED_AT : lerp(PAUSED_AT, 100, ramp(t, T.resume, T.f2));
      return { index: 2, pct, paused, done };
    }
    return { index: 3, pct: 100 * ramp(t, T.f2, T.f3), paused, done };
  }
  // The phone's card (T.s is when its screen starts; the screen's own clock then runs 1:1, so a tap is T.pause - T.s).
  const xferAct = (D, T) => D.screen(T.s, 'xfer', (t) => {
    const x = xferAt(T, t);
    return { pick: ramp(t, T.a - 0.2, T.a), index: x.index, count: 3, name: NAMES[x.index - 1], pct: x.pct, paused: x.paused, done: x.done, pressPause: T.pause - T.s, pressResume: T.resume - T.s };
  }, [[T.s, 0]]);
  // The laptop's toast, mirroring the same numbers (percent only).
  const toastAt = (T) => (t) => {
    const x = xferAt(T, t);
    return { a: ramp(t, T.a, T.a + 0.3), name: NAMES[x.index - 1], index: x.index, count: 3, pct: x.pct, paused: x.paused > 0.5 ? 1 : 0, done: x.done };
  };
  // The whole transfer laid out inside [a, b] by proportion (the reel).
  const xferSpan = (s, a, b) => {
    const d = Math.max(1.8, b - a);
    return { s, a, one: a + 0.17 * d, watch: a + 0.4 * d, pause: a + 0.46 * d, resume: a + 0.62 * d, f2: a + 0.74 * d, f3: a + 0.88 * d };
  };

  // ---- Move things: macros (MacroScreen.kt) and a drawn shape ----
  // The deck (screens.js macros5: tap at lt 0.95, the slide to page two at 2.0), back to the surface, a lightning bolt drawn on the pad
  // (the finger rests until the line turns solid, then strokes it, as A.macros does), and on `runs` the laptop's terminal opens.
  // T: s (deck appears), apps (the tap), web (the slide), surf (surface again), draw (finger down), end (stroke ends), runs.
  function macrosAct(D, T) {
    D.screen(T.s, 'macros5', {}, [[T.s, 0.2], [T.apps, 0.95], [T.web, 2.0], [T.web + 0.55, 2.55]]);
    D.screen(T.surf, 'surface');
    const t0 = T.draw + 0.45, d = Math.max(0.7, T.end - t0);
    D.touch([[T.draw, 225, 320], [t0, 225, 320], [t0 + 0.4 * d, 150, 445], [t0 + 0.6 * d, 220, 455], [t0 + d, 165, 580]]);
    D.shaping.push([t0, t0 + d + 0.1]);
    D.lapScreen(T.s, 'desktop', (t) => ({ terminal: ramp(t, T.runs, T.runs + 0.4) }));
  }

  // ---- For developers (Settings > Developer, DeveloperHub.kt) ----
  const GIT_TAP = 1.2; // the lt at which the Git page's first row is tapped (the finger comes in 0.4 s before)
  // The developer switch, then the hub. T: s (the switch page starts), tap (its tap lands; omit to skip the page), hub (the hub starts;
  // default s), fill (the rings start to fill; omit for full), sess (the first session turns to "Waiting for you"; omit for done),
  // swipe (the pager moves to Git; omit to stay on AI), press (the Git row is tapped; omit for none).
  function devAct(D, T) {
    if (T.tap !== undefined) D.screen(T.s, 'devSwitch', (t) => ({ on: ramp(t, T.tap + 0.05, T.tap + 0.3) }), lead(T.s, T.tap, 0.9));
    const s = T.hub ?? T.s;
    D.screen(s, 'devHub', (t) => ({
      page: T.swipe === undefined ? 0 : ramp(t, T.swipe, T.swipe + 0.45),
      ai: { fill: T.fill === undefined ? 1 : ramp(t, T.fill, T.fill + 1.1), session: T.sess === undefined ? 1 : ramp(t, T.sess, T.sess + 0.5) },
      git: T.press === undefined ? {} : { press: GIT_TAP },
    }), T.press === undefined ? [[s, 0]] : lead(s, T.press, GIT_TAP));
  }

  // ---- Small things: the Air mouse (ControlSurface.kt air mode, M-TILT) ----
  // Where the file and the folder sit on the laptop's desktop, as fractions of its 1280 x 800 screen (laptop.js FILE_AT, FOLDER_AT).
  const AIR_FILE = { x: 790 / 1280, y: 250 / 800 };
  const AIR_FOLDER = { x: 600 / 1280, y: 250 / 800 };
  // The phone tilts +-10 degrees from `tilt` in a few eased swings, the pointer on the laptop following with a 120 ms lag; on `hold` Left
  // is held, the phone tilts the file across to the folder, and Left is released. T: s (screen starts), tilt, hold, end (the line's end).
  function airAct(D, T) {
    const { s, tilt, hold, end } = T;
    D.tilt.to(tilt, tilt + 0.4, 10);
    D.tilt.to(tilt + 0.4, tilt + 0.85, -10);
    D.tilt.to(tilt + 0.85, Math.max(tilt + 1.1, hold - 0.05), 0);
    const d0 = hold + 0.25, d1 = Math.min(hold + 1.05, end - 0.55), rel = d1 + 0.1;
    D.tilt.to(d0, d1, -10);
    D.tilt.to(d1 + 0.15, d1 + 0.5, 0);
    const held = (t) => ramp(t, hold, hold + 0.16) * (1 - ramp(t, rel, rel + 0.16));
    D.screen(s, 'air', (t) => ({ on: ramp(t, s + 0.1, s + 0.5), left: held(t) }));
    D.lapScreen(s, 'desktop', (t) => {
      const tn = D.tilt.at(t - 0.12) / 10, home = D.tilt.at(hold - 0.12) / 10, reach = AIR_FILE.x - AIR_FOLDER.x;
      // Before Left is held the pointer sways about the file and settles on it; after, the lagged tilt carries it to the folder.
      const x = t < hold ? lerp(AIR_FILE.x + tn * reach, AIR_FILE.x, ramp(t, hold - 0.4, hold - 0.05)) : AIR_FILE.x + (tn - home) * reach;
      const k = t < hold ? 0 : t > d1 + 0.12 ? 1 : clamp(-(tn - home));
      return { pointer: { x, y: AIR_FILE.y }, drag: { k, held: held(t) } };
    });
  }

  // ---- Straight talk: plain dark cards and the line from the phone to the laptop ----
  const cards = (items) => EP.cards.rows(items, null, true);
  // The phone steps aside, the laptop stays up at its pose, the cards sit at CARDS(). `line` (t0, t1) draws the phone-laptop line.
  function talkAct(L, D, items, line) {
    stage(L, D, ASIDE[lay()]);
    D.card(L.t0, L.next - 0.05, cards(items), CARDS());
    if (line) D.line(line[0], line[1]);
  }
  // Facing each other, and the unresolved shape glinting on both screens: it rises at `at` and holds to the end of the hold.
  function glintAct(L, D, at) {
    stage(L, D, phoneAt('hero'));
    D.glint.to(at - 0.1, at + 0.6, 1);
    D.glint.to(L.next - 0.5, L.next - 0.1, 0);
  }
  // The end card: the camera moves to 'end' and the laptop leaves.
  const endAct = (L, D) => {
    D.move(L.t0 - 0.4, 'end', 1.3);
    D.lap(L.t0 - 0.4, 'off', 0.6);
  };
  const CLOSE = 'Your laptop, from anywhere *in the room.*';

  // ================= demo5, lines 13-24 =================
  // 13: copy on the laptop, a chip flies to the phone, one flies back, then the history.
  demo[13] = {
    h: 'Copy here. *Paste* there.', s: ['Both *ways*@both', 'With a *history*@history'], lap: true,
    act: (L, D) => {
      stage(L, D, phoneAt('wide'));
      const sel = L.t0 + 0.2, fly = Math.max(L.w('paste') - 0.2, L.t0 + 1.0);
      const back = Math.max(L.w('both') - 0.1, fly + 1.2);
      const hist = Math.max(L.w('history') - 0.2, back + 0.9);
      D.lapScreen(L.t0 - 0.3, 'notes', clipAct(D, { start: L.t0 - 0.3, sel, fly, dlg1: fly + 0.25, dlg1End: back - 0.45, back, hist }));
    },
  };
  // 14: three files, file 1 done by "two", 2 of 3 at 41% around "watch", Pause on "pause", Resume, 100% and "Sent 3 files".
  demo[14] = {
    h: 'Send *files.*', s: ['Either *way*@either', 'Up to *2 GB* each@two', '*Pause* any time@pause'], lap: true,
    act: (L, D) => {
      stage(L, D, phoneAt('wide'));
      const a = L.t0 - 0.05, one = Math.max(L.w('two'), a + 0.8), watch = Math.max(L.w('watch'), one + 0.8), pause = Math.max(L.w('pause'), watch + 0.3);
      const f3 = L.next - 0.4, f2 = f3 - 0.35, resume = Math.max(pause + 0.45, Math.min(pause + 0.7, f2 - 0.3));
      const T = { s: L.t0 - 0.3, a, one, watch, pause, resume, f2, f3 };
      xferAct(D, T);
      D.lapScreen(T.s, 'desktop', (t) => ({ toast: toastAt(T)(t) }));
    },
  };
  // 15: the deck, a bolt drawn on the pad, and the laptop's terminal opens on "runs".
  demo[15] = {
    h: '*Macros.*', s: ['Apps · folders · *websites*@macros', 'Draw a *shape*@draw', 'And it *runs*@runs'], lap: true,
    act: (L, D) => {
      stage(L, D, phoneAt('hero'));
      const apps = L.w('apps'), web = Math.max(L.w('folders') + 0.05, apps + 0.5), surf = Math.max(L.w('draw') - 0.3, web + 0.6), runs = L.w('runs');
      macrosAct(D, { s: L.t0 - 0.3, apps, web, surf, draw: Math.max(L.w('draw'), surf + 0.1), end: runs - 0.1, runs });
    },
  };
  // 16: the developer switch turns on, the AI page fills from "limits" and its first session turns on "session"; the laptop shows code.
  demo[16] = {
    h: 'For *developers.*', s: ['*Limits* and spend@limits', 'Every open *session*@session'], lap: true,
    act: (L, D) => {
      stage(L, D, phoneAt('hero'));
      const tap = L.w('developer');
      devAct(D, { s: L.t0 - 0.3, tap, hub: Math.max(L.w('your') - 0.15, tap + 0.5), fill: L.w('limits') - 0.1, sess: L.w('session') });
      D.lapScreen(L.t0 - 0.3, 'editor');
    },
  };
  // 17: the pager swipes to Git at the line's start, the first request is tapped on "tap" and the laptop's browser opens on "opens".
  demo[17] = {
    h: 'Your *pull requests.*', s: ['GitHub · GitLab · *and more*@more', 'Tap, and it *opens*@opens'], lap: true,
    act: (L, D) => {
      stage(L, D, phoneAt('hero'));
      devAct(D, { s: L.t0 - 0.3, swipe: L.t0 - 0.1, press: L.w('tap') });
      const open = L.w('opens');
      D.lapScreen(L.t0 - 0.3, 'browser-pr', (t) => ({ open: ramp(t, open - 0.15, open + 0.35) }));
    },
  };
  // 18: the Air mouse, the phone tilting, the pointer following, Left held and a file dragged to a folder.
  demo[18] = {
    h: '*Air* mouse.', s: ['*Tilt* to aim@tilt', '*Hold* to drag@hold'], lap: true,
    act: (L, D) => {
      stage(L, D, phoneAt('hero'));
      airAct(D, { s: L.t0 - 0.3, tilt: L.w('tilt'), hold: L.w('hold'), end: L.next });
    },
  };
  // 19: a film's credits roll on a centred laptop, the countdown card on "sleep", then black; the phone's chip is on.
  demo[19] = {
    h: 'Sleep when it *ends.*', lap: true,
    act: (L, D) => {
      const s = L.t0 - 0.3, sleep = L.w('sleep');
      stage(L, D, EP.F.tall ? 'lapPhone' : SIDE, LAP_CENTRE());
      D.roomFx.lamp.to(L.t0 - 0.2, L.t0 + 0.9, 0.6);
      D.screen(s, 'surface');
      D.sleep.to(L.t0, L.t0 + 0.5, 1);
      D.sleep.to(L.next - 0.4, L.next, 0);
      D.lapScreen(s, 'credits', (t) => ({
        roll: 5 + 2.4 * (t - s),
        countdown: { a: ramp(t, sleep - 0.1, sleep + 0.3), secs: 60 - Math.max(0, t - (sleep - 0.1)) },
        black: ramp(t, Math.max(sleep + 1.0, L.next - 1.4), L.next - 0.1),
      }));
    },
  };
  // 20: the phone steps aside, four plain cards, and a line drawn from the phone to the laptop on "straight" (it carries on through line 21).
  demo[20] = {
    h: 'No account. | No *server.*', s: ['*Straight* to your laptop@straight', 'Bluetooth · Wi-Fi · *USB*@bluetooth'], lap: true,
    act: (L, D) => {
      D.screen(L.t0 - 0.3, 'surface');
      D.lapScreen(L.t0 - 0.3, 'desktop');
      talkAct(L, D, [
        { name: 'Straight to your laptop', at: L.w('straight'), hot: true },
        { name: 'Bluetooth', at: L.w('bluetooth') },
        { name: 'Wi-Fi', at: L.w('wifi') },
        { name: 'USB', at: L.w('usb') },
      ], [L.w('straight') - 0.1, nextEnd(L) + 0.1]);
    },
  };
  // 21: the headline stays; Windows tested, Mac and Linux newer, tell us on GitHub. The phone is still aside and the line still drawn.
  demo[21] = {
    h: null, s: ['Windows: tested on *real hardware*@tested', 'Mac and Linux: *newer*@newer', 'Tell us on *GitHub*@github'], lap: true,
    act: (L, D) => {
      D.screen(L.t0 - 0.3, 'surface');
      D.lapScreen(L.t0 - 0.3, 'desktop');
      talkAct(L, D, [
        { name: 'Windows', desc: 'Tested on real hardware', at: L.w('tested'), hot: true },
        { name: 'Mac', desc: 'Newer', at: L.w('mac'), descAt: L.w('newer') },
        { name: 'Linux', desc: 'Newer', at: L.w('linux'), descAt: L.w('newer') },
        { name: 'Tell us on GitHub', at: L.w('tell') },
      ]);
    },
  };
  // 22: phone and laptop face each other; the unresolved shape glints on both on "mirroring" and holds through the silence.
  demo[22] = {
    h: 'Coming in *Edgepad 6.*', s: ['Screen *mirroring*@mirroring', 'Remote *access*@remote'], lap: true,
    act: (L, D) => {
      D.screen(L.t0 - 0.3, 'surface');
      D.lapScreen(L.t0 - 0.3, 'desktop');
      glintAct(L, D, L.w('mirroring'));
    },
  };
  demo[23] = { end: true, act: endAct };
  demo[24] = { close: true, h: CLOSE, act: () => {} };

  // ================= reel5, lines 7-14 =================
  // 7: clipboard, files and macros each at half length inside one line, keyed to "clipboard", "file", "macros" and "shape".
  reel[7] = {
    h: 'Clipboard. Files. *Macros.*', lap: true,
    act: (L, D) => {
      stage(L, D, phoneAt('wide'));
      const s = L.t0 - 0.3, file = L.w('file'), both = L.w('both'), mac = L.w('macros'), shape = L.w('shape'), dr = L.w('draw');
      const notes = clipAct(D, { start: s, sel: L.t0 + 0.05, fly: L.t0 + 0.3, hist: L.t0 + 0.7, back: Math.max(both - 0.4, L.t0 + 1.4) });
      const T = xferSpan(file - 0.25, file - 0.1, mac - 0.4);
      xferAct(D, T);
      D.lapScreen(s, 'notes', (t) => ({ ...notes(t), toast: toastAt(T)(t) }));
      const web = mac + 0.4, surf = Math.max(shape - 0.45, web + 0.6);
      macrosAct(D, { s: mac - 0.3, apps: mac + 0.05, web, surf, draw: Math.max(shape - 0.2, surf + 0.1), end: dr + 0.2, runs: dr + 0.4 });
    },
  };
  // 8: the Air mouse, as demo 18.
  reel[8] = {
    h: '*Air* mouse.', lap: true,
    act: (L, D) => {
      stage(L, D, phoneAt('hero'));
      airAct(D, { s: L.t0 - 0.3, tilt: L.w('tilt'), hold: L.w('hold'), end: L.next });
    },
  };
  // 9: AI then Git in one line: the switch on "developers", the AI page filling on "limits", the pager swiping on "pull", the browser on "glance".
  reel[9] = {
    h: 'For *developers.*', lap: true,
    act: (L, D) => {
      stage(L, D, phoneAt('hero'));
      const tap = L.w('developers'), swipe = L.w('pull') - 0.1;
      const press = Math.max(L.w('requests') + 0.1, swipe + 0.7), open = Math.max(L.w('glance') - 0.2, press + 0.5);
      devAct(D, { s: L.t0 - 0.3, tap, hub: Math.max(L.w('your') - 0.15, tap + 0.5), fill: L.w('limits') - 0.1, sess: L.w('sessions'), swipe, press });
      D.lapScreen(L.t0 - 0.3, 'editor');
      D.lapScreen(open - 0.45, 'browser-pr', (t) => ({ open: ramp(t, open - 0.1, open + 0.4) }));
    },
  };
  // 10: the platforms and the connections as plain cards, and the line from the phone to the laptop on "bluetooth".
  reel[10] = {
    h: 'No account. No *server.*', lap: true,
    act: (L, D) => {
      D.screen(L.t0 - 0.3, 'surface');
      D.lapScreen(L.t0 - 0.3, 'desktop');
      talkAct(L, D, [
        { name: 'Windows', at: L.w('windows'), hot: true },
        { name: 'Mac', at: L.w('mac') },
        { name: 'Linux', at: L.w('linux') },
        { name: 'Bluetooth', at: L.w('bluetooth') },
        { name: 'Wi-Fi', at: L.w('wifi') },
        { name: 'USB', at: L.w('usb') },
      ], [L.w('bluetooth') - 0.1, nextEnd(L) + 0.1]);
    },
  };
  // 11: the headline stays; Mac and Linux newer, tell us on GitHub. The line from line 10 is still drawn.
  reel[11] = {
    h: null, lap: true,
    act: (L, D) => {
      D.screen(L.t0 - 0.3, 'surface');
      D.lapScreen(L.t0 - 0.3, 'desktop');
      talkAct(L, D, [
        { name: 'Mac', desc: 'Newer', at: L.w('mac'), descAt: L.w('newer') },
        { name: 'Linux', desc: 'Newer', at: L.w('linux'), descAt: L.w('newer') },
        { name: 'Tell us on GitHub', at: L.w('tell') },
      ]);
    },
  };
  // 12: as demo 22 (STORYBOARD.md gives this line no headline, so it takes that one).
  reel[12] = {
    h: 'Coming in *Edgepad 6.*', lap: true,
    act: (L, D) => {
      D.screen(L.t0 - 0.3, 'surface');
      D.lapScreen(L.t0 - 0.3, 'desktop');
      glintAct(L, D, L.w('mirroring'));
    },
  };
  reel[13] = { end: true, act: endAct };
  reel[14] = { close: true, h: CLOSE, act: () => {} };
})();
