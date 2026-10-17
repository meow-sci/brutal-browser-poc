// WebGPU-in-headless flag matrix probe.
// Usage: node probe/probe.mjs   (from test/)
// For each browser x flag set: launch headless, load probe/index.html from a
// localhost server (secure context), report navigator.gpu.requestAdapter() result + adapter info.
import puppeteer from 'puppeteer-core';
import os from 'node:os';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { startServer } from '../serve.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const home = os.homedir();
const BROWSERS = {
  edge: '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge',
  cft: `${home}/Library/Caches/ms-playwright/chromium-1234/chrome-mac-arm64/Google Chrome for Testing.app/Contents/MacOS/Google Chrome for Testing`,
  shell: `${home}/Library/Caches/ms-playwright/chromium_headless_shell-1234/chrome-headless-shell-mac-arm64/chrome-headless-shell`,
};
const FLAGSETS = {
  '1-none': [],
  '2-unsafe-webgpu': ['--enable-unsafe-webgpu'],
  '3-angle-metal': ['--enable-unsafe-webgpu', '--use-angle=metal'],
  '4-swiftshader': ['--enable-unsafe-webgpu', '--use-webgpu-adapter=swiftshader', '--enable-unsafe-swiftshader'],
};
const only = process.argv[2];
const scratch = process.env.E3_SCRATCH ?? fs.mkdtempSync(path.join(os.tmpdir(), 'e3-probe-'));
const server = await startServer(here, 0);
const url = `http://localhost:${server.address().port}/index.html`;
const results = [];
for (const [bname, exe] of Object.entries(BROWSERS)) {
  if (only && !bname.startsWith(only)) continue;
  for (const [fname, flags] of Object.entries(FLAGSETS)) {
    const userDataDir = fs.mkdtempSync(path.join(scratch, `ud-${bname}-`));
    const args = [...flags, '--no-first-run', '--no-default-browser-check'];
    let res;
    let browser;
    try {
      browser = await puppeteer.launch({
        executablePath: exe, headless: bname === 'shell' ? 'shell' : true, args, userDataDir,
        protocolTimeout: 60000,
      });
      const page = await browser.newPage();
      await page.goto(url, { waitUntil: 'load' });
      res = await page.evaluate(() => window.probe);
      res.browserVersion = await browser.version();
    } catch (e) {
      res = { launchError: String(e).split('\n')[0] };
    } finally {
      try { await browser?.close(); } catch { }
    }
    const row = { browser: bname, flags: fname, ...res };
    delete row.ua;
    results.push(row);
    console.log(JSON.stringify(row));
  }
}
server.close();
fs.writeFileSync(path.join(here, '../out/probe-results.json'), JSON.stringify(results, null, 2));
