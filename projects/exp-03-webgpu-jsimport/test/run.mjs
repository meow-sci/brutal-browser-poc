// exp-03 test driver: serves the published AppBundle, runs it in headless Chromium/Edge
// via puppeteer-core, screenshots the triangle, checks pixels, collects console + benchmarks.
//
// Usage (from test/):
//   node run.mjs [--browser edge|cft|shell] [--flags "--enable-unsafe-webgpu ..."] [--root <wwwroot>]
//                [--out <dir>] [--query nojiterp=1] [--timeout ms]
// Outputs: out/console.log, out/triangle.png, out/triangle-canvas.png, out/many-draws.png, out/results.json
import puppeteer from 'puppeteer-core';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { startServer } from './serve.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const home = os.homedir();
const BROWSERS = {
  edge: '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge',
  cft: `${home}/Library/Caches/ms-playwright/chromium-1234/chrome-mac-arm64/Google Chrome for Testing.app/Contents/MacOS/Google Chrome for Testing`,
  shell: `${home}/Library/Caches/ms-playwright/chromium_headless_shell-1234/chrome-headless-shell-mac-arm64/chrome-headless-shell`,
};

const argv = process.argv.slice(2);
const opt = (name, def) => { const i = argv.indexOf(`--${name}`); return i >= 0 ? argv[i + 1] : def; };
const browserName = opt('browser', 'edge');
const extraFlags = (opt('flags', '') || '').split(/\s+/).filter(Boolean);
const root = path.resolve(opt('root', path.join(here, '../WebGpuTriangle/bin/Release/net10.0/publish/wwwroot')));
const outDir = path.resolve(opt('out', path.join(here, 'out')));
const timeoutMs = Number(opt('timeout', 300000));
fs.mkdirSync(outDir, { recursive: true });

const lines = [];
const t0 = Date.now();
const record = (kind, text) => {
  const line = `[+${((Date.now() - t0) / 1000).toFixed(3)}s] [${kind}] ${text}`;
  lines.push(line);
  console.log(line);
};

const waiters = [];
function waitFor(marker, ms = timeoutMs) {
  const hit = lines.find((l) => l.includes(marker));
  if (hit) return Promise.resolve(hit);
  return new Promise((resolve, reject) => {
    const w = { marker, resolve };
    waiters.push(w);
    setTimeout(() => reject(new Error(`timeout waiting for "${marker}"`)), ms);
  });
}

const server = await startServer(root, 0);
const query = opt('query', '');
const url = `http://localhost:${server.address().port}/index.html?gate=1${query ? '&' + query : ''}`;
const userDataDir = fs.mkdtempSync(path.join(process.env.E3_SCRATCH ?? os.tmpdir(), `e3-ud-${browserName}-`));
const args = ['--no-first-run', '--no-default-browser-check', ...extraFlags];
record('driver', `browser=${browserName} exe=${BROWSERS[browserName]} headless=new args=${JSON.stringify(args)} url=${url} root=${root}`);

const browser = await puppeteer.launch({
  executablePath: BROWSERS[browserName],
  headless: browserName === 'shell' ? 'shell' : true, // true => new headless (--headless=new semantics)
  args, userDataDir, protocolTimeout: timeoutMs,
});
record('driver', `browser.version()=${await browser.version()}`);
let exitCode = 1;
try {
  const page = await browser.newPage();
  await page.setViewport({ width: 800, height: 720, deviceScaleFactor: 1 });
  page.on('console', (m) => {
    const text = m.text();
    record(m.type(), text);
    for (const w of [...waiters]) if (text.includes(w.marker)) { w.resolve(text); waiters.splice(waiters.indexOf(w), 1); }
  });
  page.on('pageerror', (e) => record('pageerror', e.message));
  page.on('requestfailed', (r) => record('requestfailed', `${r.url()} ${r.failure()?.errorText}`));
  page.on('response', (r) => { if (r.status() >= 400) record('http', `${r.status()} ${r.url()}`); });

  await page.goto(url, { waitUntil: 'load' });
  const fatal = waitFor('FATAL').then((l) => { throw new Error(l); });
  fatal.catch(() => { });

  await Promise.race([waitFor('FRAMES DONE'), fatal]);
  const pixelLine = await Promise.race([waitFor('PIXEL CHECK'), fatal]);
  await Promise.race([waitFor('GATE WAITING'), fatal]);

  // screenshot (full page + canvas element) while the page is gated
  await page.screenshot({ path: path.join(outDir, 'triangle.png') });
  const canvasEl = await page.$('#gpu');
  const b64 = await canvasEl.screenshot({ encoding: 'base64' });
  fs.writeFileSync(path.join(outDir, 'triangle-canvas.png'), Buffer.from(b64, 'base64'));
  const shot = await page.evaluate(async (b64) => {
    const img = new Image();
    img.src = 'data:image/png;base64,' + b64;
    await img.decode();
    const c = new OffscreenCanvas(img.width, img.height);
    const g = c.getContext('2d');
    g.drawImage(img, 0, 0);
    const px = (x, y) => [...g.getImageData(x, y, 1, 1).data];
    return { w: img.width, h: img.height, center: px(img.width >> 1, img.height >> 1), corner: px(4, 4) };
  }, b64);
  const near = (p, e, tol) => p.slice(0, 3).every((v, i) => Math.abs(v - e[i]) <= tol);
  const shotPass = near(shot.center, [85, 85, 85], 12) && near(shot.corner, [26, 51, 77], 4);
  record('driver', `SCREENSHOT PIXEL CHECK ${shotPass ? 'PASS' : 'FAIL'} ${JSON.stringify(shot)}`);
  await page.evaluate(() => window.__releaseGate());

  await Promise.race([waitFor('ALL DONE'), fatal]);
  await page.screenshot({ path: path.join(outDir, 'many-draws.png') });
  const results = await page.evaluate(() => window.__e3);
  results.driver = { browser: browserName, browserVersion: await browser.version(), args, screenshotPixelCheck: { pass: shotPass, ...shot } };
  fs.writeFileSync(path.join(outDir, 'results.json'), JSON.stringify(results, null, 2));
  const inPagePass = pixelLine.includes('PIXEL CHECK PASS');
  record('driver', `RESULT in-page PIXEL CHECK ${inPagePass ? 'PASS' : 'FAIL'}; screenshot check ${shotPass ? 'PASS' : 'FAIL'}`);
  exitCode = inPagePass && shotPass ? 0 : 2;
} catch (e) {
  record('driver', `ERROR ${e.stack ?? e}`);
} finally {
  fs.writeFileSync(path.join(outDir, 'console.log'), lines.join('\n') + '\n');
  await browser.close().catch(() => { });
  server.close();
  process.exit(exitCode);
}
