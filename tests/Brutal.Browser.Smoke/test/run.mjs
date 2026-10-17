// Playwright end-to-end test for the Brutal.Browser smoke app (input -> GLFW events, Web Audio, frame loop).
//
// Usage: node run.mjs [--no-build] [--headed] [--strict-autoplay] [--browser <exe>] [--out <console.log>]
//   default: `dotnet publish -c Release` the smoke app, serve bin/Release/net10.0/publish/wwwroot, run four passes:
//     1. default (decodeAudioData): start click, key/mouse/cursor/scroll/resize events, audio running and a measured output level
//        (window.__brutal.audio.getOutputLevel() > 0.01 within 500 ms of AUDIO play), FRAME 120
//     2. ?managed=1: the NVorbis fallback decodes the Ogg (same output level check)
//     3. ?crash: an exception in the frame -> `ERROR <message>` and the loop stops
//     4. deviceScaleFactor 2: canvas pixel size / content scale
//   --strict-autoplay pins Chrome's desktop autoplay policy (document-user-activation-required), so an AudioContext created before
//   the start click stays suspended until the page resumes it inside the click.
//   Exit code 0 = all checks passed.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { startServer } from './serve.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const projectDir = path.resolve(here, '..');
const argv = process.argv.slice(2);
const flag = (name) => argv.includes(name);
const opt = (name, def) => { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : def; };

const outFile = path.resolve(here, opt('--out', 'console.log'));
const log = [];
const t0 = Date.now();
const rec = (s) => { const line = `[+${String(Date.now() - t0).padStart(6)}ms] ${s}`; log.push(line); console.log(line); };

// ---- build ---------------------------------------------------------------------------------------------------------
if (!flag('--no-build')) {
  rec('[driver] dotnet publish -c Release (smoke app)');
  const r = spawnSync('dotnet', ['publish', projectDir, '-c', 'Release', '--nologo', '-v', 'q'], { stdio: 'inherit' });
  if (r.status !== 0) { console.error('dotnet publish failed'); process.exit(2); }
}
const wwwroot = ['bin/Release/net10.0/publish/wwwroot', 'bin/Release/net10.0/browser-wasm/publish/wwwroot', 'bin/Release/net10.0/browser-wasm/wwwroot']
  .map((p) => path.join(projectDir, p)).find((p) => fs.existsSync(path.join(p, 'index.html')));
if (!wwwroot) { console.error('no published wwwroot found; run without --no-build'); process.exit(2); }

// ---- browser -------------------------------------------------------------------------------------------------------
// Full Chromium (channel 'chromium', the build matching Playwright 1.64.1; it has a WebGPU adapter, like the other e2e
// tests). Override with --browser <exe> / CHROME_PATH. Pages are served over http://localhost (a secure context).
const server = await startServer(wwwroot, 0, { log: (m) => rec(`[server] ${m}`) });
const base = `http://localhost:${server.address().port}/index.html`;
const explicitExe = opt('--browser', process.env.CHROME_PATH);
const browser = await chromium.launch({
  headless: !flag('--headed'),
  ...(explicitExe ? { executablePath: explicitExe } : { channel: 'chromium' }),
  args: ['--no-first-run', ...(flag('--strict-autoplay') ? ['--autoplay-policy=document-user-activation-required'] : [])],
});
rec(`[driver] browser=${browser.version()} serving ${wwwroot} at ${base}`);

// ---- checks --------------------------------------------------------------------------------------------------------
const results = [];
function check(name, ok, detail = '') {
  results.push({ name, ok });
  rec(`[check] ${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ` - ${detail}` : ''}`);
}

