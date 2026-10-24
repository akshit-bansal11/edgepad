// Shared drawing helpers: easing, colours, text, Lucide icons, the Edgepad mark, and the app's Perimeter.
// Every number that comes from the Android app says where: Palette.kt, Space/Type in Palette.kt, Perimeter.kt.
(function () {
  const EP = (window.EP = {});

  EP.clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
  EP.lerp = (a, b, t) => a + (b - a) * t;
  // Smoothstep: the S curve used for every move, so nothing starts or stops abruptly.
  EP.ease = (t) => {
    t = EP.clamp(t);
    return t * t * (3 - 2 * t);
  };
  EP.easeOut = (t) => 1 - Math.pow(1 - EP.clamp(t), 3);
  // Progress of t between a and b, clamped to 0..1.
  EP.seg = (t, a, b) => EP.clamp((t - a) / (b - a));

  // ---- colour. values-night/colors.xml is the dark set, values/colors.xml the light one. ----
  const hex = (h) => [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16));
  const DARK = { bg: '#0E0F12', card: '#1B1C20', line: '#2B2D33', faint: '#24262B', ink: '#F2F3F5', dim: '#9A9CA3', accent: '#4DA3FF', onAccent: '#0B1A2E', accentSoft: '#1B2A3D', off: '#3A3C43' };
  const LIGHT = { bg: '#F2F3F7', card: '#FFFFFF', line: '#E3E5EA', faint: '#EBEDF2', ink: '#16171A', dim: '#6B6E76', accent: '#0068D6', onAccent: '#FFFFFF', accentSoft: '#E6F0FC', off: '#E3E5EA' };
  EP.rgba = (c, a = 1) => `rgba(${Math.round(c[0])},${Math.round(c[1])},${Math.round(c[2])},${a})`;
  // k = 0 the dark theme, 1 the light one; in between is a cross-fade, for the theme switch.
  EP.palette = (k) => {
    const p = {};
    for (const name of Object.keys(DARK)) {
      const a = hex(DARK[name]);
      const b = hex(LIGHT[name]);
      p[name] = a.map((v, i) => v + (b[i] - v) * k);
    }
    p.k = k;
    return p;
  };
  // The page the phone stands on: ground #F2F3F7, ink #16171A, accent #0068D6, secondary #6B6E76.
  EP.page = { ground: '#F2F3F7', ink: '#16171A', accent: '#0068D6', dim: '#6B6E76' };

  // ---- text ----
  const WEIGHT = { Regular: 400, Bold: 700, Black: 900 };
  EP.text = (ctx, s, x, y, o) => {
    const g = ctx.globalAlpha;
    ctx.globalAlpha = g * (o.a ?? 1);
    ctx.font = `${WEIGHT[o.w || 'Regular']} ${o.size}px Lato, sans-serif`;
    ctx.fillStyle = typeof o.color === 'string' ? o.color : EP.rgba(o.color);
    ctx.textAlign = o.align || 'left';
    ctx.textBaseline = o.base || 'alphabetic';
    ctx.letterSpacing = `${(o.track || 0) * o.size}px`;
    ctx.fillText(s, x, y);
    ctx.letterSpacing = '0px';
    ctx.globalAlpha = g;
  };
  EP.measure = (ctx, s, size, w = 'Regular') => {
    ctx.font = `${WEIGHT[w]} ${size}px Lato, sans-serif`;
    return ctx.measureText(s).width;
  };
  // Text centred on (x, y) the way the app centres a line: the visual middle sits CAP_CENTRE of the size above the baseline.
  EP.textMid = (ctx, s, x, y, o) => EP.text(ctx, s, x, y + o.size * 0.35, o);

  EP.rr = (ctx, x, y, w, h, r) => {
    ctx.beginPath();
    ctx.roundRect(x, y, w, h, Math.max(0, Math.min(r, w / 2, h / 2)));
  };
  EP.fillRR = (ctx, x, y, w, h, r, color, a = 1) => {
    EP.rr(ctx, x, y, w, h, r);
    ctx.fillStyle = typeof color === 'string' ? color : EP.rgba(color, a);
    ctx.fill();
  };

  // ---- Lucide icons (ISC), 24 x 24, stroke 2: the app's own icon set. ----
  const ICONS = {
    settings: 'M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0z',
    dashboard: 'M4 3h5a1 1 0 0 1 1 1v7a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z M15 3h5a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1h-5a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z M15 12h5a1 1 0 0 1 1 1v7a1 1 0 0 1-1 1h-5a1 1 0 0 1-1-1v-7a1 1 0 0 1 1-1z M4 16h5a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-3a1 1 0 0 1 1-1z',
    grid: 'M4 3h5a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z M15 3h5a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1h-5a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z M15 14h5a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1h-5a1 1 0 0 1-1-1v-5a1 1 0 0 1 1-1z M4 14h5a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-5a1 1 0 0 1 1-1z',
    lock: 'M5 11h14a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2z M7 11V7a5 5 0 0 1 10 0v4',
    skipBack: 'M19 20 9 12l10-8v16z M5 19V5',
    skipForward: 'M5 4l10 8-10 8V4z M19 5v14',
    play: 'M6 3l14 9-14 9V3z',
    pause: 'M7 4h2a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z M15 4h2a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1h-2a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z',
    keyboard: 'M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z M6 8h.01 M10 8h.01 M14 8h.01 M18 8h.01 M8 12h.01 M12 12h.01 M16 12h.01 M7 16h10',
    gamepad: 'M6 11h4 M8 9v4 M15 12h.01 M18 10h.01 M17.32 5H6.68a4 4 0 0 0-3.978 3.59c-.006.052-.01.101-.017.152C2.604 9.416 2 14.456 2 16a3 3 0 0 0 3 3c1 0 1.5-.5 2-1l1.414-1.414A2 2 0 0 1 9.828 16h4.344a2 2 0 0 1 1.414.586L17 18c.5.5 1 1 2 1a3 3 0 0 0 3-3c0-1.545-.604-6.584-.685-7.258-.007-.05-.011-.1-.017-.151A4 4 0 0 0 17.32 5z',
    scan: 'M3 7V5a2 2 0 0 1 2-2h2 M17 3h2a2 2 0 0 1 2 2v2 M21 17v2a2 2 0 0 1-2 2h-2 M7 21H5a2 2 0 0 1-2-2v-2',
    pen: 'M12 20h9 M16.376 3.622a1 1 0 0 1 3.002 3.002L7.368 18.635a2 2 0 0 1-.855.506l-2.872.838a.5.5 0 0 1-.62-.62l.838-2.872a2 2 0 0 1 .506-.854z',
    music: 'M9 18V5l12-2v13 M9 18a3 3 0 1 1-6 0 3 3 0 0 1 6 0z M21 16a3 3 0 1 1-6 0 3 3 0 0 1 6 0z',
    chevron: 'M15 18l-6-6 6-6',
    globe: 'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20z M2 12h20 M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z',
    note: 'M15 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-5-5z M15 3v5h5 M8 13h8 M8 17h5',
    calc: 'M6 2h12a2 2 0 0 1 2 2v16a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2z M8 6h8 M8 11h.01 M12 11h.01 M16 11h.01 M8 15h.01 M12 15h.01 M16 15h.01 M8 19h.01 M12 19h.01 M16 19h.01',
    docs: 'M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6z M14 2v6h6 M16 13H8 M16 17H8 M10 9H8',
    terminal: 'M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z M7 9l3 3-3 3 M13 15h4',
    image: 'M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M9 8.5a1.5 1.5 0 1 1-3 0 1.5 1.5 0 0 1 3 0z M21 15l-3.086-3.086a2 2 0 0 0-2.828 0L6 21',
    check: 'M20 6L9 17l-5-5',
  };
  const paths = {};
  // Draws icon `name` centred on (cx, cy), `size` px square, stroked in `color`.
  EP.icon = (ctx, name, cx, cy, size, color, a = 1, filled = false) => {
    const p = (paths[name] ||= new Path2D(ICONS[name]));
    ctx.save();
    ctx.translate(cx - size / 2, cy - size / 2);
    ctx.scale(size / 24, size / 24);
    ctx.lineWidth = 2;
    ctx.lineCap = ctx.lineJoin = 'round';
    ctx.strokeStyle = typeof color === 'string' ? color : EP.rgba(color, a);
    ctx.fillStyle = ctx.strokeStyle;
    if (filled) ctx.fill(p);
    ctx.stroke(p);
    ctx.restore();
  };

  // ---- the Edgepad mark (assets/edgepad-light.svg and -dark.svg), drawn at `size` px with its top-left at (x, y). ----
  const TILE = new Path2D('M354.65 0H669.35C771.47 0 822.53 0 868.58 15.2C937.64 37.99 986.01 86.36 1008.8 155.42C1024 201.47 1024 252.53 1024 354.65V669.35C1024 771.47 1024 822.53 1008.8 868.58C986.01 937.64 937.64 986.01 868.58 1008.8C822.53 1024 771.47 1024 669.35 1024H354.65C252.53 1024 201.47 1024 155.42 1008.8C86.36 986.01 37.99 937.64 15.2 868.58C0 822.53 0 771.47 0 669.35V354.65C0 252.53 0 201.47 15.2 155.42C37.99 86.36 86.36 37.99 155.42 15.2C201.47 0 252.53 0 354.65 0Z');
  const TICKS = new Path2D('M394 744H224M420.6 610.1L263.6 545M610.1 420.6L545 263.6M744 394V224');
  const ARC = new Path2D('M496.5 496.5L348 348M504 744A240 240 0 0 1 744 504');
  EP.mark = (ctx, x, y, size, dark, a = 1) => {
    ctx.save();
    ctx.globalAlpha *= a;
    ctx.translate(x, y);
    ctx.scale(size / 1024, size / 1024);
    ctx.fillStyle = dark ? '#1B1C20' : '#FFFFFF';
    ctx.fill(TILE);
    ctx.transform(1.2, 0, 0, 1.2, -102.4, -102.4);
    ctx.lineWidth = 72;
    ctx.lineCap = 'round';
    ctx.strokeStyle = dark ? '#F2F3F5' : '#16171A';
    ctx.stroke(TICKS);
    ctx.strokeStyle = dark ? '#4DA3FF' : '#0068D6';
    ctx.stroke(ARC);
    ctx.restore();
  };

  // ---- Perimeter.kt: a rounded rectangle walked clockwise from the left end of the top edge. ----
  EP.Perimeter = class {
    constructor(width, height, radius) {
      this.width = width;
      this.height = height;
      const r = (this.radius = Math.max(1, Math.min(radius, Math.min(width, height) / 2)));
      const top = width - 2 * r;
      const side = height - 2 * r;
      const arc = (Math.PI / 2) * r;
      this.arc = arc;
      this.length = 2 * top + 2 * side + 4 * arc;
      this.cornerMid = [-arc / 2, top + arc / 2, top + arc + side + arc / 2, 2 * top + 2 * arc + side + arc / 2, this.length - arc / 2];
      const H = Math.PI / 2;
      let start = 0;
      this.pieces = [];
      const line = (x, y, ux, uy, len) => {
        this.pieces.push({ start, len, line: true, x, y, ux, uy });
        start += len;
      };
      const corner = (cx, cy, from) => {
        this.pieces.push({ start, len: arc, cx, cy, from });
        start += arc;
      };
      line(r, 0, 1, 0, top);
      corner(width - r, r, -H);
      line(width, r, 0, 1, side);
      corner(width - r, height - r, 0);
      line(width - r, height, -1, 0, top);
      corner(r, height - r, H);
      line(0, height - r, 0, -1, side);
      corner(r, r, Math.PI);
    }
    wrap(s) {
      return ((s % this.length) + this.length) % this.length;
    }
    lengthAt(at) {
      const w = ((at % 4) + 4) % 4;
      const k = Math.floor(w);
      return this.wrap(this.cornerMid[k] + (w - k) * (this.cornerMid[k + 1] - this.cornerMid[k]));
    }
    // Unwrapped on purpose: callers interpolate between positions, and a wrap would jump.
    slotAt(slot, offset) {
      const sign = slot % 2 === 0 ? -1 : 1;
      return this.cornerMid[Math.floor(slot / 2)] + sign * (this.arc / 2 + offset);
    }
    // x, y and the inward unit normal nx, ny at length s.
    point(s, out) {
      const d = this.wrap(s);
      let p = this.pieces[this.pieces.length - 1];
      for (const q of this.pieces) {
        if (d < q.start + q.len) {
          p = q;
          break;
        }
      }
      const t = Math.min(d - p.start, p.len);
      if (p.line) {
        out[0] = p.x + p.ux * t;
        out[1] = p.y + p.uy * t;
        out[2] = -p.uy;
        out[3] = p.ux;
      } else {
        const ang = p.from + t / this.radius;
        out[0] = p.cx + this.radius * Math.cos(ang);
        out[1] = p.cy + this.radius * Math.sin(ang);
        out[2] = -Math.cos(ang);
        out[3] = -Math.sin(ang);
      }
    }
    // Distance from the nearest bend: 0 on an arc, else to the nearer end of its straight edge.
    fromBend(s) {
      const d = this.wrap(s);
      for (const q of this.pieces) {
        if (d < q.start + q.len) return q.line ? Math.min(d - q.start, q.start + q.len - d) : 0;
      }
      return 0;
    }
  };
})();
