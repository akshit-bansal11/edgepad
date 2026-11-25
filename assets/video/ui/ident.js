// The dial ident (M-IDENT): a port of scripts/logo-motion.ps1's dial maths onto the 1024 logo canvas, drawn on its tile.
(() => {
  const { clamp, TOK } = EP;
  const P = 881.8, R_ARC = 288, R_IN = 424, R_OUT = 656, R_NEEDLE = 720, W = 88, TOTAL = 2650;
  // pop order (arc, tick-1, tick-2, needle, tick-3, tick-4); angle in degrees, r1 the outer end, blue for the arc and the needle
  const PARTS = [
    { a: 180, r1: R_ARC, blue: true }, { a: 180, r1: R_OUT }, { a: 202.5, r1: R_OUT },
    { a: 225, r1: R_NEEDLE, blue: true }, { a: 247.5, r1: R_OUT }, { a: 270, r1: R_OUT },
  ];
  const DRAG = [[900, 0], [1750, 150], [2150, 122], [2450, 142]];
  const LOOP_SPEED = 22.5 / 1000; // deg per ms after the end: 67.5 deg every 3 s, so it always passes through rest positions
  const TILE = new Path2D('M354.65 0H669.35C771.47 0 822.53 0 868.58 15.2C937.64 37.99 986.01 86.36 1008.8 155.42C1024 201.47 1024 252.53 1024 354.65V669.35C1024 771.47 1024 822.53 1008.8 868.58C986.01 937.64 937.64 986.01 868.58 1008.8C822.53 1024 771.47 1024 669.35 1024H354.65C252.53 1024 201.47 1024 155.42 1008.8C86.36 986.01 37.99 937.64 15.2 868.58C0 822.53 0 771.47 0 669.35V354.65C0 252.53 0 201.47 15.2 155.42C37.99 86.36 86.36 37.99 155.42 15.2C201.47 0 252.53 0 354.65 0Z');
  const seg = (t, start, dur) => clamp((t - start) / dur, 0, 1);
  const out = (x) => 1 - Math.pow(1 - x, 3);
  const inOut = (x) => (x < 0.5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2);
  const spring = (x) => (x >= 1 ? 1 : 1 - Math.exp(-6 * x) * Math.cos(9 * x));

  // The ruler's drag angle in degrees at `ms` (waypoints, then the snap to a multiple of 67.5 deg). Held at the end.
  const scroll = (ms) => {
    const t = Math.min(ms, TOTAL);
    if (t <= DRAG[0][0]) return 0;
    for (let i = 1; i < DRAG.length; i++) {
      const [t0, a0] = DRAG[i - 1], [t1, a1] = DRAG[i];
      if (t <= t1) return a0 + (a1 - a0) * inOut((t - t0) / (t1 - t0));
    }
    const last = DRAG[DRAG.length - 1][1], snapped = Math.round(last / 67.5) * 67.5;
    return last + (snapped - last) * out(seg(t, 2450, 180));
  };
  EP.identAngle = (ms) => scroll(ms);

  // Everything one frame needs: the pop parts {k, op}, the ruler ticks {a, op, blue} (empty before 900 ms), the press, the angle.
  const state = (ms, loop) => {
    const pop = PARTS.map((_, i) => {
      const x = seg(ms, i * 60, 420);
      return { k: 0.55 + 0.45 * spring(x), op: clamp(x / 0.25, 0, 1) };
    });
    const ruler = [];
    let held = 0, off = 0;
    if (ms >= 900) {
      off = scroll(ms) + (loop && ms > TOTAL ? LOOP_SPEED * (ms - TOTAL) : 0);
      if (!loop || ms <= TOTAL) held = clamp(seg(ms, 860, 80) - seg(ms, 2450, 140), 0, 1);
      for (let k = Math.ceil((-11.25 - off) / 22.5); k <= Math.floor((101.25 - off) / 22.5); k++) {
        const a = 180 + 22.5 * k + off;
        const op = clamp((a - 168.75) / 11.25, 0, 1) * clamp((281.25 - a) / 11.25, 0, 1);
        if (op > 0) ruler.push({ a, op, blue: (((k % 3) + 3) % 3) === 2 });
      }
    }
    return { pop, ruler, held, angle: off };
  };

  const stroke = (ctx, a, r0, r1, op, blue, w) => {
    if (op <= 0.001 || Math.abs(r1 - r0) < 0.01) return; // a zero-length round-capped stroke would draw a dot
    const c = Math.cos(a * Math.PI / 180), s = Math.sin(a * Math.PI / 180);
    ctx.globalAlpha = op;
    ctx.strokeStyle = blue ? TOK.IDENT_ACCENT : TOK.IDENT_INK;
    ctx.lineWidth = w;
    ctx.beginPath();
    ctx.moveTo(P + r0 * c, P + r0 * s);
    ctx.lineTo(P + r1 * c, P + r1 * s);
    ctx.stroke();
  };

  // EP.ident(ctx, x, y, size, ms, o): the dial ident on its tile, top-left (x, y), `size` px square, `ms` since its start.
  // ms < 0 draws nothing. 0-760 ms the pop: the arc, then each tick in angle order 60 ms apart, springing out from the pivot.
  // From 900 ms a ruler (a tick every 22.5 deg, every third one long and blue, visible 168.75-281.25 deg) is dragged by a
  // thumb (forward, back, forward), gets thicker while pressed, and snaps by whole 67.5 deg repeats on release (ends 2630 ms).
  // After 2650 ms the final mark holds. o fields: a (alpha, default 1); loop (default false): after 2650 ms the ruler keeps
  // scrolling 67.5 deg every 3 s, seamlessly, with held = 0, so the mark returns to the logo every 3 s.
  EP.ident = (ctx, x, y, size, ms, o = {}) => {
    if (ms < 0) return;
    const { pop, ruler, held } = state(ms, !!o.loop);
    ctx.save();
    ctx.globalAlpha *= o.a === undefined ? 1 : o.a;
    ctx.translate(x, y);
    ctx.scale(size / 1024, size / 1024);
    ctx.fillStyle = TOK.IDENT_TILE;
    ctx.fill(TILE);
    ctx.clip(TILE);
    ctx.lineCap = 'round';
    const base = ctx.globalAlpha;
    if (pop[0].op > 0.001) {
      ctx.globalAlpha = base * pop[0].op;
      ctx.strokeStyle = TOK.IDENT_ACCENT;
      ctx.lineWidth = W;
      ctx.beginPath();
      ctx.arc(P, P, R_ARC * pop[0].k, Math.PI, 1.5 * Math.PI);
      ctx.stroke();
    }
    if (ms < 900) pop.forEach((p, i) => i > 0 && stroke(ctx, PARTS[i].a, R_IN * p.k, PARTS[i].r1 * p.k, base * p.op, !!PARTS[i].blue, W)); // from 900 ms the ruler takes the ticks over
    ruler.forEach((r) => stroke(ctx, r.a, R_IN, r.blue ? R_NEEDLE : R_OUT, base * r.op, r.blue, W + 8 * held));
    ctx.restore();
  };

  // Dev check (called by nobody at render time): the state at the four times the SMIL reference is compared at.
  EP.identCheck = () => [500, 1200, 2000, 2650].map((ms) => {
    const s = state(ms, false);
    return { ms, parts: s.pop.map((p, i) => ({ k: p.k, opacity: ms >= 900 && i > 0 ? 0 : p.op })), ruler: s.ruler, angle: s.angle };
  });
})();
