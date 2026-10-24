// Renders the demo video in CI: serves the repo, drives headless Chrome over CDP, pipes frames into ffmpeg.
// Node 24, built-ins only. Needs out/voice.mp3 and out/timing.json from voice.py, plus ffmpeg and Chrome.
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { createReadStream, existsSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, extname, join, normalize, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout as sleep } from 'node:timers/promises';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, '../..');
const OUT = join(HERE, 'out');
const FPS = 30;
const CHROME = process.env.CHROME || 'google-chrome';
const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json', '.ttf': 'font/ttf', '.svg': 'image/svg+xml' };
const SIZES = { '16x9': [1920, 1080], '9x16': [1080, 1920], '4x5': [1080, 1350], gif: [880, 660] };

const children = [];
let server;
let profile;
function cleanup() {
  for (const c of children) if (c.exitCode === null) c.kill('SIGKILL');
  server?.close();
  // Chrome's helpers can still be writing to the profile just after the kill. A leftover temp folder costs
  // nothing on a throwaway runner, so a failed removal must not fail a finished render.
  try {
    if (profile) rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
  } catch (e) {
    console.warn(`left ${profile}: ${e.code}`);
  }
}
process.on('exit', cleanup);
for (const sig of ['SIGINT', 'SIGTERM']) process.on(sig, () => process.exit(1));

function serve() {
  server = createServer((req, res) => {
    const file = normalize(join(ROOT, decodeURIComponent(new URL(req.url, 'http://x').pathname)));
    if (!file.startsWith(ROOT + sep) || !existsSync(file) || !statSync(file).isFile()) {
      res.writeHead(404).end();
      return;
    }
    res.writeHead(200, { 'content-type': TYPES[extname(file)] || 'application/octet-stream' });
    createReadStream(file).pipe(res);
  });
  return new Promise((ok) => server.listen(0, '127.0.0.1', () => ok(server.address().port)));
}

async function launchChrome() {
  profile = mkdtempSync(join(tmpdir(), 'edgepad-chrome-'));
  const chrome = spawn(CHROME, [
    '--headless=new', '--no-sandbox', '--disable-gpu', '--hide-scrollbars', '--remote-debugging-port=0',
    `--user-data-dir=${profile}`, '--no-first-run', '--disable-extensions', 'about:blank',
  ], { stdio: 'ignore' });
  children.push(chrome);
  const portFile = join(profile, 'DevToolsActivePort');
  for (let i = 0; i < 200 && !existsSync(portFile); i++) await sleep(100);
  const [port, path] = readFileSync(portFile, 'utf8').trim().split('\n');
  const ws = new WebSocket(`ws://127.0.0.1:${port}${path}`);
  await new Promise((ok, bad) => { ws.onopen = ok; ws.onerror = () => bad(new Error('CDP socket failed')); });
  let id = 0;
  const waiting = new Map();
  ws.onmessage = (m) => {
    const msg = JSON.parse(m.data);
    const w = waiting.get(msg.id);
    if (!w) return;
    waiting.delete(msg.id);
    if (msg.error) w.bad(new Error(`${w.method}: ${msg.error.message}`));
    else w.ok(msg.result);
  };
  const send = (method, params = {}, sessionId) =>
    new Promise((ok, bad) => {
      waiting.set(++id, { ok, bad, method });
      ws.send(JSON.stringify({ id, method, params, sessionId }));
    });
  return { send };
}

// One page per aspect, sized exactly to the canvas it draws.
async function openPage(cdp, port, aspect) {
  const [w, h] = SIZES[aspect];
  const { targetId } = await cdp.send('Target.createTarget', { url: 'about:blank' });
  const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
  const call = (method, params) => cdp.send(method, params, sessionId);
  await call('Emulation.setDeviceMetricsOverride', { width: w, height: h, deviceScaleFactor: 1, mobile: false });
  await call('Page.enable');
  await call('Page.navigate', { url: `http://127.0.0.1:${port}/assets/video/index.html?aspect=${aspect}` });
  // The navigation has committed but the scripts may not have run yet: wait for the page to publish `ready`.
  const ready = await call('Runtime.evaluate', {
    expression: 'new Promise((ok) => { const i = setInterval(() => { if (window.ready) { clearInterval(i); ok(window.ready.then(() => true)); } }, 50); })',
    awaitPromise: true,
    returnByValue: true,
  });
  if (ready.exceptionDetails) throw new Error(`page failed: ${JSON.stringify(ready.exceptionDetails)}`);
  const duration = (await call('Runtime.evaluate', { expression: 'window.duration', returnByValue: true })).result.value;
  const frame = async (t) => {
    const r = await call('Runtime.evaluate', {
      expression: `window.renderAt(${t}), document.getElementById('c').toDataURL('image/jpeg', 0.95)`,
      returnByValue: true,
    });
    if (r.exceptionDetails) throw new Error(`renderAt(${t}): ${JSON.stringify(r.exceptionDetails)}`);
    return Buffer.from(r.result.value.split(',')[1], 'base64');
  };
  return { frame, duration, close: () => cdp.send('Target.closeTarget', { targetId }) };
}

