// Playwright end-to-end test of the web ICD smoke app: a textured, depth-tested triangle pair rendered through the real
// Brutal.Vulkan API on WebGPU. Pass criteria: READY and FRAME 120 on the console, no ERROR lines and no WebGPU errors,
// GPU-readback pixels (window.__brutal.readPixel) and screenshot pixels match the expected colours.
//
// Usage: node run.mjs [--no-build] [--config Release|Debug] [--chrome | --browser <exe>] [--headed] [--out <dir>]
//   default: `dotnet publish -c Release` (trimmed), serve publish/wwwroot, headless Chromium (Playwright's build, channel 'chromium').
//   --chrome: Google Chrome from /Applications instead. Exit code 0 = pass.
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
const config = opt('--config', 'Release');
const chromeExe = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const exe = flag('--chrome') ? chromeExe : opt('--browser', undefined);
const label = exe ? path.basename(exe).replace(/\s+/g, '-').toLowerCase() : 'chromium';
const outDir = path.resolve(here, opt('--out', 'out'));
fs.mkdirSync(outDir, { recursive: true });

const log = [];
const t0 = Date.now();
const rec = (s) => { const line = `[+${String(Date.now() - t0).padStart(6)}ms] ${s}`; log.push(line); console.log(line); };

if (!flag('--no-build')) {
  rec(`[driver] dotnet publish -c ${config}`);
  const r = spawnSync('dotnet', ['publish', projectDir, '-c', config, '--nologo', '-v', 'q'], { stdio: 'inherit' });
  if (r.status !== 0) { console.error('dotnet publish failed'); process.exit(2); }
}
const wwwroot = [`bin/${config}/net10.0/publish/wwwroot`, `bin/${config}/net10.0/browser-wasm/publish/wwwroot`]
  .map((p) => path.join(projectDir, p)).find((p) => fs.existsSync(path.join(p, 'index.html')));
if (!wwwroot) { console.error('no published wwwroot (run without --no-build)'); process.exit(2); }
if (!fs.existsSync(path.join(wwwroot, 'webgpu-glue.js'))) { console.error(`webgpu-glue.js missing from ${wwwroot}`); process.exit(2); }

// The expected colours follow from TriangleScene: texel (1,1) yellow * push tint (1,.5,1) at the centre; texel (0,1) blue at
// the lower left of the front triangle; the back triangle (yellow * (.25,1,1) * (1,.5,1)) only below the front one; clear colour.
const W = 640, H = 480;
const ndc = (x, y) => [Math.round((x + 1) / 2 * W), Math.round((y + 1) / 2 * H)];
const probes = [
  { name: 'centre: front triangle, texel (1,1) * tint', at: ndc(0, 0), expect: [255, 128, 0], tol: 3 },
  { name: 'front triangle, texel (0,1)', at: ndc(-0.45, 0.6), expect: [0, 0, 255], tol: 3 },
  { name: 'back triangle below the front one (depth test passes)', at: ndc(0, 0.88), expect: [64, 128, 0], tol: 3 },
  { name: 'corner: clear colour (0.1, 0.2, 0.3)', at: [4, 4], expect: [26, 51, 77], tol: 2 },
];
const near = (p, e, tol) => p.slice(0, 3).every((v, i) => Math.abs(v - e[i]) <= tol);

const results = [];
const check = (name, ok, detail = '') => { results.push({ name, ok }); rec(`[check] ${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ` - ${detail}` : ''}`); };

const server = await startServer(wwwroot, 0);
const url = `http://localhost:${server.address().port}/index.html`;
const browser = await chromium.launch({ headless: !flag('--headed'), ...(exe ? { executablePath: exe } : { channel: 'chromium' }) });
rec(`[driver] browser ${label} ${browser.version()} serving ${wwwroot}`);

