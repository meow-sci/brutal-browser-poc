// Headless Chromium + WebGPU smoke test using puppeteer-core and an already-installed browser.
//   node test/headless.mjs --url http://localhost:5173/ --browser cft --preset unsafe [--expect-dotnet]
//   node test/headless.mjs --serve [--serve-config vite.bundled.config.ts] ...   (starts a Vite dev server itself)
// Exit code: 0 = all expectations met, 1 = failure, 2 = usage/launch error.
import { parseArgs } from 'node:util';
import fs from 'node:fs';
import path from 'node:path';
import puppeteer from 'puppeteer-core';

const PW = `${process.env.HOME}/Library/Caches/ms-playwright`;
const BROWSERS = {
  edge: { exe: '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge', headless: true },
  cft: { exe: `${PW}/chromium-1234/chrome-mac-arm64/Google Chrome for Testing.app/Contents/MacOS/Google Chrome for Testing`, headless: true },
  shell: { exe: `${PW}/chromium_headless_shell-1234/chrome-headless-shell-mac-arm64/chrome-headless-shell`, headless: 'shell' },
  chrome: { exe: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', headless: true },
};
const PRESETS = {
  none: [],
  unsafe: ['--enable-unsafe-webgpu'],
  'unsafe+vulkan': ['--enable-unsafe-webgpu', '--enable-features=Vulkan'],
  'unsafe+metal': ['--enable-unsafe-webgpu', '--use-angle=metal'],
  'unsafe+swiftshader': ['--enable-unsafe-webgpu', '--use-webgpu-adapter=swiftshader'],
  'unsafe+swiftshader-angle': ['--enable-unsafe-webgpu', '--use-angle=swiftshader', '--use-webgpu-adapter=swiftshader'],
  'unsafe+ignore-blocklist': ['--enable-unsafe-webgpu', '--ignore-gpu-blocklist'],
  'unsafe+devfeatures': ['--enable-unsafe-webgpu', '--enable-webgpu-developer-features'],
};

const { values: a } = parseArgs({
  options: {
    url: { type: 'string', default: 'http://localhost:5173/' },
    browser: { type: 'string', default: 'cft' },
    preset: { type: 'string', default: 'unsafe' },
    flags: { type: 'string', default: '' }, // extra, space separated
    'expect-dotnet': { type: 'boolean', default: false },
    'allow-no-gpu': { type: 'boolean', default: false },
    timeout: { type: 'string', default: '60000' },
    screenshot: { type: 'string', default: '' },
    quiet: { type: 'boolean', default: false },
    serve: { type: 'boolean', default: false }, // start `vite` (dev server) programmatically and test against it
    'serve-config': { type: 'string', default: '' },
    reload: { type: 'boolean', default: false }, // with --net: reload once and print the second-load requests (HTTP cache behaviour)
    net: { type: 'boolean', default: false }, // print per-request Accept-Encoding / Content-Encoding / Content-Length
    summary: { type: 'boolean', default: false }, // one line per run (for matrix loops)
  },
});

let server;
if (a.serve) {
  const { createServer } = await import('vite');
  server = await createServer({ configFile: a['serve-config'] || undefined, logLevel: 'warn', server: { port: 5199, strictPort: false } });
  await server.listen();
  a.url = server.resolvedUrls.local[0];
  if (!a.quiet) console.log(`vite dev server: ${a.url}`);
}
const b = BROWSERS[a.browser] ?? { exe: a.browser, headless: true };
if (!fs.existsSync(b.exe)) { console.error(`browser not found: ${b.exe}`); process.exit(2); }
const args = [...(PRESETS[a.preset] ?? (console.error(`unknown preset ${a.preset}`), process.exit(2))), ...a.flags.split(' ').filter(Boolean)];

const browser = await puppeteer.launch({ executablePath: b.exe, headless: b.headless, args, protocolTimeout: 120000 });
let code = 0;
try {
  const version = await browser.version();
  const page = await browser.newPage();
  const problems = [];
  page.on('console', (m) => { if (!a.quiet) console.log(`  [console.${m.type()}] ${m.text().split('\n')[0].slice(0, 200)}`); });
  page.on('pageerror', (e) => problems.push(`pageerror: ${e.message}`));
  page.on('requestfailed', (r) => problems.push(`requestfailed: ${r.url()} ${r.failure()?.errorText}`));
  const net = [];
  page.on('response', (r) => { if (a.net) net.push(`${r.status()} req-cc=${r.request().headers()["cache-control"] ?? "-"} ae=${r.request().headers()['accept-encoding'] ?? '-'} ce=${r.headers()['content-encoding'] ?? '-'} len=${r.headers()['content-length'] ?? '-'} type=${r.headers()['content-type'] ?? '-'} ${r.url().replace(/^https?:\/\/[^/]+/, '')}`); });
  page.on('response', (r) => { if (r.status() >= 400 && !r.url().endsWith('/favicon.ico')) problems.push(`HTTP ${r.status()}: ${r.url()}`); });

  await page.goto(a.url, { waitUntil: 'load', timeout: Number(a.timeout) });
  await page.waitForFunction('window.__e2e && window.__e2e.done', { timeout: Number(a.timeout) });
  const e2e = await page.evaluate(() => window.__e2e);
  // Screenshot-based assertion: decode a screenshot of the canvas rect in-page, read the centre pixel.
  const el = await page.$('#gpu');
  const shot = await el.screenshot({ encoding: 'base64' });
  e2e.webgpu.screenshotPixel = await page.evaluate(async (b64) => {
    const img = new Image(); img.src = 'data:image/png;base64,' + b64; await img.decode();
    const c = document.createElement('canvas'); c.width = img.width; c.height = img.height;
    const g = c.getContext('2d', { willReadFrequently: true }); g.drawImage(img, 0, 0);
    return Array.from(g.getImageData(img.width >> 1, img.height >> 1, 1, 1).data);
  }, shot);
  if (a.screenshot) { fs.mkdirSync(path.dirname(a.screenshot), { recursive: true }); fs.writeFileSync(a.screenshot, Buffer.from(shot, 'base64')); }
  if (a.net && a.reload) {
    console.log(net.join('\n') + '\n--- reload ---');
    net.length = 0;
    await page.reload({ waitUntil: 'load' });
    await page.waitForFunction('window.__e2e && window.__e2e.done', { timeout: Number(a.timeout) });
  }
  if (a.net) console.log(net.join('\n'));
  const ua = await page.evaluate(() => navigator.userAgent);
  const gpu = e2e.webgpu;
  if (a.summary) console.log(`SUMMARY ${a.browser} ${version} preset=${a.preset} adapter=${gpu.adapter} info=${gpu.info ? [gpu.info.vendor, gpu.info.architecture, gpu.info.description].join('/') : '-'} fallback=${gpu.isFallbackAdapter} pixelOk=${gpu.pixelOk} shot=${JSON.stringify(gpu.screenshotPixel)} dataUrlSame=${JSON.stringify(gpu.toDataURLSameTask)} dataUrlNext=${JSON.stringify(gpu.toDataURLNextFrame)} drawImage=${JSON.stringify(gpu.drawImageSameTask)} dotnetTick=${e2e.dotnet.tickReturn} coi=${e2e.page.crossOriginIsolated}`);
  else console.log(JSON.stringify({ browser: a.browser, version, preset: a.preset, args, ua, page: e2e.page, webgpu: gpu, dotnet: e2e.dotnet, errors: e2e.errors, problems }, null, 1));

  if (!gpu.hasNavigatorGpu) problems.push('navigator.gpu missing');
  else if (!gpu.adapter && !a['allow-no-gpu']) problems.push('requestAdapter() returned null');
  else if (gpu.adapter && !gpu.pixelOk) problems.push(`pixel readback mismatch: ${JSON.stringify(gpu.pixel)}`);
  if (gpu.adapter && gpu.pixelOk) {
    const [r, g, bl] = gpu.screenshotPixel ?? [];
    if (!(Math.abs(r - 255) <= 2 && Math.abs(g - 128) <= 3 && bl <= 2)) problems.push(`screenshot of the WebGPU canvas is not orange: ${JSON.stringify(gpu.screenshotPixel)} (compositing problem)`);
  }
  if (a['expect-dotnet'] && e2e.dotnet.tickReturn !== 42) problems.push(`dotnet Tick did not return 42: ${JSON.stringify(e2e.dotnet)}`);
  if (e2e.errors.length) problems.push(...e2e.errors);
  if (problems.length) { console.error('FAIL:\n - ' + problems.join('\n - ')); code = 1; } else console.log('OK');
} catch (e) {
  console.error('FAIL (exception):', e.message); code = 1;
} finally {
  await browser.close();
  await server?.close();
}
process.exit(code);