function ffmpeg(args) {
  const p = spawn('ffmpeg', ['-y', '-loglevel', 'error', ...args], { stdio: ['pipe', 'inherit', 'inherit'] });
  children.push(p);
  const done = new Promise((ok, bad) => p.on('close', (code) => (code === 0 ? ok() : bad(new Error(`ffmpeg exited ${code}`)))));
  p.stdin.on('error', () => {}); // a dead ffmpeg is reported by `done`
  return { stdin: p.stdin, done };
}

async function write(stdin, buf) {
  if (!stdin.write(buf)) await new Promise((ok) => stdin.once('drain', ok));
}

async function renderVideo(page, aspect) {
  const frames = Math.ceil(page.duration * FPS);
  const out = join(OUT, `edgepad-demo-${aspect}.mp4`);
  const enc = ffmpeg([
    '-f', 'image2pipe', '-c:v', 'mjpeg', '-framerate', String(FPS), '-i', '-',
    '-i', join(OUT, 'voice.mp3'),
    '-c:v', 'libx264', '-b:v', '20M', '-maxrate', '20M', '-bufsize', '40M', '-pix_fmt', 'yuv420p',
    '-c:a', 'aac', '-b:a', '192k', '-movflags', '+faststart', '-t', String(page.duration), out,
  ]);
  for (let i = 0; i < frames; i++) await write(enc.stdin, await page.frame(i / FPS));
  enc.stdin.end();
  await enc.done;
  console.log(`${out}: ${frames} frames`);
}

// 15 fps from the page's own 7 s loop (trackpad, then a dial slide); coarser settings until it is under 3 MB.
async function renderGif(page) {
  const fps = 15;
  const shots = [];
  for (let i = 0; i < Math.ceil(page.duration * fps); i++) shots.push(await page.frame(i / fps));
  const out = join(OUT, 'demo.gif');
  for (const [rate, colors] of [[15, 128], [12, 96], [10, 64]]) {
    const enc = ffmpeg([
      '-f', 'image2pipe', '-c:v', 'mjpeg', '-framerate', String(fps), '-i', '-',
      '-filter_complex',
      `fps=${rate},scale=880:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=${colors}[p];[b][p]paletteuse=dither=bayer:bayer_scale=4`,
      '-loop', '0', out,
    ]);
    for (const s of shots) await write(enc.stdin, s);
    enc.stdin.end();
    await enc.done;
    const size = statSync(out).size;
    console.log(`${out}: ${rate} fps, ${colors} colours, ${size} bytes`);
    if (size <= 3 * 1024 * 1024) return;
  }
  throw new Error('demo.gif is still over 3 MB at the coarsest setting');
}

async function main() {
  if (!existsSync(join(OUT, 'voice.mp3')) || !existsSync(join(OUT, 'timing.json'))) {
    throw new Error('run voice.py first: out/voice.mp3 and out/timing.json are missing');
  }
  const timing = JSON.parse(readFileSync(join(OUT, 'timing.json'), 'utf8'));
  const port = await serve();
  const cdp = await launchChrome();
  for (const aspect of ['16x9', '9x16', '4x5']) {
    const page = await openPage(cdp, port, aspect);
    await renderVideo(page, aspect);
    if (aspect === '16x9') {
      // The website's copy: the same picture at a quality-based rate, small enough to keep in the repo.
      await ffmpeg([
        '-nostdin', '-i', join(OUT, 'edgepad-demo-16x9.mp4'), '-c:v', 'libx264', '-crf', '23', '-preset', 'slow',
        '-pix_fmt', 'yuv420p', '-c:a', 'copy', '-movflags', '+faststart', join(OUT, 'edgepad-demo.mp4'),
      ]).done;
      console.log(`edgepad-demo.mp4: ${statSync(join(OUT, 'edgepad-demo.mp4')).size} bytes`);
      // A good frame: the trackpad scene a few seconds in, with a finger mid-glide.
      const line = timing.lines.find((l) => l.id === 'trackpad');
      writeFileSync(join(OUT, 'edgepad-demo-poster.jpg'), await page.frame(line.start + 2.4));
    }
    await page.close();
  }
  const gif = await openPage(cdp, port, 'gif');
  await renderGif(gif);
  await gif.close();
}

try {
  await main();
} catch (e) {
  console.error(e);
  process.exitCode = 1;
} finally {
  cleanup();
}