async function openPage(query, contextOptions = { viewport: { width: 800, height: 600 } }) {
  const context = await browser.newContext(contextOptions);
  const page = await context.newPage();
  const lines = [];
  const errors = [];
  page.on('console', (msg) => {
    const text = msg.text();
    lines.push(text);
    rec(`[console.${msg.type()}] ${text}`);
    if (msg.type() === 'error') errors.push(text);
  });
  page.on('pageerror', (err) => { errors.push(String(err)); rec(`[pageerror] ${err && err.stack ? err.stack : err}`); });
  page.on('requestfailed', (req) => rec(`[requestfailed] ${req.url()}`));
  page.on('response', (res) => { if (res.status() >= 400 && !res.url().endsWith('/favicon.ico')) rec(`[http ${res.status()}] ${res.url()}`); });

  const waitLine = async (re, timeoutMs = 30000, from = 0) => {
    const start = Date.now();
    for (;;) {
      const hit = lines.slice(from).find((l) => re.test(l));
      if (hit !== undefined) return hit;
      if (Date.now() - start > timeoutMs) return null;
      await new Promise((r) => setTimeout(r, 25));
    }
  };
  await page.goto(base + query, { waitUntil: 'load' });
  return { page, lines, errors, waitLine };
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
/** Polls window.__brutal.audio.getOutputLevel() (RMS of the master mix, 0..1) inside the page every 10 ms; resolves with the peak as soon
 *  as it exceeds `min` (sampling 150 ms more to catch the peak) or after `timeout` ms. -1: hook missing. */
const pollLevel = (page, min, timeout) => page.evaluate(async ([lo, ms]) => {
  const get = globalThis.__brutal?.audio?.getOutputLevel;
  if (!get) return -1;
  let peak = 0, hitAt = 0;
  for (const end = performance.now() + ms; ; await new Promise((r) => setTimeout(r, 10))) {
    peak = Math.max(peak, get());
    if (!hitAt && peak > lo) hitAt = performance.now();
    if (hitAt ? performance.now() - hitAt >= 150 : performance.now() >= end) return peak;
  }
}, [min, timeout]);
const has = (lines, re) => lines.some((l) => re.test(l));

// ---- pass 1: default ----------------------------------------------------------------------------------------------
async function passDefault() {
  rec('[driver] ===== pass 1: default (decodeAudioData)');
  const { page, lines, errors, waitLine } = await openPage('');

  check('READY printed', !!(await waitLine(/^READY$/, 60000)));
  check('page ready', !!(await waitLine(/^HOST ready$/, 10000)));
  check('FRAME 60 before any input', !!(await waitLine(/^FRAME 60$/)));
  check('GLFW extensions advertised', has(lines, /GLFW extensions=\[VK_KHR_surface,VK_BRUTAL_web_surface\]/));
  check('HOST canvas size read', has(lines, /^HOST canvas css=800x600 pixels=800x600 dpr=1$/));
  check('BRUTAL LogSystem reaches the console', has(lines, /^\[info\] Glfw: browser GLFW shim initialised: /));

  // start overlay click = the user gesture for audio
  await page.click('#start');
  check('AUDIO loaded', !!(await waitLine(/^AUDIO loaded /, 30000)));
  check('AUDIO play', !!(await waitLine(/^AUDIO play$/, 10000)));
  const level = await pollLevel(page, 0.01, 500);
  check('output level > 0.01 within 500 ms of AUDIO play', level > 0.01, `peak RMS ${level.toFixed(3)}`);
  const loaded = lines.find((l) => l.startsWith('AUDIO loaded'));
  check('decodeAudioData path: stereo 48 kHz ~1.54 s', /decoder=WebAudio channels=2 sampleRate=48000 duration=1\.54\d/.test(loaded ?? ''), loaded);
  const audioState = await page.evaluate(() => globalThis.__brutal.audio.getState());
  check('AudioContext state is running', audioState === 'running', audioState);
  check('one voice is playing', (await page.evaluate(() => globalThis.__brutal.audio.voices())) >= 1);

  // ---- synthetic input
  await page.mouse.move(200, 150);
  await page.mouse.down();
  await page.mouse.move(280, 210, { steps: 4 });
  await page.mouse.up();
  await page.mouse.wheel(0, 100);
  await page.keyboard.press('ArrowLeft');
  await page.keyboard.press('a');
  await sleep(300);
  await page.setViewportSize({ width: 700, height: 500 });
  await sleep(300);

  check('EVT key ArrowLeft press', !!(await waitLine(/^EVT key key=Left scancode=57419 action=Press mods=0$/, 5000)));
  check('EVT key ArrowLeft release', !!(await waitLine(/^EVT key key=Left scancode=57419 action=Release mods=0$/, 5000)));
  check('EVT key A press + char 97', has(lines, /^EVT key key=A scancode=30 action=Press/) && has(lines, /^EVT char codepoint=97$/));
  check('EVT mouse Left press', !!(await waitLine(/^EVT mouse button=Left action=Press mods=0$/, 5000)));
  check('EVT mouse Left release', !!(await waitLine(/^EVT mouse button=Left action=Release mods=0$/, 5000)));
  check('EVT cursor 200,150', has(lines, /^EVT cursor x=200 y=150$/));
  check('EVT cursor 280,210 (drag end)', has(lines, /^EVT cursor x=280 y=210$/));
  check('EVT scroll dx=0 dy=-1 (100 px wheel down)', !!(await waitLine(/^EVT scroll dx=0 dy=-1$/, 5000)));
  check('EVT resize 700x500 (viewport resize)', !!(await waitLine(/^EVT resize size=700x500 fb=700x500 scale=1$/, 5000)));
  const size = await page.evaluate(() => ({ w: document.getElementById('canvas').width, h: document.getElementById('canvas').height }));
  check('canvas backing store follows the viewport', size.w === 700 && size.h === 500, JSON.stringify(size));

  // pointer lock through SetInputMode(Cursor, Disabled) (informational: depends on the headless browser granting the lock)
  await page.keyboard.press('l');
  await sleep(500);
  const modeLine = lines.find((l) => l.startsWith('EVT cursormode'));
  check('EVT cursormode Disabled (SetInputMode)', modeLine === 'EVT cursormode Disabled', modeLine);
  const locked = await page.evaluate(() => globalThis.__brutal.browser.locked());
  rec(`[info] pointer lock acquired in headless Chromium: ${locked}`);

  // blur releases keys: hold a key, blur the page, expect a synthetic release
  await page.keyboard.down('d');
  await sleep(200);
  const before = lines.length;
  await page.evaluate(() => window.dispatchEvent(new Event('blur')));
  check('blur releases the held key', !!(await waitLine(/^EVT key key=D scancode=0 action=Release mods=0$/, 5000, before)));
  check('blur reports LostFocus', !!(await waitLine(/^EVT focus LostFocus$/, 5000, before)));
  await page.keyboard.up('d');

  check('FRAME 120', !!(await waitLine(/^FRAME 120$/, 30000)));
  check('no ERROR lines / page errors', !has(lines, /^ERROR /) && errors.length === 0, errors.join(' | '));
  await page.close();
}

// ---- pass 2: managed decoder -------------------------------------------------------------------------------------
async function passManaged() {
  rec('[driver] ===== pass 2: NVorbis fallback (?managed)');
  const { page, lines, errors, waitLine } = await openPage('?managed=1');
  check('[managed] READY', !!(await waitLine(/^READY$/, 60000)));
  await waitLine(/^HOST ready$/, 10000);
  await page.click('#start');
  const loaded = await waitLine(/^AUDIO loaded /, 120000);
  check('[managed] AUDIO loaded by NVorbis: stereo 48 kHz ~1.54 s', /decoder=Managed channels=2 sampleRate=48000 duration=1\.54\d/.test(loaded ?? ''), loaded);
  check('[managed] AUDIO play', !!(await waitLine(/^AUDIO play$/, 10000)));
  const level = await pollLevel(page, 0.01, 500);
  check('[managed] output level > 0.01 within 500 ms of AUDIO play', level > 0.01, `peak RMS ${level.toFixed(3)}`);
  const audioState = await page.evaluate(() => globalThis.__brutal.audio.getState());
  check('[managed] AudioContext state is running', audioState === 'running', audioState);
  check('[managed] no ERROR lines / page errors', !has(lines, /^ERROR |^AUDIO error/) && errors.length === 0, errors.join(' | '));
  await page.close();
}

// ---- pass 3: exception in the frame ------------------------------------------------------------------------------
async function passCrash() {
  rec('[driver] ===== pass 3: exception in the frame (?crash)');
  const { page, lines, waitLine } = await openPage('?crash=1');
  check('[crash] ERROR <message> printed', !!(await waitLine(/^ERROR smoke crash test$/, 60000)));
  check('[crash] loop stopped', !!(await waitLine(/^HOST loop stopped$/, 10000)));
  const smoke = await page.evaluate(() => globalThis.__brutal.smoke);
  check('[crash] Tick returned false (no thrown JS error)', smoke.stopped === true && smoke.error === null, JSON.stringify(smoke));
  await sleep(300);
  check('[crash] no FRAME lines after the failure', !has(lines, /^FRAME /));
  await page.close();
}

// ---- pass 4: device pixel ratio ----------------------------------------------------------------------------------
async function passDpr() {
  rec('[driver] ===== pass 4: devicePixelRatio 2');
  const { page, lines, waitLine } = await openPage('', { viewport: { width: 400, height: 300 }, deviceScaleFactor: 2 });
  check('[dpr] canvas pixels = 2 x css', !!(await waitLine(/^HOST canvas css=400x300 pixels=800x600 dpr=2$/, 60000)));
  const size = await page.evaluate(() => ({ w: document.getElementById('canvas').width, h: document.getElementById('canvas').height }));
  check('[dpr] canvas backing store is 800x600', size.w === 800 && size.h === 600, JSON.stringify(size));
  await page.setViewportSize({ width: 300, height: 200 });
  check('[dpr] resize reports css and framebuffer', !!(await waitLine(/^EVT resize size=300x200 fb=600x400 scale=2$/, 5000)) && has(lines, /^EVT framebuffer size=600x400$/));
  await page.close();
}

let crashed = null;
try {
  await passDefault();
  await passManaged();
  await passCrash();
  await passDpr();
} catch (e) {
  crashed = e;
  rec(`[driver] exception: ${e && e.stack || e}`);
}

await browser.close();
server.close();

const failed = results.filter((r) => !r.ok);
rec(`[driver] ${results.length - failed.length}/${results.length} checks passed${crashed ? ' (driver exception)' : ''}`);
fs.writeFileSync(outFile, log.join('\n') + '\n');
console.log(`wrote ${log.length} lines to ${outFile}`);
process.exit(failed.length === 0 && !crashed ? 0 : 1);
