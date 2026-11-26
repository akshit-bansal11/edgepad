// The laptop cards (EP.cards), drawn on the page in the laptop's own look, for the Edgepad 5 beats (beats5a.js, beats5b.js).
// window.BEATS starts empty here; beats5.js fills it. No latency figure appears anywhere.
(function () {
  const { text, fillRR, rr, icon, ease, seg, clamp } = EP;
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
  };

  EP.cards = C; // the laptop cards, for the 5.x beats (beats5a.js, beats5b.js)
  window.BEATS = {}; // beats5.js adds demo5 and reel5
})();
