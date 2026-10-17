// End-to-end test for the HelloMesh browser build (docs/architecture.md sections 10 and 12). Plain Node script, no runner.
//
//   node test/e2e.mjs                    READY, FRAME 120, pixels, mouse/keyboard -> CAMERA (200 px drag = 80 deg yaw, ArrowUp zooms
//                                        in), Space and the "Play sound" button -> AUDIO play AND a measured output level > 0.01 within
//                                        500 ms (window.__brutal.audio.getOutputLevel(), the RMS of the master mix), a sound started
//                                        before the first gesture is audible once it resumed the context; prints the PERF line
//   node test/e2e.mjs --chrome           Google Chrome instead of the Playwright Chromium
//   node test/e2e.mjs --strict-autoplay  pin Chrome's desktop autoplay policy (--autoplay-policy=document-user-activation-required)
//                                        and require the AudioContext to be suspended until the first click
//   node test/e2e.mjs --preview          test the production build (dist/) through `vite preview` instead of the dev server
//   node test/e2e.mjs --url http://localhost:5173/   test an already running server
//   options: --headed  --timeout <ms, default 90000>  --browser-path <exe>  --flags "<chromium flags>"  --out <dir>
//
// Starts the Vite dev server itself (createServer), launches headless Chromium with a real WebGPU adapter, captures the
// console, and exits 0 when every check passed, 1 when a check failed, 2 when the browser could not be launched.
import { parseArgs } from 'node:util';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright-core';
import pngjs from 'pngjs';
import { createServer, preview } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const { values: a } = parseArgs({
  options: {
    chrome: { type: 'boolean', default: false },
    preview: { type: 'boolean', default: false },
    headed: { type: 'boolean', default: false },
    url: { type: 'string' },
    timeout: { type: 'string', default: '90000' },
    'browser-path': { type: 'string' },
    flags: { type: 'string', default: '' },
    'strict-autoplay': { type: 'boolean', default: false },
    out: { type: 'string', default: path.join(root, 'test', 'out') },
  },
});
const TIMEOUT = Number(a.timeout);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const t0 = Date.now();
const stamp = () => `+${((Date.now() - t0) / 1000).toFixed(1)}s`;
const results = [];
function check(name, ok, detail = '') {
  results.push({ name, ok: !!ok, detail });
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? `  (${detail})` : ''}  ${stamp()}`);
  return !!ok;
}

/** Launch options. Default: Playwright's full Chromium in new headless mode (`channel: 'chromium'`), which has a real WebGPU adapter.
 *  Plain `headless: true` would pick chrome-headless-shell, which has none. `--chrome` = Google Chrome by path. */
function launchOptions() {
  if (a['browser-path']) return { executablePath: a['browser-path'] };
  if (a.chrome) {
    const c = { darwin: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', win32: 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe', linux: '/usr/bin/google-chrome' }[process.platform];
    if (!c || !fs.existsSync(c)) throw new Error(`Google Chrome not found at ${c} (use --browser-path)`);
    return { executablePath: c };
  }
  return { channel: 'chromium' };
}

/** Browser flags: --flags plus, with --strict-autoplay, Chrome's desktop autoplay policy pinned explicitly (a headless or test build may
 *  default to "no-user-gesture-required", where an AudioContext starts without a gesture and hides every "forgot to resume inside the
 *  gesture" bug). NOT `user-gesture-required`: for Web Audio that value only restricts cross-origin iframes, so a top-level page's
 *  context is created 'running' (measured, Chromium 156 and Chrome 155). */
function launchArgs() {
  const args = a.flags.split(' ').filter(Boolean);
  if (a['strict-autoplay']) args.push('--autoplay-policy=document-user-activation-required');
  return args;
}

async function startServer() {
  if (a.url) return { url: a.url, close: async () => {}, startMs: 0 };
  const t = Date.now();
  if (a.preview) {
    const s = await preview({ root, logLevel: 'warn', preview: { port: 5198, strictPort: false } });
    return { url: s.resolvedUrls.local[0], close: () => s.close(), startMs: Date.now() - t };
  }
  const s = await createServer({ root, logLevel: 'info', server: { port: 5199, strictPort: false } });
  await s.listen();
  return { url: s.resolvedUrls.local[0], close: () => s.close(), startMs: Date.now() - t };
}

/** Pixel reader over a PNG screenshot. */
function decode(file) {
  const png = pngjs.PNG.sync.read(fs.readFileSync(file));
  return { width: png.width, height: png.height, at: (x, y) => { const o = (Math.round(y) * png.width + Math.round(x)) * 4; return [png.data[o], png.data[o + 1], png.data[o + 2], png.data[o + 3]]; } };
}
const dist = (p, q) => Math.max(...[0, 1, 2].map((i) => Math.abs(p[i] - q[i])));

// ---- audio helpers (window.__brutal.audio, brutal-browser-audio.js)
const LEVEL_MIN = 0.01; // RMS of the master mix that counts as "sound reaches the output"
const levels = [];      // [label, peak RMS] of every measurement, printed at the end
/** Polls getOutputLevel() inside the page every 10 ms until it exceeds `min` (then keeps sampling for 150 ms to catch the peak) or
 *  `timeout` ms pass; resolves with the peak seen. -1: hook missing. */
const pollLevel = (page, min, timeout) => page.evaluate(async ([lo, ms]) => {
  const get = window.__brutal?.audio?.getOutputLevel;
  if (!get) return -1;
  let peak = 0, hitAt = 0;
  for (const end = performance.now() + ms; ; await new Promise((r) => setTimeout(r, 10))) {
    peak = Math.max(peak, get());
    if (!hitAt && peak > lo) hitAt = performance.now();
    if (hitAt ? performance.now() - hitAt >= 150 : performance.now() >= end) return peak;
  }
}, [min, timeout]);
/** Waits until no voice plays and the output is silent, so the next level measurement cannot be a previous sound's tail. */
const waitSilent = (page) => page.evaluate(async () => {
  const a = window.__brutal?.audio;
  for (const end = performance.now() + 5000; performance.now() < end; await new Promise((r) => setTimeout(r, 25))) if (a.voices() === 0 && a.getOutputLevel() < 0.001) return true;
  return false;
});
const audioState = (page) => page.evaluate(() => window.__brutalAudioState?.() ?? window.__brutal?.audio?.getState?.());

let server, browser, exitCode = 1;
const lines = []; // console messages: { t, type, text }
const problems = [];
const count = (re) => lines.filter((l) => re.test(l.text)).length;
const failing = () => problems.some((p) => p.startsWith('pageerror') || p === 'page crashed') || count(/^ERROR\b/) > 0;
/** Resolves with the first console line (index >= since) matching `re`; null on timeout or as soon as the page reports an error. */
async function waitForLine(re, since, timeout) {
  for (const end = Date.now() + timeout; Date.now() < end;) {
    const hit = lines.slice(since).find((l) => re.test(l.text));
    if (hit) return hit;
    if (failing()) return null;
    await sleep(40);
  }
  return null;
}
const frameCount = () => Math.max(0, ...lines.map((l) => Number(/^FRAME (\d+)\b/.exec(l.text)?.[1] ?? 0)));
/** The last `CAMERA yaw=.. pitch=.. dist=.. pan=..` line, parsed (architecture.md section 12). */
function lastCamera() {
  const l = lines.findLast((x) => x.text.startsWith('CAMERA '));
  const m = l && /yaw=(-?[\d.]+) pitch=(-?[\d.]+) dist=([\d.]+)/.exec(l.text);
  return m ? { yaw: Number(m[1]), pitch: Number(m[2]), dist: Number(m[3]) } : null;
}
try {
  let launch;
  try { launch = launchOptions(); } catch (e) { console.error(e.message); process.exit(2); }
  server = await startServer();
  console.log(`server ${server.url} (${server.startMs} ms to start)`);
  try {
    browser = await chromium.launch({ ...launch, headless: !a.headed, args: launchArgs(), timeout: 30000 });
  } catch (e) { console.error(`cannot launch ${JSON.stringify(launch)}: ${e.message}`); process.exit(2); }
  console.log(`browser ${a.chrome ? 'Google Chrome' : 'Chromium'} ${browser.version()}  ${launch.executablePath ?? `playwright channel ${launch.channel}`}`);
  const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, deviceScaleFactor: 1 });
  const page = await context.newPage();
  page.on('console', (m) => lines.push({ t: Date.now() - t0, type: m.type(), text: m.text() }));
  page.on('pageerror', (e) => problems.push(`pageerror: ${e.message}`));
  page.on('crash', () => problems.push('page crashed'));
  page.on('requestfailed', (r) => problems.push(`request failed: ${r.url()} ${r.failure()?.errorText}`));
  page.on('response', (r) => { if (r.status() >= 400) problems.push(`HTTP ${r.status()}: ${r.url()}`); });

  const tNav = Date.now();
  await page.goto(server.url, { waitUntil: 'load' });

  // ---- boot
  const ready = await waitForLine(/^READY$/, 0, TIMEOUT);
  check('boot: READY', ready, ready ? `${Date.now() - tNav} ms after navigation` : failing() ? 'boot failed: ' + (lines.find((l) => /^ERROR\b/.test(l.text))?.text ?? problems[0] ?? '').slice(0, 160) : `not seen within ${TIMEOUT} ms`);
  if (!ready) throw new Error('no READY');
  const hostLine = lines.find((l) => l.text.startsWith('HOST '))?.text;
  if (hostLine) console.log(`     ${hostLine}`);

  // ---- audio before the first gesture: the sound is decoded at boot, so the context already exists. Under the browsers' autoplay
  // policy it is suspended; a sound started now must be deferred (played once the context runs), not lost.
  // Everything here evaluates through CDP with userGesture:false: page.evaluate() and locator calls run with a user gesture, which
  // would give the document the activation that unlocks the AudioContext and make this phase meaningless.
  const cdp = await context.newCDPSession(page);
  const plain = async (expression) => (await cdp.send('Runtime.evaluate', { expression, returnByValue: true, userGesture: false })).result.value;
  const loaded = await waitForLine(/^AUDIO loaded$/, 0, TIMEOUT);
  check('AUDIO loaded (decoded before the first gesture)', loaded, loaded ? '' : 'not seen');
  const audioStateExpr = 'window.__brutalAudioState?.() ?? window.__brutal?.audio?.getState?.()';
  const stateBefore = await plain(audioStateExpr);
  if (a['strict-autoplay']) check('strict autoplay: AudioContext is suspended before the first gesture', stateBefore === 'suspended', String(stateBefore));
  else console.log(`INFO  AudioContext state before the first gesture: ${stateBefore}`);
  check('#play-sound is enabled once booted', await plain("document.getElementById('play-sound').disabled === false"));
  let deferred = false;
  if (stateBefore === 'suspended') {
    // a click dispatched from a timer is no user activation: the page's resume() cannot start the context, the voice has to wait for it
    const s0 = lines.length;
    await plain("setTimeout(() => document.getElementById('play-sound').click(), 0), 0");
    const early = await waitForLine(/^AUDIO play\b/, s0, 15000);
    check('play-sound while the context is suspended -> AUDIO play', early, early?.text ?? 'none within 15 s');
    await sleep(300);
    const stateEarly = await plain(audioStateExpr);
    if (a['strict-autoplay']) check('strict autoplay: the context stays suspended without a user activation', stateEarly === 'suspended', String(stateEarly));
    else console.log(`INFO  state after the click without activation: ${stateEarly}`);
    deferred = !!early && stateEarly === 'suspended';
  }

  // ---- start overlay, frames
  await page.click('#start', { timeout: 15000 });
  check('start overlay hidden after click', await page.locator('#overlay').isHidden());
  if (deferred) {
    const lvl = await pollLevel(page, LEVEL_MIN, 1500); // the sound is 1.5 s long; resuming a context can take a few hundred ms
    levels.push(['deferred voice after the first gesture', lvl]);
    check(`voice started while suspended is audible after the first click (level > ${LEVEL_MIN})`, lvl > LEVEL_MIN, `peak RMS ${lvl.toFixed(3)}`);
  }
  let framesOk = null;
  for (const end = Date.now() + TIMEOUT; !framesOk && !failing() && Date.now() < end; await sleep(100)) framesOk = frameCount() >= 120 ? lines.find((l) => l.text === `FRAME ${frameCount()}`) : null;
  check('frames: FRAME 120', framesOk, framesOk ? `${framesOk.t - (lines.find((l) => /^READY$/.test(l.text))?.t ?? 0)} ms after READY` : 'not reached');
  if (!framesOk) throw new Error('no frames');

  // ---- pixels: screenshot of the page (overlay is hidden, canvas fills the viewport)
  fs.mkdirSync(a.out, { recursive: true });
  const shotPath = path.join(a.out, 'frame.png');
  await page.screenshot({ path: shotPath });
  const box = await page.evaluate(() => { const c = document.getElementById('canvas'); const r = c.getBoundingClientRect(); return { x: r.x, y: r.y, w: r.width, h: r.height, cw: c.width, ch: c.height, dpr: devicePixelRatio }; });
  const png = decode(shotPath);
  const centre = png.at((box.x + box.w / 2) * box.dpr, (box.y + box.h / 2) * box.dpr);
  const corner = png.at((box.x + 4) * box.dpr, (box.y + 4) * box.dpr);
  check('canvas fills the viewport (DPR aware)', box.w >= 1280 - 1 && box.h >= 720 - 1 && Math.abs(box.cw - box.w * box.dpr) <= 1, `css ${box.w}x${box.h}, backing ${box.cw}x${box.ch}, dpr ${box.dpr}`);
  check('screenshot: centre pixel differs from the corner clear colour', dist(centre, corner) > 12, `centre rgba(${centre}) corner rgba(${corner}) -> ${shotPath}`);
  const hooks = await page.evaluate(() => ({ readPixel: typeof window.__brutal?.readPixel === 'function', frameCount: window.__brutal?.frameCount, lastError: window.__brutal?.lastError }));
  if (hooks.readPixel) {
    const px = await page.evaluate(async ([cx, cy]) => ({ centre: await window.__brutal.readPixel(cx, cy), corner: await window.__brutal.readPixel(4, 4) }), [Math.floor(box.cw / 2), Math.floor(box.ch / 2)]);
    check('window.__brutal.readPixel: centre differs from corner', dist(px.centre, px.corner) > 12, `centre [${px.centre}] corner [${px.corner}]`);
  } else console.log('SKIP  window.__brutal.readPixel not exposed (webgpu-glue.js sets it in the real build)');
  if (hooks.frameCount !== undefined) check('window.__brutal.frameCount >= 120', hooks.frameCount >= 120, String(hooks.frameCount));
  if (hooks.lastError) check('window.__brutal.lastError is empty', false, String(hooks.lastError));

  // ---- input and audio
  const cx = box.x + box.w / 2, cy = box.y + box.h / 2;
  const waitCamera = async (since) => waitForLine(/^CAMERA /, since, 15000);
  const quiet = async () => { for (let n = count(/^CAMERA /); ; n = count(/^CAMERA /)) { await sleep(400); if (count(/^CAMERA /) === n) return; } };

  let since = lines.length;
  await page.mouse.move(cx, cy);
  await page.mouse.down();
  await page.mouse.move(cx + 160, cy + 60, { steps: 16 });
  await page.mouse.up();
  const cam1 = await waitCamera(since);
  check('mouse drag -> CAMERA line', cam1, cam1?.text ?? 'none within 15 s');
  await quiet();

  // A purely horizontal drag of DRAG_PX CSS pixels turns the camera by DRAG_PX * 0.4 degrees (InputController.DegreesPerPixel;
  // GLFW cursor coordinates are CSS pixels) and leaves the pitch alone. Yaw wraps at 360 (OrbitCamera.Rotate).
  const DRAG_PX = 200, DEG_PER_PX = 0.4;
  const before = lastCamera();
  since = lines.length;
  await page.mouse.move(cx - DRAG_PX / 2, cy);
  await page.mouse.down();
  await page.mouse.move(cx + DRAG_PX / 2, cy, { steps: 20 });
  await page.mouse.up();
  await waitCamera(since);
  await quiet();
  const after = lastCamera();
  const turned = before && after ? ((after.yaw - before.yaw) % 360 + 540) % 360 - 180 : NaN;
  check(`${DRAG_PX} px horizontal drag turns the camera by ${DRAG_PX * DEG_PER_PX} deg (pitch unchanged)`,
    Math.abs(turned - DRAG_PX * DEG_PER_PX) <= 0.5 && before?.pitch === after?.pitch,
    `yaw ${before?.yaw} -> ${after?.yaw} (delta ${turned.toFixed(2)}), pitch ${before?.pitch} -> ${after?.pitch}`);

  since = lines.length;
  await page.keyboard.press('ArrowUp', { delay: 250 });
  const cam2 = await waitCamera(since);
  check('ArrowUp -> another CAMERA line', cam2, cam2?.text ?? 'none within 15 s');
  await quiet();
  const zoomed = lastCamera();
  check('ArrowUp reduces the camera distance', zoomed && after && zoomed.dist < after.dist,
    `dist ${after?.dist} -> ${zoomed?.dist} (x${zoomed && after ? (zoomed.dist / after.dist).toFixed(3) : '?'}, one step = x0.9)`);

  // Space is handled by window-level key listeners (brutal-browser.js), so it must work whichever element has focus: drop the focus first
  check('AudioContext state is running after the start click', (await audioState(page)) === 'running', String(await audioState(page)));
  const silentBefore = await waitSilent(page);
  const focused = await page.evaluate(() => { const e = document.activeElement; const tag = e?.id ? `#${e.id}` : e?.tagName; e?.blur?.(); return tag; });
  since = lines.length;
  await page.keyboard.press('Space', { delay: 100 });
  const play = await waitForLine(/^AUDIO play\b/, since, 15000);
  check(`Space -> AUDIO play (focus was ${focused}, then blurred)`, play, play?.text ?? 'none within 15 s');
  const lvlSpace = play ? await pollLevel(page, LEVEL_MIN, 500) : 0;
  levels.push(['Space', lvlSpace]);
  check(`Space -> output level > ${LEVEL_MIN} within 500 ms`, silentBefore && lvlSpace > LEVEL_MIN, `peak RMS ${lvlSpace.toFixed(3)}${silentBefore ? '' : ' (output was not silent before)'}`);
  check('no AUDIO error', count(/^AUDIO error\b/) === 0, lines.find((l) => /^AUDIO error/.test(l.text))?.text ?? '');

  // the page button: BrowserHost.Dispatch('play-sound') from the click handler, after audio.imports.resume()
  const silentBefore2 = await waitSilent(page);
  since = lines.length;
  await page.click('#play-sound');
  const play2 = await waitForLine(/^AUDIO play\b/, since, 15000);
  check('#play-sound click -> AUDIO play', play2, play2?.text ?? 'none within 15 s');
  const lvlButton = play2 ? await pollLevel(page, LEVEL_MIN, 500) : 0;
  levels.push(['#play-sound', lvlButton]);
  check(`#play-sound click -> output level > ${LEVEL_MIN} within 500 ms`, silentBefore2 && lvlButton > LEVEL_MIN, `peak RMS ${lvlButton.toFixed(3)}${silentBefore2 ? '' : ' (output was not silent before)'}`);
  check('#play-sound click moves focus back to the canvas', (await page.evaluate(() => document.activeElement?.id)) === 'canvas');
  check('no AUDIO error after the button', count(/^AUDIO error\b/) === 0, lines.find((l) => /^AUDIO error/.test(l.text))?.text ?? '');

  // an unknown command is reported, not thrown (BrowserHost.Dispatch), through the same export the button uses (window.__brutalDispatch)
  since = lines.length;
  await page.evaluate(() => window.__brutalDispatch('no-such-command'));
  const warn = await waitForLine(/^WARN unknown command no-such-command$/, since, 5000);
  check('Dispatch of an unknown command -> WARN unknown command <name>', warn, warn?.text ?? 'none within 5 s');

  const stateAfter = await audioState(page);
  if (stateAfter === undefined) console.log('SKIP  AudioContext state: neither window.__brutalAudioState() nor window.__brutal.audio.getState() is exposed');
  else check('AudioContext state is running', stateAfter === 'running', stateAfter);
  console.log(`INFO  output levels (peak RMS of the master mix): ${levels.map(([k, v]) => `${k} = ${v.toFixed(3)}`).join('; ')}`);

  // frame-time report of the browser host (`PERF avg=<ms> p95=<ms> ...` every 300 frames); informational, not a check
  const perf = await waitForLine(/^PERF /, 0, 12000);
  console.log(perf ? `INFO  ${perf.text}` : 'INFO  no PERF line within 12 s');

  // ---- no errors anywhere
  const errText = await page.evaluate(() => { const e = document.getElementById('error'); return e && !e.hidden ? e.textContent : ''; });
  check('#error overlay is empty', !errText, errText?.trim().split('\n')[0]);
  check('no ERROR lines in the console', count(/^ERROR\b/) === 0, lines.find((l) => /^ERROR\b/.test(l.text))?.text ?? '');
  check('no page errors, failed requests or HTTP >= 400', problems.length === 0, problems.slice(0, 3).join(' | '));
} catch (e) {
  if (!/^no (READY|frames)$/.test(e.message)) check('unexpected exception', false, e.stack?.split('\n').slice(0, 3).join(' | '));
  else if (failing()) console.log(`problems: ${problems.join(' | ')}`);
} finally {
  fs.mkdirSync(a.out, { recursive: true });
  fs.writeFileSync(path.join(a.out, 'console.log'), lines.map((l) => `[${(l.t / 1000).toFixed(2)}s ${l.type}] ${l.text}`).join('\n') + '\n');
  // console excerpt: marker lines plus warnings/errors, with runs of the same marker (CAMERA, FRAME) collapsed
  const interesting = lines.filter((l) => /^(HOST|READY|FRAME |CAMERA|AUDIO|PERF|ERROR)/.test(l.text) || l.type === 'error' || l.type === 'warning');
  console.log(`\nconsole excerpt (${interesting.length} of ${lines.length} console lines; full log: ${path.join(a.out, 'console.log')}):`);
  for (let i = 0; i < interesting.length;) {
    const key = (l) => l.text.split(' ')[0];
    let j = i; while (j + 1 < interesting.length && key(interesting[j + 1]) === key(interesting[i])) j++;
    const show = (l) => console.log(`  ${l.type === 'log' ? '' : `[${l.type}] `}${l.text.slice(0, 200)}`);
    if (j - i >= 3) { show(interesting[i]); console.log(`  ... ${j - i - 1} more ${key(interesting[i])} lines`); show(interesting[j]); } else for (let k = i; k <= j; k++) show(interesting[k]);
    i = j + 1;
  }
  const failed = results.filter((r) => !r.ok);
  if (results.length) {
    console.log(`\n${results.length - failed.length}/${results.length} checks passed in ${((Date.now() - t0) / 1000).toFixed(1)} s${failed.length ? '; FAILED: ' + failed.map((f) => f.name).join('; ') : ''}`);
    exitCode = failed.length ? 1 : 0;
  }
  // Closing Chromium can take seconds on a busy machine; the verdict is already known, so do not wait for it forever.
  await Promise.race([Promise.allSettled([browser?.close(), server?.close()]), sleep(10000)]);
}
process.exit(exitCode);
