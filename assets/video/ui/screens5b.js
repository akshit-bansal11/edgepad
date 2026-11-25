// The 5.x screens, part B, ported from screens/*.kt at 1 unit = 1 dp: the AI page, the Git page, the Developer hub that pages
// between them, and the Gamepad layout editor. Each is a pure function of its local time `lt`. Loads after screens.js, which
// supplies EP.ui, EP.gamepadLayout and EP.drawPad.
(function () {
  const { clamp, lerp, ease, seg, rgba, text, textMid, fillRR, icon, measure } = EP;
  const { TOP, BOTTOM, bar, tri, held, tap, para, wrapLines, segmented } = EP.ui;
  const S = EP.screens;

  // Strings, verbatim from res/values (the file is named on each line). Sample data is the only invented text.
  const STR = {
    back: 'Controls', // strings_keyboard.xml back_to_surface
    devAi: 'AI', // strings_dashboard.xml dev_ai
    devGit: 'Git', // dev_git
    usage: 'Usage', // strings_ai.xml ai_usage
    spend: 'Spend', // ai_spend
    today: 'Today', // strings_dashboard.xml ai_today
    days7: '7 days', // strings_ai.xml ai_spend_7d
    days30: '30 days', // ai_spend_30d
    week: 'Week', // strings_dashboard.xml ai_win_week
    durHr: '%1$d h', // dur_hr
    durDay: '%1$d d', // dur_day
    durMin: '%1$d min', // dur_min
    resets: 'Resets in %1$s', // ai_resets
    updated: 'Updated %1$s ago', // ai_updated
    estimate: 'Estimated at API prices', // ai_estimate
    sessions: 'Sessions', // ai_sessions
    busy: 'Working', // state_busy
    wait: 'Waiting for you', // state_wait
    idle: 'Idle', // state_idle
    claude: 'Claude', // agent_claude
    codex: 'Codex', // agent_codex
    repos: 'Repositories', // git_repos_label
    reposOne: '%1$d repo', // git_repos_n
    reposMany: '%1$d repos',
    followersOne: '%1$d follower', // git_followers_n
    followersMany: '%1$d followers',
    issuesOne: '%1$d issue', // git_issues_n
    issuesMany: '%1$d issues',
    prsOne: '%1$d PR', // git_prs_n
    prsMany: '%1$d PRs',
    lists: 'Pull requests and issues', // git_lists_title
    mine: 'Mine', // git_mine
    review: 'Review', // git_review
    assigned: 'Assigned', // git_assigned
    install: 'Private repos missing? Install Edgepad Dashboard on your account', // git_install_row
    meta: '#%1$s · updated %2$s', // git_item_meta
    ciOk: 'Checks passed', // git_ci_ok
    ciPending: 'Checks running', // git_ci_pending
    open: 'Open', // git_state_open
    editing: 'Editing %1$s', // strings_gamepad.xml gamepad_layout_editing
    xbox: 'Xbox', // GamepadLayout.XBOX, shown as it is (LayoutLabel.kt)
    xboxKeys: 'Xbox keys', // gamepad_preset_xbox_keys
    platformer: 'Platformer', // gamepad_preset_platformer
    racing: 'Racing', // gamepad_preset_racing
    shooter: 'Shooter', // gamepad_preset_shooter
    hint: 'Drag to move. Long-press a control for its menu.', // gamepad_layout_hint
    layouts: 'Layouts', // gamepad_layout_section and gamepad_layouts
    add: 'Add', // gamepad_add
    done: 'Done', // strings.xml editor_done
  };
  // A string with its %1$d / %1$s / %2$s placeholders filled (and %% as %).
  const fmt = (s, ...a) => a.reduce((r, v, i) => r.replace('%' + (i + 1) + '$d', v).replace('%' + (i + 1) + '$s', v), s).replace('%%', '%');
  const plural = (n, one, many) => fmt(n === 1 ? one : many, n);

  // Colours that differ by theme, [dark, light] (values-night/colors.xml, values/colors.xml).
  const TRACK = [[107, 109, 117], [135, 138, 146]];
  const LEVELS = [
    [[48, 209, 88], [31, 127, 55]], // level_green
    [[255, 214, 10], [154, 103, 0]], // level_yellow
    [[255, 159, 10], [179, 84, 0]], // level_orange
    [[255, 105, 97], [215, 0, 21]], // level_red
  ];
  const themed = (pair, pal) => EP.mixRgb(pair[0], pair[1], pal.k);
  // RingLevel.colourIndex: green below 50, yellow from 50, orange from 75, red from 90.
  const levelColor = (pct, pal) => themed(LEVELS[pct >= 90 ? 3 : pct >= 75 ? 2 : pct >= 50 ? 1 : 0], pal);

  const FACE = 1.17; // a TextView's line height as a multiple of the size (as screens5a.js)
  const ASC = 0.987, DESC = 0.213; // Lato's ascent and descent as a multiple of the size
  const base = (top, size) => top + size * FACE * 0.8; // the baseline of a line whose box starts at `top`
  const PAGE = 16; // Space.PAGE, a page's side margin
  const DOTS = 24; // PageDots' height, Space.XL
  const BAR_END = TOP + 48;
  const LAPTOP = 'Work laptop'; // sample data: the device chip's name

  // TileContrast (AiLayout.kt): the letter colour that reads on a brand tile, and whether the tile needs an outline.
  const NEAR_BLACK = [17, 17, 17];
  const WHITE = [255, 255, 255];
  const lum = (c) => {
    const f = (v) => (v / 255 <= 0.03928 ? v / 255 / 12.92 : Math.pow((v / 255 + 0.055) / 1.055, 2.4));
    return 0.2126 * f(c[0]) + 0.7152 * f(c[1]) + 0.0722 * f(c[2]);
  };
  const ratio = (a, b) => (Math.max(lum(a), lum(b)) + 0.05) / (Math.min(lum(a), lum(b)) + 0.05);
  const letterOn = (tile) => (ratio(tile, WHITE) >= ratio(tile, NEAR_BLACK) ? WHITE : NEAR_BLACK);
  // TextUtils.ellipsize at the end.
  const fit = (ctx, s, maxW, size, w) => {
    if (measure(ctx, s, size, w) <= maxW) return s;
    let t = s;
    while (t.length > 1 && measure(ctx, t + '…', size, w) > maxW) t = t.slice(0, -1);
    return t.trimEnd() + '…';
  };

  // Hub.kt hubBar: the back link to Controls, the page's name centred, and the DeviceChip (laptop icon, its name, a chevron) at
  // the far end, 12 dp in from the edge.
  const hubBar = (ctx, w, pal, title) => {
    bar(ctx, w, pal, title, STR.back);
    const cy = TOP + 24, tw = Math.min(measure(ctx, LAPTOP, 13, 'Bold'), 120), cw = 64 + tw, x = w - 12 - cw;
    fillRR(ctx, x, cy - 14, cw, 28, 14, pal.faint);
    icon(ctx, 'laptop', x + 20, cy, 20, pal.dim);
    textMid(ctx, LAPTOP, x + 34, cy, { size: 13, w: 'Bold', color: pal.ink });
    icon(ctx, 'chevron_down', x + cw - 20, cy, 20, pal.dim);
  };

  // ---- AI page (AiScreen.kt, AiLayout.kt, RingGauge.kt, SessionPill.kt) ----
  // Upright, one card per connected tool under one another (AiLayout.columnWidthDp: the page's width less 2 x 16). A card holds
  // the tool's logo tile, name and "Updated n min ago", a Usage block (one RingGauge per limit window, 270-degree arcs whose
  // value and arc take the level colour: green, yellow from 50, orange from 75, red from 90) and a Spend block (today, 7 days and
  // 30 days as compact tokens and estimated dollars). Under the card, Sessions: one 36 dp row each with the tool's mark, the
  // session's title, a state dot and word, and a small ring at the end for how full that session's context window is (the app
  // says "Context n% full" only to a screen reader, so it is not drawn). The app draws the tool's own logo; the film has none of
  // them, so it draws the app's fallback, the initial on the brand colour.
  // lt: nothing of its own. o: fill 0..1 (the rings, their values and the context rings fill from 0, default 1), session 0..1
  // (the first session's state cross-fades from Working to "Waiting for you", default 0), scroll px (the page scrolled up under the
  // bar, default 0; the two cards are taller than the screen, the app scrolls).
  const SAMPLE = [
    {
      name: STR.claude, brand: [217, 119, 87], // AiLayout.tools brand 0xFFD97757
      rings: [{ pct: 34, cap: fmt(STR.durHr, 5), reset: fmt(STR.durHr, 3) }, { pct: 58, cap: STR.week, reset: fmt(STR.durDay, 4) }], // sample data
      spend: [['1.8M', '~$4.20'], ['12M', '~$31.50'], ['48.2M', '~$120.40']], // sample data
      rows: [{ title: 'Fix the login redirect', from: 'busy', to: 'wait', ctx: 42 }], // sample data
    },
    {
      name: STR.codex, brand: [43, 43, 43], // AiLayout.tools brand 0xFF2B2B2B
      rings: [{ pct: 12, cap: fmt(STR.durHr, 5), reset: fmt(STR.durHr, 4) }, { pct: 27, cap: STR.week, reset: fmt(STR.durDay, 5) }], // sample data
      spend: [['640K', '~$1.30'], ['4.1M', '~$8.90'], ['17M', '~$36.00']], // sample data
      rows: [{ title: 'Cache the session lookup', from: 'idle', to: 'idle', ctx: 67 }], // sample data
    },
  ];
  const STATES = { busy: [STR.busy, 0], wait: [STR.wait, 2], idle: [STR.idle, -1] }; // word, LEVELS index (-1 = the track colour)
  const stateColor = (key, pal) => (STATES[key][1] < 0 ? themed(TRACK, pal) : themed(LEVELS[STATES[key][1]], pal));
  const SUB = 12, VAL = 13; // AiScreen SUB_SP, VALUE_SP
  const RING = 88; // AiLayout.ringDp for a 358 dp column of two rings (capped at MAX_RING_DP)

  // RingGauge.onDraw: the track and the used share on a 270-degree arc from 135 degrees, the value inside, the caption and the
  // reset line under it. `top` is the ring's top, `d` its diameter.
  const ring = (ctx, pal, cx, top, d, pct, caption, sub) => {
    const stroke = 8, r = (d - stroke) / 2, cy = top + d / 2, a0 = (135 * Math.PI) / 180, sweep = (270 * Math.PI) / 180;
    ctx.lineWidth = stroke;
    ctx.lineCap = 'round';
    ctx.strokeStyle = rgba(themed(TRACK, pal));
    ctx.beginPath();
    ctx.arc(cx, cy, r, a0, a0 + sweep);
    ctx.stroke();
    const color = levelColor(pct, pal);
    if (pct > 0) {
      ctx.strokeStyle = rgba(color);
      ctx.beginPath();
      ctx.arc(cx, cy, r, a0, a0 + (sweep * pct) / 100);
      ctx.stroke();
    }
    const label = Math.round(pct) + '%';
    const size = measure(ctx, label, 24, 'Black') > d * 0.6 ? 15 : 24; // Type.TITLE, else Type.VALUE
    text(ctx, label, cx, cy + size * 0.35, { size, w: 'Black', color, align: 'center' });
    const capY = top + d + 13 * ASC;
    text(ctx, caption, cx, capY, { size: 13, color: pal.dim, align: 'center' });
    text(ctx, sub, cx, capY + 13 * DESC + SUB * ASC, { size: SUB, color: pal.dim, align: 'center' });
  };

  // SessionPill.onDraw: a faint 36 dp row; the circle 4 dp in, the title 12 dp after it, the state dot and word, the context ring.
  const pill = (ctx, pal, x, y, w, row, fill, mix) => {
    fillRR(ctx, x, y, w, 36, 10, pal.faint);
    const brand = row.brand, cy = y + 18;
    ctx.fillStyle = rgba(brand);
    ctx.beginPath();
    ctx.arc(x + 18, cy, 14, 0, Math.PI * 2);
    ctx.fill();
    if (ratio(brand, pal.faint) < 1.6) { // TileContrast.needsOutline against the row's faint fill
      ctx.lineWidth = 1;
      ctx.strokeStyle = rgba(pal.line);
      ctx.stroke();
    }
    textMid(ctx, 'C', x + 18, cy, { size: 12, w: 'Bold', color: letterOn(brand), align: 'center' });
    const stateW = (k) => measure(ctx, STATES[k][0], 12);
    const room = w - 32 - 80 - Math.max(stateW(row.from), stateW(row.to)); // SessionRow.titleRoom with the wider state
    textMid(ctx, fit(ctx, row.title, room, 13, 'Bold'), x + 44, cy, { size: 13, w: 'Bold', color: pal.ink });
    for (const [key, a] of row.from === row.to ? [[row.from, 1]] : [[row.from, 1 - mix], [row.to, mix]]) {
      const sx = x + w - 24 - 16 - stateW(key);
      ctx.save();
      ctx.globalAlpha *= a;
      textMid(ctx, STATES[key][0], sx, cy, { size: 12, color: pal.dim });
      ctx.fillStyle = rgba(stateColor(key, pal));
      ctx.beginPath();
      ctx.arc(sx - 8 - 4, cy, 4, 0, Math.PI * 2);
      ctx.fill();
      ctx.restore();
    }
    const rcx = x + w - 12 - 8, pct = row.ctx * fill, rr = (16 - 2.5) / 2;
    ctx.lineWidth = 2.5;
    ctx.strokeStyle = rgba(themed(TRACK, pal));
    ctx.beginPath();
    ctx.arc(rcx, cy, rr, 0, Math.PI * 2);
    ctx.stroke();
    if (pct > 0) {
      ctx.lineCap = 'round';
      ctx.strokeStyle = rgba(levelColor(row.ctx, pal));
      ctx.beginPath();
      ctx.arc(rcx, cy, rr, -Math.PI / 2, -Math.PI / 2 + (Math.PI * 2 * pct) / 100);
      ctx.stroke();
    }
  };

  // One tool's column: the card, then Sessions. Returns the y under it.
  const tool = (ctx, pal, y, w, t, fill, session) => {
    const x = PAGE, cw = w - 2 * PAGE, ix = x + 14, inner = cw - 28;
    const head = 15 * FACE + SUB * FACE, ringH = RING + (13 + SUB) * (ASC + DESC), val = VAL * FACE, sub = SUB * FACE;
    const cardH = 12 + head + 12 + sub + 8 + ringH + 12 + sub + 8 + (sub + val + sub) + 8 + sub + 12;
    fillRR(ctx, x, y, cw, cardH, 14, pal.card);
    let c = y + 12;
    // The logo tile (28 dp, the brand colour, the initial), then the name over "Updated 1 min ago".
    fillRR(ctx, ix, c + (head - 28) / 2, 28, 28, 8, rgba(t.brand));
    if (ratio(t.brand, pal.card) < 1.6) {
      EP.rr(ctx, ix, c + (head - 28) / 2, 28, 28, 8);
      ctx.lineWidth = 1;
      ctx.strokeStyle = rgba(pal.line);
      ctx.stroke();
    }
    textMid(ctx, t.name.slice(0, 1), ix + 14, c + head / 2, { size: 13, w: 'Bold', color: letterOn(t.brand), align: 'center' });
    text(ctx, t.name, ix + 40, base(c, 15), { size: 15, w: 'Bold', color: pal.ink });
    text(ctx, fmt(STR.updated, fmt(STR.durMin, 1)), ix + 40, base(c + 15 * FACE, SUB), { size: SUB, color: pal.dim });
    c += head + 12;
    text(ctx, STR.usage.toUpperCase(), ix, base(c, SUB), { size: SUB, w: 'Bold', color: pal.dim });
    c += sub + 8;
    t.rings.forEach((g, i) => ring(ctx, pal, ix + (inner / t.rings.length) * (i + 0.5), c, RING, g.pct * fill, g.cap, fmt(STR.resets, g.reset)));
    c += ringH + 12;
    text(ctx, STR.spend.toUpperCase(), ix, base(c, SUB), { size: SUB, w: 'Bold', color: pal.dim });
    c += sub + 8;
    [STR.today, STR.days7, STR.days30].forEach((label, i) => {
      const cx = ix + (inner / 3) * i;
      text(ctx, label, cx, base(c, SUB), { size: SUB, color: pal.dim });
      text(ctx, t.spend[i][0], cx, base(c + sub, VAL), { size: VAL, w: 'Bold', color: pal.ink });
      text(ctx, t.spend[i][1], cx, base(c + sub + val, SUB), { size: SUB, color: pal.dim });
    });
    c += sub + val + sub + 8;
    text(ctx, STR.estimate, ix, base(c, SUB), { size: SUB, color: pal.dim });
    c = y + cardH + 12;
    text(ctx, STR.sessions.toUpperCase(), x + 4, base(c, SUB), { size: SUB, w: 'Bold', color: pal.dim });
    c += sub;
    t.rows.forEach((row, i) => {
      c += 8;
      pill(ctx, pal, x, c, cw, { ...row, brand: t.brand }, fill, i === 0 ? session : 0);
      c += 36;
    });
    return c;
  };

  const aiPage = (ctx, w, h, pal, o) => {
    ctx.fillStyle = rgba(pal.bg);
    ctx.fillRect(0, 0, w, h);
    ctx.save();
    ctx.beginPath();
    ctx.rect(0, BAR_END, w, h - BAR_END);
    ctx.clip();
    let y = BAR_END - (o.scroll ?? 0);
    SAMPLE.forEach((t, i) => {
      y = tool(ctx, pal, y + (i ? 16 : 0), w, t, ease(o.fill ?? 1), ease(o.session ?? 0));
    });
    ctx.restore();
    hubBar(ctx, w, pal, STR.devAi);
  };

  // ---- Git page (GitScreen.kt) ----
  // The nav bar, a chip per connected host (the chosen one on the accent's soft fill; GitHub's mark is the Simple Icon the film
  // has, the others are text), then two cards one under the other, as the app stacks them upright: the profile (avatar initial,
  // name, "@login · n repos · n followers", the repositories with "language · star · issues · PRs" and an arrow, the install line)
  // and the lists card ("Pull requests and issues" over the Mine / Review / Assigned switch, then the items grouped by repository:
  // an icon, the title over "#n · updated ...", a badge). Neutral sample data throughout.
  // lt: o.press is the lt at which the first Mine row ("Fix the login redirect") is tapped (finger in 0.4 s before, the row
  // flashes). o: tab 0..2 fractional (Mine, Review, Assigned; the switch's thumb sits on it and the lists cross-fade), press.
  // Edgepad's own pull request and issue icons (res/drawable/ic_git_pr.xml, ic_git_issue.xml), 24 x 24, stroke 1.5.
  const PR = new Path2D('M6,4.25a2.25,2.25 0 1,0 0,4.5a2.25,2.25 0 1,0 0,-4.5M6,8.75V15.25M6,15.25m-2.25,2.25a2.25,2.25 0 1,0 4.5,0a2.25,2.25 0 1,0 -4.5,0M18,19.75a2.25,2.25 0 1,0 0,-4.5a2.25,2.25 0 1,0 0,4.5M18,15.25V10A2.5,2.5 0 0,0 15.5,7.5H12.5M14.25,5.25L12,7.5L14.25,9.75');
  const ISSUE = new Path2D('M12,3.25a8.75,8.75 0 1,0 0,17.5a8.75,8.75 0 1,0 0,-17.5M12,11.75V12.25');
  const appPath = (ctx, p, cx, cy, size, color) => {
    ctx.save();
    ctx.translate(cx - size / 2, cy - size / 2);
    ctx.scale(size / 24, size / 24);
    ctx.lineWidth = 1.5;
    ctx.lineCap = ctx.lineJoin = 'round';
    ctx.strokeStyle = rgba(color);
    ctx.stroke(p);
    ctx.restore();
  };
  const HOSTS = ['GitHub', 'GitLab', 'Gitea']; // GitHost labels, sample connected hosts
  const REPOS = [
    { name: 'acme/website', lang: 'TypeScript', stars: 128, issues: 3, prs: 2 }, // sample data
    { name: 'acme/api', lang: 'Go', stars: 42, issues: 1, prs: 1 }, // sample data
  ];
  // Items per tab: [issue?, title, #n, updated, badge: 'ok' | 'pending' | 'open'], grouped by repository. Sample data.
  const LISTS = [
    [
      { repo: 'acme/website', rows: [[0, 'Fix the login redirect', '214', '2 hours ago', 'ok'], [0, 'Update the docs', '209', '1 day ago', 'open']] },
      { repo: 'acme/api', rows: [[0, 'Cache the session lookup', '87', '3 hours ago', 'pending'], [1, 'Add a health check', '85', '2 days ago', 'open']] },
    ],
    [{ repo: 'acme/api', rows: [[0, 'Paginate the list endpoint', '91', '5 hours ago', 'ok'], [0, 'Rename the config keys', '90', '1 day ago', 'pending']] }],
    [{ repo: 'acme/website', rows: [[1, 'Fix the footer links', '212', '3 days ago', 'open']] }],
  ];
  const BADGES = { ok: STR.ciOk, pending: STR.ciPending, open: STR.open };
  const listHeight = (groups) => groups.reduce((s, g) => s + 12 + SUB * FACE + 48 * g.rows.length, 0); // heading (12 above) + 48 dp rows

  const gitPage = (ctx, w, h, pal, lt, o) => {
    ctx.fillStyle = rgba(pal.bg);
    ctx.fillRect(0, 0, w, h);
    const tab = clamp(o.tab ?? 0, 0, 2), ix = PAGE + 16, iw = w - 2 * PAGE - 32;
    let y = BAR_END;
    // Host chips: a 48 dp target round a 32 dp pill, 8 dp apart, 4 dp under.
    let hx = PAGE;
    HOSTS.forEach((name, i) => {
      const on = i === 0, mark = name === 'GitHub', cw = 8 + (mark ? 20 : 0) + measure(ctx, name, 13, on ? 'Bold' : 'Regular') + 8;
      fillRR(ctx, hx, y + 8, cw, 32, 10, on ? pal.accentSoft : pal.faint);
      if (mark) EP.brand(ctx, 'github', hx + 8 + 8, y + 24, 16, rgba(pal.ink));
      textMid(ctx, name, hx + 8 + (mark ? 20 : 0), y + 24, { size: 13, w: on ? 'Bold' : 'Regular', color: on ? pal.accent : pal.ink });
      hx += cw + 8;
    });
    y += 52;
    // The profile card.
    const profH = 12 + 40 + 12 + SUB * FACE + REPOS.length * 48 + 48 + 8;
    fillRR(ctx, PAGE, y, w - 2 * PAGE, profH, 14, pal.card);
    let c = y + 12;
    ctx.fillStyle = rgba(pal.accent);
    ctx.beginPath();
    ctx.arc(ix + 20, c + 20, 20, 0, Math.PI * 2);
    ctx.fill();
    textMid(ctx, 'A', ix + 20, c + 20, { size: 17, w: 'Bold', color: pal.onAccent, align: 'center' });
    const who = 15 * FACE + SUB * FACE, wt = c + (40 - who) / 2;
    text(ctx, 'Acme', ix + 52, base(wt, 15), { size: 15, w: 'Bold', color: pal.ink }); // sample data
    text(ctx, ['@acme', plural(2, STR.reposOne, STR.reposMany), plural(14, STR.followersOne, STR.followersMany)].join(' · '), ix + 52, base(wt + 15 * FACE, SUB), { size: SUB, color: pal.dim });
    c += 40 + 12;
    text(ctx, STR.repos.toUpperCase(), ix, base(c, SUB), { size: SUB, w: 'Bold', color: pal.dim });
    c += SUB * FACE;
    for (const r of REPOS) {
      const lines = 14 * FACE + SUB * FACE, top = c + (48 - lines) / 2;
      text(ctx, r.name, ix, base(top, 14), { size: 14, w: 'Bold', color: pal.ink });
      text(ctx, [r.lang, '★ ' + r.stars, plural(r.issues, STR.issuesOne, STR.issuesMany), plural(r.prs, STR.prsOne, STR.prsMany)].join(' · '), ix, base(top + 14 * FACE, SUB), { size: SUB, color: pal.dim });
      textMid(ctx, '↗', ix + iw, c + 24, { size: 16, color: pal.accent, align: 'right' });
      c += 48;
    }
    const inst = wrapLines(ctx, STR.install, iw, SUB);
    inst.forEach((l, i) => text(ctx, l, ix, base(c + (48 - inst.length * SUB * FACE) / 2 + i * SUB * FACE, SUB), { size: SUB, color: pal.dim }));
    y += profH;
    // The lists card, directly under it.
    const i0 = Math.min(Math.floor(tab), 1), f = tab - i0, hs = [listHeight(LISTS[i0]), listHeight(LISTS[i0 + 1])];
    const head = 15 * FACE + 48, cardH = head + lerp(hs[0], hs[1], f);
    fillRR(ctx, PAGE, y, w - 2 * PAGE, cardH, 14, pal.card);
    text(ctx, STR.lists, ix, base(y, 15), { size: 15, w: 'Bold', color: pal.ink });
    const names = [STR.mine, STR.review, STR.assigned];
    segmented(ctx, pal, ix + 3 * (Math.max(...names.map((n) => measure(ctx, n, 13, 'Bold'))) + 28) + 4, y + 15 * FACE + 24, names, tab);
    let press = null;
    LISTS.forEach((groups, ti) => {
      const a = clamp(1 - Math.abs(tab - ti));
      if (a <= 0) return;
      ctx.save();
      ctx.globalAlpha *= a;
      let gy = y + head;
      for (const g of groups) {
        text(ctx, g.repo.toUpperCase(), ix, base(gy + 12, SUB), { size: SUB, w: 'Bold', color: pal.dim });
        gy += 12 + SUB * FACE;
        g.rows.forEach((r, ri) => {
          const [issue, title, num, ago, badge] = r, cy = gy + 24;
          if (ti === 0 && g === groups[0] && ri === 0) {
            press = { y: cy };
            const k = o.press === undefined ? 0 : tri(lt, o.press, 0.3);
            if (k > 0) fillRR(ctx, ix - 8, gy, iw + 16, 48, 10, pal.faint, k);
          }
          appPath(ctx, issue ? ISSUE : PR, ix + 9, cy, 18, issue ? pal.accent : themed(LEVELS[0], pal));
          const bw = measure(ctx, BADGES[badge], SUB, 'Bold') + 16;
          fillRR(ctx, ix + iw - bw, cy - (SUB * FACE + 8) / 2, bw, SUB * FACE + 8, 10, pal.faint);
          textMid(ctx, BADGES[badge], ix + iw - bw / 2, cy, { size: SUB, w: 'Bold', color: badge === 'ok' ? themed(LEVELS[0], pal) : pal.ink, align: 'center' });
          const lines = 14 * FACE + SUB * FACE, top = cy - lines / 2, room = iw - 18 - 12 - bw - 8;
          text(ctx, fit(ctx, title, room, 14, 'Bold'), ix + 30, base(top, 14), { size: 14, w: 'Bold', color: pal.ink });
          text(ctx, fit(ctx, fmt(STR.meta, num, ago), room, SUB), ix + 30, base(top + 14 * FACE, SUB), { size: SUB, color: pal.dim });
          gy += 48;
        });
      }
      ctx.restore();
    });
    if (o.press !== undefined && press) tap(ctx, lt, o.press, w / 2, press.y);
  };

  // ---- Developer hub (DeveloperHub.kt, Hub.kt hubView, Pager.kt) ----
  // The AI and Git pages side by side in a pager, each with its own bar, and the page dots in a 24 dp row at the foot: the
  // showing page's dot in the accent, the other in the off colour (8 dp circles, 8 apart). lt: handed to the pages unchanged.
  // o: page 0..1 fractional (0 = AI, 1 = Git; the pages slide by it, dots blend), ai and git: the pages' own `o` (see aiPage:
  // fill, session, scroll; and gitPage: tab, press).
  S.devHub = (ctx, w, h, pal, lt, o = {}) => {
    const page = clamp(o.page ?? 0, 0, 1), ph = h - BOTTOM - DOTS;
    ctx.fillStyle = rgba(pal.bg);
    ctx.fillRect(0, 0, w, h);
    [(pw, phh) => aiPage(ctx, pw, phh, pal, o.ai || {}), (pw, phh) => gitPage(ctx, pw, phh, pal, lt, o.git || {})].forEach((draw, i) => {
      const dx = (i - page) * w;
      if (Math.abs(dx) >= w) return;
      ctx.save();
      ctx.beginPath();
      ctx.rect(0, 0, w, ph);
      ctx.clip();
      ctx.translate(dx, 0);
      draw(w, ph);
      ctx.restore();
    });
    [0, 1].forEach((i) => {
      ctx.fillStyle = rgba(EP.mixRgb(pal.off, pal.accent, clamp(1 - Math.abs(page - i))));
      ctx.beginPath();
      ctx.arc(w / 2 - 8 + i * 16, ph + DOTS / 2, 4, 0, Math.PI * 2);
      ctx.fill();
    });
  };
  // The two pages on their own, as the pager shows them at rest: o is that page's own `o`.
  S.ai = (ctx, w, h, pal, lt, o = {}) => S.devHub(ctx, w, h, pal, lt, { page: 0, ai: o });
  S.git = (ctx, w, h, pal, lt, o = {}) => S.devHub(ctx, w, h, pal, lt, { page: 1, git: o });

  // ---- Gamepad layout editor (GamepadLayoutScreen.kt, LayoutCanvas.kt, EditorFrame.kt, GamepadLayout.kt presets) ----
  // Always sideways. The canvas is the whole screen: a dot grid (12 dp cells, 2 dp dots) and two centre lines that turn accent and
  // thicken while a control sits snapped on one; over it a round back button, the title "Editing <layout>" and a round options
  // button. The controls come from EP.gamepadLayout / EP.drawPad (screens.js, the gamepad painter) at the preset o.from, moving
  // to o.to by o.k with a short stagger. Options open as a sheet (220 dp wide, under the button, over a 35% scrim): the reset
  // icon, "Layouts" with the five presets (the chosen one carries the accent check; the box shows two and a half rows and
  // scrolls), the hint, Add and Layouts, Done. The sheet is as tall as the room under the button, so its foot is cut as in the app.
  // lt: 0.6 the RT shoulder is picked up (it lifts on a shadow with an accent ring), 0.6-1.6 it is dragged from where the
  // preset has it to the middle of the screen, snapping to the grid and then to the centre (both lines light as it nears), 1.7
  // dropped (lines go out). It then keeps that spot in the layout it reflows from.
  // o: from, to (preset keys 'xbox' | 'xboxKeys' | 'platformer' | 'racing' | 'shooter'; default 'xbox'; to defaults to from), k 0..1
  // (the reflow, 280 ms is the caller's timing; controls missing from one preset grow or shrink), list 0..1 (the options sheet
  // opens), pick fractional index into [Xbox, Xbox keys, Platformer, Racing, Shooter] (the checked row), drag false (skips the RT
  // drag; default on, and it applies only when the preset has an RT).
  const PRESETS = [['xbox', STR.xbox], ['xboxKeys', STR.xboxKeys], ['platformer', STR.platformer], ['racing', STR.racing], ['shooter', STR.shooter]];
  const GRID = 12, CENTRE_SNAP = 10; // LayoutCanvas GRID_DP, CENTRE_SNAP_DP
  const DRAG0 = 0.6, DRAG1 = 1.6, DROP = 1.7;
  const STAGGER = 0.3;
  const REST = { lx: 0, ly: 0, rt: 0, lt: 0, a: 0 };
  const SCRIM = 'rgba(0,0,0,0.349)'; // EditorFrame.SCRIM, black at 35%
  const SHEET = { w: 220, left: 14, top: 10, right: 12, bottom: 12 };
  // LayoutCanvas.snap: the grid point nearest, or the centre when within 10 dp of it.
  const snap = (v, extent) => (Math.abs(v - extent / 2) < CENTRE_SNAP ? extent / 2 : Math.round(v / GRID) * GRID);
  // Each control moves from its place in one preset to its place in the other; one in only one grows or shrinks in place.
  const reflow = (from, to, k) => {
    const a = new Map(from.map((c) => [c.id, c])), b = new Map(to.map((c) => [c.id, c]));
    const ids = [...new Set([...to.map((c) => c.id), ...from.map((c) => c.id)])], n = Math.max(ids.length - 1, 1);
    return ids
      .map((id, i) => {
        const e = ease(seg(k, (i / n) * STAGGER, (i / n) * STAGGER + 1 - STAGGER)), p = a.get(id), q = b.get(id);
        if (p && q) return { ...q, cx: lerp(p.cx, q.cx, e), cy: lerp(p.cy, q.cy, e), rw: lerp(p.rw, q.rw, e), rh: lerp(p.rh, q.rh, e) };
        if (q) return { ...q, rw: q.rw * e, rh: q.rh * e };
        return { ...p, rw: p.rw * (1 - e), rh: p.rh * (1 - e) };
      })
      .filter((c) => c.rw >= 1 && c.rh >= 1);
  };

  S.gamepadLayout = (ctx, w, h, pal, lt, o = {}) => {
    ctx.fillStyle = rgba(pal.bg);
    ctx.fillRect(0, 0, w, h);
    // LayoutCanvas: the dot grid, then the two centre lines (the vertical one lit when a control is on it, the horizontal likewise).
    ctx.fillStyle = rgba(pal.line);
    ctx.beginPath();
    for (let gy = GRID; gy < h; gy += GRID) for (let gx = GRID; gx < w; gx += GRID) ctx.rect(gx - 1, gy - 1, 2, 2);
    ctx.fill();
    const from = EP.gamepadLayout(o.from ?? 'xbox', w, h), to = EP.gamepadLayout(o.to ?? o.from ?? 'xbox', w, h);
    // The RT drag: the finger takes the control's centre from its place to the middle of the screen; the control follows it snapped.
    const rt = from.find((c) => c.id === 'rt'), dragging = o.drag !== false && rt && lt >= DRAG0;
    const grab = rt ? { x: lerp(rt.cx, w / 2, ease(seg(lt, DRAG0, DRAG1))), y: lerp(rt.cy, h / 2, ease(seg(lt, DRAG0, DRAG1))) } : null;
    const at = dragging ? { x: snap(grab.x, w), y: snap(grab.y, h) } : null;
    const lifted = dragging && lt < DROP;
    const lit = (on) => {
      ctx.lineWidth = on ? 2 : 1;
      ctx.strokeStyle = rgba(on ? pal.accent : pal.line);
    };
    lit(lifted && at.x === w / 2);
    ctx.beginPath();
    ctx.moveTo(w / 2, 0);
    ctx.lineTo(w / 2, h);
    ctx.stroke();
    lit(lifted && at.y === h / 2);
    ctx.beginPath();
    ctx.moveTo(0, h / 2);
    ctx.lineTo(w, h / 2);
    ctx.stroke();
    const placed = from.map((c) => (dragging && c.id === 'rt' ? { ...c, cx: at.x, cy: at.y } : c));
    EP.drawPad(ctx, w, h, pal, reflow(placed, to, o.k ?? 0), { input: REST, press: {}, lifted: lifted ? 'rt' : null });
    if (dragging) EP.tapDot(ctx, grab.x, grab.y, held(lt, DRAG0 - 0.1, DROP));
    // EditorFrame: the round back button and the title over the canvas, the scrim under the options button.
    const open = ease(o.list ?? 0), name = PRESETS.find((p) => p[0] === ((o.k ?? 0) < 0.5 ? o.from ?? 'xbox' : o.to ?? o.from ?? 'xbox'))[1];
    ctx.fillStyle = rgba(pal.card);
    ctx.beginPath();
    ctx.arc(28, 26, 20, 0, Math.PI * 2);
    ctx.fill();
    icon(ctx, 'chevron_left', 28, 26, 24, pal.accent);
    textMid(ctx, fmt(STR.editing, name), w / 2, 24, { size: 15, w: 'Bold', color: pal.dim, align: 'center' });
    if (open > 0) {
      ctx.save();
      ctx.globalAlpha *= open;
      ctx.fillStyle = SCRIM;
      ctx.fillRect(0, 0, w, h);
      ctx.restore();
    }
    ctx.fillStyle = rgba(EP.mixRgb(pal.card, pal.accent, open));
    ctx.beginPath();
    ctx.arc(w - 28, 26, 20, 0, Math.PI * 2);
    ctx.fill();
    icon(ctx, 'sliders_horizontal', w - 28, 26, 24, EP.mixRgb(pal.accent, pal.onAccent, open));
    if (open <= 0) return;
    // The sheet: dropped 4 dp under the button's 48 dp target, its right edge on the target's, 16 dp corners, 16 dp of shadow.
    const sx = w - 4 - SHEET.w, sy = 54, sh = Math.min(h - sy, 376), pick = o.pick ?? 0;
    ctx.save();
    ctx.globalAlpha *= open;
    ctx.translate(w - 4, sy);
    ctx.scale(lerp(0.85, 1, open), lerp(0.85, 1, open));
    ctx.translate(-(w - 4), -sy);
    ctx.shadowColor = 'rgba(0,0,0,0.35)';
    ctx.shadowBlur = 16;
    ctx.shadowOffsetY = 4;
    fillRR(ctx, sx, sy, SHEET.w, sh, 16, rgba(pal.card));
    ctx.shadowColor = 'transparent';
    ctx.beginPath();
    ctx.rect(sx, sy, SHEET.w, sh);
    ctx.clip();
    const lx = sx + SHEET.left, lw = SHEET.w - SHEET.left - SHEET.right;
    let y = sy + SHEET.top;
    ctx.fillStyle = rgba(pal.accentSoft);
    ctx.beginPath();
    ctx.arc(lx + lw - 18, y + 18, 18, 0, Math.PI * 2);
    ctx.fill();
    icon(ctx, 'refresh_cw', lx + lw - 18, y + 18, 18, pal.accent);
    y += 50;
    text(ctx, STR.layouts, lx, base(y + 12, 13), { size: 13, w: 'Bold', color: pal.dim });
    y += 12 + 13 * FACE + 4;
    // The list: five 48 dp rows with a hairline between, in a 120 dp box that scrolls to keep the chosen row in view.
    ctx.save();
    ctx.beginPath();
    ctx.rect(lx, y, lw, 120);
    ctx.clip();
    const scroll = clamp(pick * 49 - 36, 0, PRESETS.length * 49 - 1 - 120);
    PRESETS.forEach(([, label], i) => {
      const ry = y + i * 49 - scroll;
      if (i) {
        ctx.fillStyle = rgba(pal.line);
        ctx.fillRect(lx, ry - 1, lw, 1);
      }
      textMid(ctx, label, lx, ry + 24, { size: 16, color: pal.ink });
      icon(ctx, 'check', lx + lw - 10, ry + 24, 20, pal.accent, clamp(1 - Math.abs(pick - i)));
    });
    ctx.restore();
    y += 120;
    y = para(ctx, STR.hint, lx, y + 8, lw, 13, pal.dim) + 8;
    const bw = (lw - 8) / 2;
    textMid(ctx, STR.add, lx + bw / 2, y + 25, { size: 17, w: 'Bold', color: pal.accent, align: 'center' });
    textMid(ctx, STR.layouts, lx + bw + 8 + bw / 2, y + 25, { size: 17, w: 'Bold', color: pal.accent, align: 'center' });
    y += 50 + 8;
    fillRR(ctx, lx, y, lw, 36, 10, rgba(pal.accent));
    textMid(ctx, STR.done, lx + lw / 2, y + 18, { size: 15, w: 'Bold', color: pal.onAccent, align: 'center' });
    ctx.restore();
  };
})();