let exitCode = 1;
try {
  const page = await browser.newPage({ viewport: { width: 800, height: 600 }, deviceScaleFactor: 1 });
  const errors = [];
  const waiters = [];
  page.on('console', (m) => {
    const text = m.text();
    rec(`[${m.type()}] ${text}`);
    if (m.type() === 'error' || text.startsWith('ERROR') || text.includes('WEBGPU ERROR')) errors.push(text);
    for (const w of [...waiters]) if (text.includes(w.marker)) { waiters.splice(waiters.indexOf(w), 1); w.resolve(text); }
  });
  page.on('pageerror', (e) => { rec(`[pageerror] ${e.message}`); errors.push(e.message); });
  // waits for a console marker; fails early once an error was logged (no point waiting for frames after a fatal error)
  const waitFor = (marker, ms = 90000) => new Promise((resolve, reject) => {
    if (log.some((l) => l.includes(`] ${marker}`) || l.endsWith(marker))) { resolve(marker); return; }
    waiters.push({ marker, resolve });
    const started = Date.now();
    const poll = setInterval(() => {
      if (!waiters.some((w) => w.marker === marker)) { clearInterval(poll); return; }
      if (errors.length || Date.now() - started > ms) {
        clearInterval(poll);
        reject(new Error(errors.length ? `error before "${marker}": ${errors[0]}` : `timeout waiting for "${marker}"`));
      }
    }, 250);
  });

  await page.goto(url, { waitUntil: 'load' });
  await waitFor('READY');
  check('READY printed', true);
  await waitFor('FRAME 120');
  check('FRAME 120 reached', true);

  // GPU -> CPU through Vulkan: vkCmdCopyImageToBuffer into host-visible memory at frame 30, fence polled across frames
  const readback = log.find((l) => l.includes('] READBACK '));
  const bytes = readback ? readback.split('READBACK ')[1].split(' ')[0].split(',').map(Number) : [];
  const bgra = readback?.includes('format=B8G8R8A8');
  const rgba = bgra ? [bytes[2], bytes[1], bytes[0], bytes[3]] : bytes;
  const compute = log.find((l) => l.includes('] COMPUTE '));
  check('compute dispatch (storage buffer + push constant) read back', !!compute && compute.includes('ok=True'), compute ?? 'no COMPUTE line');
  const stress = log.find((l) => l.includes('] PUSHSTRESS '));
  check('push-constant ring grows past 4096 updates in one submit (no error)', !!stress, stress ?? 'no PUSHSTRESS line');
  check('vkCmdCopyImageToBuffer readback of the centre pixel', !!readback && near(rgba, [255, 128, 0], 3), readback ?? 'no READBACK line');

  // GPU readback of the canvas texture (served at the next present)
  for (const p of probes) {
    const px = await page.evaluate(([x, y]) => window.__brutal.readPixel(x, y), p.at);
    check(`readPixel ${p.name} @${p.at}`, near(px, p.expect, p.tol), `got [${px}] expected [${p.expect}] ±${p.tol}`);
  }

  // screenshot of the canvas element, decoded in the page
  const png = await page.locator('#canvas').screenshot({ path: path.join(outDir, `smoke-${label}.png`) });
  const shot = await page.evaluate(async ([b64, points]) => {
    const img = new Image();
    img.src = 'data:image/png;base64,' + b64;
    await img.decode();
    const c = new OffscreenCanvas(img.width, img.height);
    const g = c.getContext('2d', { willReadFrequently: true });
    g.drawImage(img, 0, 0);
    return points.map(([x, y]) => [...g.getImageData(x, y, 1, 1).data]);
  }, [png.toString('base64'), probes.map((p) => p.at)]);
  probes.forEach((p, i) => check(`screenshot ${p.name}`, near(shot[i], p.expect, p.tol + 4), `got [${shot[i]}]`));

  const state = await page.evaluate(() => ({ frameCount: window.__brutal.frameCount, lastError: window.__brutal.lastError }));
  check('window.__brutal.frameCount >= 120', state.frameCount >= 120, `frameCount ${state.frameCount}`);
  check('no WebGPU error (window.__brutal.lastError)', state.lastError === null, String(state.lastError));
  check('no ERROR lines / console errors', errors.length === 0, errors.slice(0, 5).join(' | '));
  exitCode = results.every((r) => r.ok) ? 0 : 1;
} catch (e) {
  rec(`[driver] FAIL ${e.stack ?? e}`);
} finally {
  rec(`[driver] RESULT ${exitCode === 0 ? 'PASS' : 'FAIL'} (${results.filter((r) => r.ok).length}/${results.length} checks, ${label}, ${config})`);
  fs.writeFileSync(path.join(outDir, `console-${label}-${config}.log`), log.join('\n') + '\n');
  await browser.close().catch(() => { });
  server.close();
  process.exit(exitCode);
}
