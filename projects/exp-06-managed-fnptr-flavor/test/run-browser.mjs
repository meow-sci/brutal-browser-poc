// exp-06: runs the published BrowserFlavorSmoke app in headless Chromium and records the console.
// Reuses puppeteer-core from exp-01's test/node_modules (no install here); adapted from exp-04's run-browser.mjs.
// Usage: node run-browser.mjs [--query "bench=0&callnull=1"] [--out <file>] [--browser <exe>] [--dir <wwwroot>] [--timeout ms]
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const require = createRequire(path.resolve(here, '../../exp-01-dotnet-wasm-smoke/test/package.json'));
const puppeteer = require('puppeteer-core');

const argv = process.argv.slice(2);
const opt = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };
const query = opt('--query', '');
const dir = path.resolve(here, opt('--dir', '../apps/BrowserFlavorSmoke/bin/Release/net10.0/publish/wwwroot'));
const out = path.resolve(here, opt('--out', `../out/browser-run${query ? '-' + query.replace(/[^a-z0-9]+/gi, '_') : ''}.txt`));
const exe = opt('--browser', '/Users/asherwin/Library/Caches/ms-playwright/chromium-1234/chrome-mac-arm64/Google Chrome for Testing.app/Contents/MacOS/Google Chrome for Testing');
const timeoutMs = Number(opt('--timeout', '180000'));

const MIME = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.mjs': 'text/javascript; charset=utf-8',
  '.json': 'application/json', '.wasm': 'application/wasm', '.dat': 'application/octet-stream', '.pdb': 'application/octet-stream', '.map': 'application/json' };
const server = http.createServer((req, res) => {
  let file = path.join(dir, decodeURIComponent(new URL(req.url, 'http://x').pathname));
  if (!file.startsWith(dir)) return res.writeHead(403).end();
  if (fs.existsSync(file) && fs.statSync(file).isDirectory()) file = path.join(file, 'index.html');
  fs.readFile(file, (err, data) => err ? res.writeHead(404).end() : res.writeHead(200, { 'content-type': MIME[path.extname(file)] || 'application/octet-stream', 'cache-control': 'no-store' }).end(data));
});
await new Promise((r) => server.listen(0, '127.0.0.1', r));

const lines = [];
const t0 = Date.now();
const rec = (s) => { lines.push(`[+${String(Date.now() - t0).padStart(6)}ms] ${s}`); console.log(lines.at(-1)); };
const browser = await puppeteer.launch({ executablePath: exe, headless: true, args: ['--headless=new', '--no-first-run'] });
rec(`[driver] browser=${await browser.version()} query=${query || '(none)'} dir=${dir}`);
const page = await browser.newPage();
let done;
const finished = new Promise((r) => (done = r));
page.on('console', (m) => { rec(`[console.${m.type()}] ${m.text()}`); if (m.text().startsWith('EXP06 DONE')) setTimeout(() => done('DONE'), 300); });
page.on('response', (r) => { if (r.status() >= 400) rec(`[http ${r.status()}] ${r.url()}`); });
page.on('pageerror', (e) => { rec(`[pageerror] ${e && e.stack ? e.stack : e}`); setTimeout(() => done('CRASH'), 1000); });
await page.goto(`http://127.0.0.1:${server.address().port}/index.html${query ? '?' + query : ''}`, { waitUntil: 'load' });
let timer;
const result = await Promise.race([finished, new Promise((r) => { timer = setTimeout(() => r('TIMEOUT'), timeoutMs); })]);
clearTimeout(timer); // an uncleared timer would keep node alive for the full timeout after the page is done
rec(`[driver] finished: ${result}`);
await browser.close();
server.close();
fs.writeFileSync(out, lines.join('\n') + '\n');
console.log(`wrote ${out}`);
process.exit(result === 'DONE' ? 0 : 1);
