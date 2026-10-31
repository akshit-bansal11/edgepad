// Shared drawing helpers: easing, colours, text, the icons (the app's own, res/drawable/ic_*.xml), the Edgepad mark, and the app's Perimeter.
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
  const DARK = { bg: '#0E0F12', card: '#1B1C20', line: '#2B2D33', faint: '#24262B', ink: '#F2F3F5', dim: '#9A9CA3', accent: '#4DA3FF', onAccent: '#0B1A2E', accentSoft: '#1B2A3D', off: '#3A3C43', danger: '#FF6961' };
  const LIGHT = { bg: '#F2F3F7', card: '#FFFFFF', line: '#E3E5EA', faint: '#EBEDF2', ink: '#16171A', dim: '#6B6E76', accent: '#0068D6', onAccent: '#FFFFFF', accentSoft: '#E6F0FC', off: '#E3E5EA', danger: '#D70015' };
  EP.mixRgb = (a, b, k) => a.map((v, i) => v + (b[i] - v) * EP.clamp(k));
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

  // ---- Edgepad's own icons: res/drawable/ic_*.xml, 24 x 24 grid, stroke 1.5, round caps and joins. Keyed by the drawable's
  // name without ic_. The camelCase names are kept for the callers that predate them. ----
  const APP = {
    bluetooth: 'M6.75 7.5L16.75 16.25L12 20.5V3.5L16.75 7.75L6.75 16.5',
    check: 'M4.75 12.75L9.5 17.25L19.25 6.75',
    chevron_down: 'M5.75 9L12 15.25L18.25 9',
    chevron_left: 'M15 5.75L8.75 12L15 18.25',
    chevron_right: 'M9 5.75L15.25 12L9 18.25',
    chevron_up: 'M5.75 15L12 8.75L18.25 15',
    clear: 'M3.75 6.25H20.25M9.25 6.25V4.75A1 1 0 0 1 10.25 3.75H13.75A1 1 0 0 1 14.75 4.75V6.25M5.75 6.25L6.6 19.1A1.5 1.5 0 0 0 8.1 20.5H15.9A1.5 1.5 0 0 0 17.4 19.1L18.25 6.25M10 10.25V16.5M14 10.25V16.5',
    close: 'M6 6L18 18M18 6L6 18',
    controls: 'M5 3.5H9A1.5 1.5 0 0 1 10.5 5V9A1.5 1.5 0 0 1 9 10.5H5A1.5 1.5 0 0 1 3.5 9V5A1.5 1.5 0 0 1 5 3.5ZM15 3.5H19A1.5 1.5 0 0 1 20.5 5V9A1.5 1.5 0 0 1 19 10.5H15A1.5 1.5 0 0 1 13.5 9V5A1.5 1.5 0 0 1 15 3.5ZM5 13.5H9A1.5 1.5 0 0 1 10.5 15V19A1.5 1.5 0 0 1 9 20.5H5A1.5 1.5 0 0 1 3.5 19V15A1.5 1.5 0 0 1 5 13.5ZM15 13.5H19A1.5 1.5 0 0 1 20.5 15V19A1.5 1.5 0 0 1 19 20.5H15A1.5 1.5 0 0 1 13.5 19V15A1.5 1.5 0 0 1 15 13.5Z',
    laptop: 'M5.5 5H18.5A1 1 0 0 1 19.5 6V15.5H4.5V6A1 1 0 0 1 5.5 5ZM2.5 18.5H21.5',
    lock: 'M6.5 10.5H17.5A1.5 1.5 0 0 1 19 12V19A1.5 1.5 0 0 1 17.5 20.5H6.5A1.5 1.5 0 0 1 5 19V12A1.5 1.5 0 0 1 6.5 10.5ZM8 10.5V7.5A4 4 0 0 1 16 7.5V10.5M12 14.5V16.5',
    macro: 'M13.25 3L5.25 13.5H11.75L10.75 21L18.75 10.5H12.25Z',
    music: 'M9 17.5V6L19 4V15.5M4.75 17.5A2.25 2.25 0 1 0 9.25 17.5A2.25 2.25 0 1 0 4.75 17.5ZM14.75 15.5A2.25 2.25 0 1 0 19.25 15.5A2.25 2.25 0 1 0 14.75 15.5Z',
    pause: 'M7.75 5.5H9.25A0.75 0.75 0 0 1 10 6.25V17.75A0.75 0.75 0 0 1 9.25 18.5H7.75A0.75 0.75 0 0 1 7 17.75V6.25A0.75 0.75 0 0 1 7.75 5.5ZM14.75 5.5H16.25A0.75 0.75 0 0 1 17 6.25V17.75A0.75 0.75 0 0 1 16.25 18.5H14.75A0.75 0.75 0 0 1 14 17.75V6.25A0.75 0.75 0 0 1 14.75 5.5Z',
    play: 'M8.5 5.75V18.25L19 12Z',
    refresh_cw: 'M19.5 12A7.5 7.5 0 1 1 17.3 6.7L19.25 8.75M19.25 4.5V8.75H15',
    settings: 'M18.76 10.19L21.16 10.71L21.16 13.29L18.76 13.81A7 7 0 0 1 18.06 15.50L19.39 17.57L17.57 19.39L15.50 18.06A7 7 0 0 1 13.81 18.76L13.29 21.16L10.71 21.16L10.19 18.76A7 7 0 0 1 8.50 18.06L6.43 19.39L4.61 17.57L5.94 15.50A7 7 0 0 1 5.24 13.81L2.84 13.29L2.84 10.71L5.24 10.19A7 7 0 0 1 5.94 8.50L4.61 6.43L6.43 4.61L8.50 5.94A7 7 0 0 1 10.19 5.24L10.71 2.84L13.29 2.84L13.81 5.24A7 7 0 0 1 15.50 5.94L17.57 4.61L19.39 6.43L18.06 8.50A7 7 0 0 1 18.76 10.19ZM12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6Z',
    sign: 'M3.5 20H20.5M3.75 16C5.5 15.5 6.5 6.5 8.75 6.5C10.75 6.5 8.5 16 10.75 16C12.25 16 12.75 11.5 14.25 11.5C15.75 11.5 15 15 16.5 15C17.75 15 18.75 13.75 20.25 13.75',
    skip_back: 'M18.5 6.5V17.5L10 12ZM6 6V18',
    skip_forward: 'M5.5 6.5V17.5L14 12ZM18 6V18',
    sliders_horizontal: 'M3 7.5H12.75M17.25 7.5H21M3 16.5H6.75M11.25 16.5H21M15 5.25A2.25 2.25 0 1 0 15 9.75A2.25 2.25 0 1 0 15 5.25ZM9 14.25A2.25 2.25 0 1 0 9 18.75A2.25 2.25 0 1 0 9 14.25Z',
    tool_arrow: 'M5 19L18.5 5.5M9.75 5.5H18.5V14.25',
    tool_ellipse: 'M3.25 12A8.75 6.75 0 1 0 20.75 12A8.75 6.75 0 1 0 3.25 12Z',
    tool_highlighter: 'M8.75 12.75L16 5.5A1.5 1.5 0 0 1 18.12 5.5L20.5 7.88A1.5 1.5 0 0 1 20.5 10L13.25 17.25ZM8.75 12.75L7.25 17.75L8.25 18.75L13.25 17.25M3.5 20.5H9',
    tool_line: 'M5.5 16.75A1.75 1.75 0 1 0 5.5 20.25A1.75 1.75 0 1 0 5.5 16.75ZM18.5 3.75A1.75 1.75 0 1 0 18.5 7.25A1.75 1.75 0 1 0 18.5 3.75ZM6.75 17.25L17.25 6.75',
    tool_pen: 'M4.5 19.5L6.75 14.75L16.25 5.25A1.75 1.75 0 0 1 18.75 7.75L9.25 17.25ZM6.75 14.75L9.25 17.25',
    tool_rectangle: 'M4.5 5H19.5A1.5 1.5 0 0 1 21 6.5V17.5A1.5 1.5 0 0 1 19.5 19H4.5A1.5 1.5 0 0 1 3 17.5V6.5A1.5 1.5 0 0 1 4.5 5Z',
    undo: 'M8.75 14.5L4.25 10L8.75 5.5M4.25 10H14.5A5 5 0 0 1 14.5 20H11',
  };
  APP.skipBack = APP.skip_back;
  APP.skipForward = APP.skip_forward;
  APP.chevron = APP.chevron_left;

  // ---- Lucide icons (ISC), 24 x 24, stroke 2: only for what the app has no icon of (the laptop's side, the Controls stencils). ----
  const LUCIDE = {
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
    volume: 'M11 4.702a.705.705 0 0 0-1.203-.498L6.413 7.587A1.4 1.4 0 0 1 5.416 8H3a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2.416a1.4 1.4 0 0 1 .997.413l3.383 3.384A.705.705 0 0 0 11 19.298z M16 9a5 5 0 0 1 0 6 M19.364 18.364a9 9 0 0 0 0-12.728',
    sun: 'M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8z M12 2v2 M12 20v2 M4.93 4.93l1.41 1.41 M17.66 17.66l1.41 1.41 M2 12h2 M20 12h2 M6.34 17.66l-1.41 1.41 M19.07 4.93l-1.41 1.41',
    download: 'M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M7 10l5 5 5-5 M12 15V3',
    bluetooth: 'M7 7l10 10-5 5V2l5 5L7 17',
    smartphone: 'M7 2h10a2 2 0 0 1 2 2v16a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2z M12 18h.01',
    unlink: 'M18.84 12.25l1.72-1.71h-.02a5.004 5.004 0 0 0-.12-7.07 5.006 5.006 0 0 0-6.95 0l-1.72 1.71 M5.17 11.75l-1.71 1.71a5.004 5.004 0 0 0 .12 7.07 5.006 5.006 0 0 0 6.95 0l1.71-1.71 M8 2v3 M2 8h3 M16 19v3 M19 16h3',
    power: 'M12 2v10 M18.4 6.6a9 9 0 1 1-12.77.04',
    laptop: 'M20 16V7a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v9m16 0H4m16 0 1.28 2.55a1 1 0 0 1-.9 1.45H3.62a1 1 0 0 1-.9-1.45L4 16',
    shield: 'M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z',
  };
  const ICONS = { ...LUCIDE, ...APP };
  const paths = {};
  // Draws icon `name` centred on (cx, cy), `size` px square, stroked in `color`. The app's icons stroke 1.5, Lucide's 2.
  EP.icon = (ctx, name, cx, cy, size, color, a = 1, filled = false) => {
    const p = (paths[name] ||= new Path2D(ICONS[name]));
    ctx.save();
    ctx.translate(cx - size / 2, cy - size / 2);
    ctx.scale(size / 24, size / 24);
    ctx.lineWidth = name in APP ? 1.5 : 2;
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

  // ---- images: loaded once, before the timeline is registered, so a frame never waits on one. ----
  // name -> [file, width / height]; the logos are the app's own res/raw SVGs, the marks assets/edgepad-*.svg.
  const IMAGES = {
    youtube: ['logo_youtube.svg', 256 / 180],
    spotify: ['logo_spotify.svg', 1],
    markLight: ['edgepad-light.svg', 1],
    markDark: ['edgepad-dark.svg', 1],
  };
  EP.img = {};
  // `base` is the brand/ folder relative to the page.
  EP.loadImages = (base) =>
    Promise.all(
      Object.entries(IMAGES).map(([name, [file, aspect]]) => {
        const im = new Image();
        im.src = base + file;
        EP.img[name] = { im, aspect };
        return im.decode().catch(() => undefined);
      }),
    );
  // An image fitted inside a size x size box centred on (cx, cy).
  EP.image = (ctx, name, cx, cy, size, a = 1) => {
    const e = EP.img[name];
    if (!e || !e.im.complete || !e.im.naturalWidth) return false;
    const w = e.aspect >= 1 ? size : size * e.aspect;
    const h = e.aspect >= 1 ? size / e.aspect : size;
    const g = ctx.globalAlpha;
    ctx.globalAlpha = g * a;
    ctx.drawImage(e.im, cx - w / 2, cy - h / 2, w, h);
    ctx.globalAlpha = g;
    return true;
  };
  // AppLogo.kt: the playing app's logo, or a music note when there is none.
  EP.appLogo = (ctx, app, cx, cy, size, pal) => {
    const name = (app || '').toLowerCase();
    const key = name.includes('spotify') ? 'spotify' : name.includes('youtube') ? 'youtube' : null;
    if (!key || !EP.image(ctx, key, cx, cy, size)) EP.icon(ctx, 'music', cx, cy, size * 0.8, pal.ink);
  };

  // ---- brand marks for the macro deck (ui/icons.js, Simple Icons), filled in a colour at `size` px. ----
  const brandPaths = {};
  EP.brand = (ctx, name, cx, cy, size, color, a = 1) => {
    const d = window.EP_BRANDS && window.EP_BRANDS[name];
    if (!d) return;
    const p = (brandPaths[name] ||= new Path2D(d));
    ctx.save();
    ctx.globalAlpha *= a;
    ctx.translate(cx - size / 2, cy - size / 2);
    ctx.scale(size / 24, size / 24);
    ctx.fillStyle = color;
    ctx.fill(p);
    ctx.restore();
  };

  // ---- time: tracks of eased moves, and warps that map film time onto a screen's own choreography ----
  EP.easeInOut = (t) => {
    t = EP.clamp(t);
    return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
  };
  // A track is a start value and a list of moves [t0, t1, value]: from t0 to t1 it eases from wherever it was
  // to `value`. Values are numbers or objects of numbers (a camera). Moves are kept sorted by t0.
  EP.Track = class {
    constructor(v0, ease = EP.easeInOut) {
      this.v0 = v0;
      this.moves = [];
      this.ease = ease;
    }
    to(t0, t1, v) {
      this.moves.push([t0, Math.max(t1, t0 + 1e-3), v]);
      this.moves.sort((a, b) => a[0] - b[0]);
      return this;
    }
    set(t, v) {
      return this.to(t, t + 1e-3, v);
    }
    at(t) {
      let v = this.v0;
      for (const [t0, t1, to] of this.moves) {
        if (t < t0) break;
        const k = this.ease((t - t0) / (t1 - t0));
        v = mix(v, to, k);
      }
      return v;
    }
  };
  function mix(a, b, k) {
    if (typeof b === 'number') return EP.lerp(typeof a === 'number' ? a : 0, b, k);
    if (typeof b !== 'object' || b === null) return k < 0.5 ? a : b;
    const o = {};
    for (const key of Object.keys(b)) o[key] = mix(a && key in a ? a[key] : b[key], b[key], k);
    return o;
  }
  // Piecewise-linear map through anchors [[t, local], ...], with slope 1 outside them.
  EP.warp = (t, anchors) => {
    if (t <= anchors[0][0]) return anchors[0][1] + (t - anchors[0][0]);
    for (let i = 0; i + 1 < anchors.length; i++) {
      const [a, la] = anchors[i], [b, lb] = anchors[i + 1];
      if (t <= b) return la + ((t - a) / (b - a)) * (lb - la);
    }
    const [a, la] = anchors[anchors.length - 1];
    return la + (t - a);
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
