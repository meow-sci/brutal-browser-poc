// Drives the published WasmSmoke app in headless Chromium via puppeteer-core and records the console.
//
// Because some tests trigger fatal WebAssembly traps (which kill the .NET runtime, not catchable as exceptions),
// the driver runs in passes:
//   pass 1..k : run all tests, skipping ids that already crashed; a crash is attributed to the last "BEGIN <id>" line
//   isolation : re-run every crashed id alone (?tests=<id>) so its own output/trap is captured cleanly
//
// Usage: node run-smoke.mjs [--dir <wwwroot>] [--browser <exe>] [--out <console.log>] [--timeout <ms>]
//                           [--query "<qs>"] (single pass with that query) [--extra-arg <chrome flag>]...
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import puppeteer from 'puppeteer-core';
import { startServer } from './serve.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const argv = process.argv.slice(2);
const opt = (name, def) => { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : def; };
const extraArgs = argv.flatMap((a, i) => (a === '--extra-arg' ? [argv[i + 1]] : []));

const CHROME_FOR_TESTING = '/Users/asherwin/Library/Caches/ms-playwright/chromium-1234/chrome-mac-arm64/Google Chrome for Testing.app/Contents/MacOS/Google Chrome for Testing';
const dir = path.resolve(here, opt('--dir', '../WasmSmoke/bin/Release/net10.0/publish/wwwroot'));
const browserPath = opt('--browser', process.env.CHROME_PATH || CHROME_FOR_TESTING);
const outFile = path.resolve(here, opt('--out', 'console.log'));
const timeoutMs = Number(opt('--timeout', 120000));
const singleQuery = opt('--query', null);
const probeSigs = argv.includes('--probe-sigs'); // run P01..P45 each in isolation

const lines = [];
const t0 = Date.now();
const rec = (s) => { const line = `[+${String(Date.now() - t0).padStart(6)}ms] ${s}`; lines.push(line); console.log(line); };

const server = await startServer(dir, 0, { log: (m) => rec(`[server] ${m}`) });
const base = `http://127.0.0.1:${server.address().port}/index.html`;
const browser = await puppeteer.launch({
  executablePath: browserPath,
  headless: true,
  args: ['--headless=new', '--no-first-run', '--no-default-browser-check', ...extraArgs],
});
rec(`[driver] browser=${await browser.version()} executable=${browserPath}`);
rec(`[driver] extraArgs=${JSON.stringify(extraArgs)} serving ${dir}`);

async function runPass(label, query) {
  const url = base + (query ? `?${query}` : '');
  rec(`[driver] ===== ${label}: ${url}`);
  const page = await browser.newPage();
  let lastBegin = null, crashed = false, done;
  const finished = new Promise((r) => (done = r));
  page.on('console', (msg) => {
    const text = msg.text();
    rec(`[console.${msg.type()}] ${text}`);
    const m = /^BEGIN (\S+)/.exec(text); if (m) lastBegin = m[1];
    if (text.includes('TICKS DONE')) done('TICKS DONE');
  });
  page.on('pageerror', (err) => {
    rec(`[pageerror] ${err && err.stack ? err.stack : err}`);
    if (!crashed) { crashed = true; setTimeout(() => done('CRASH'), 2000); }
  });
  page.on('requestfailed', (req) => rec(`[requestfailed] ${req.url()} ${req.failure() && req.failure().errorText}`));
  page.on('response', (res) => { if (res.status() >= 400 && !res.url().endsWith('/favicon.ico')) rec(`[http ${res.status()}] ${res.url()}`); });
  await page.goto(url, { waitUntil: 'load' });
  const result = await Promise.race([finished, new Promise((r) => setTimeout(() => r('TIMEOUT'), timeoutMs))]);
  rec(`[driver] ${label} finished: ${result}${result === 'CRASH' ? ` (attributed to ${lastBegin})` : ''}`);
  await page.close();
  return { result, lastBegin };
}

let exitCode = 0;
if (probeSigs) {
  const summary = [];
  for (let i = 1; i <= 45; i++) {
    const id = 'P' + String(i).padStart(2, '0');
    const r = await runPass(`probe ${id}`, `tests=${id}`);
    summary.push(`${id}=${r.result === 'TICKS DONE' ? 'ran' : r.result}`);
  }
  rec(`[driver] probe summary: ${summary.join(' ')}`);
} else if (singleQuery !== null) {
  const r = await runPass('single', singleQuery);
  exitCode = r.result === 'TICKS DONE' ? 0 : 1;
} else {
  const crashedIds = [];
  for (let pass = 1; pass <= 12; pass++) {
    const q = crashedIds.length ? `skip=${crashedIds.join(',')}` : '';
    const r = await runPass(`pass ${pass}`, q);
    if (r.result === 'CRASH' && r.lastBegin && !crashedIds.includes(r.lastBegin)) { crashedIds.push(r.lastBegin); continue; }
    if (r.result !== 'TICKS DONE') exitCode = 1;
    break;
  }
  rec(`[driver] crashed test ids: ${JSON.stringify(crashedIds)}`);
  for (const id of crashedIds) await runPass(`isolation ${id}`, `tests=${id}`);
}

await browser.close();
server.close();
fs.writeFileSync(outFile, lines.join('\n') + '\n');
console.log(`\nwrote ${lines.length} lines to ${outFile}`);
process.exit(exitCode);
