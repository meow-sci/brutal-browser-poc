// Browser-side validation: compile every WGSL module of a manifest with the real WebGPU implementation (Tint) in headless Chromium.
//
//   node check-wgsl.mjs <manifest.json> [--channel chrome|msedge] [--executable <chromium binary>]
//
// Launches Playwright's Chromium (`chromium.launch({ headless: true })`, no flags), requests a WebGPU device, then for every shader calls
// device.createShaderModule({ code }) + getCompilationInfo(). Errors fail the run (exit 1), warnings are printed.
// Needs `npm ci` in tools/ (Playwright) and a Chromium with WebGPU; nothing is ever downloaded.
//
// Browser selection (first one that launches AND yields a WebGPU adapter wins; --channel / --executable pin a single candidate):
//   1. Playwright's own Chromium for this Playwright version (what `chromium.launch({ headless: true })` starts)
//   2. the newest full Chromium and then the newest headless shell in Playwright's browser cache (a different revision than
//      Playwright expects is fine for a shader check; note the headless shell often has no GPU adapter on macOS)
//   3. an installed Google Chrome, then Microsoft Edge
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { chromium } from 'playwright';

const args = process.argv.slice(2);
const option = (name) => { const i = args.indexOf(name); return i >= 0 ? args.splice(i, 2)[1] : undefined; };
const channel = option('--channel'), executable = option('--executable');
const [manifestPath] = args;
if (!manifestPath) { console.error('usage: node check-wgsl.mjs <manifest.json> [--channel chrome|msedge] [--executable <chromium binary>]'); process.exit(2); }

const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
const shaders = Object.entries(manifest.shaders ?? {}).map(([sha, s]) => ({ sha, name: s.name, wgsl: s.wgsl }));

// ---- choosing a browser ------------------------------------------------------------------------------------------
/** Newest cached browser of `prefix` (chromium | chromium_headless_shell) whose binary is found under one of `relPaths`. */
function cachedBrowser(prefix, relPaths) {
  const root = process.env.PLAYWRIGHT_BROWSERS_PATH
    ?? (process.platform === 'darwin' ? join(homedir(), 'Library', 'Caches', 'ms-playwright')
      : process.platform === 'win32' ? join(process.env.LOCALAPPDATA ?? homedir(), 'ms-playwright') : join(homedir(), '.cache', 'ms-playwright'));
  if (!existsSync(root)) return undefined;
  const revs = readdirSync(root).filter((d) => d.startsWith(`${prefix}-`)).sort((a, b) => parseInt(b.split('-')[1]) - parseInt(a.split('-')[1]));
  for (const rev of revs) for (const rel of relPaths) { const p = join(root, rev, rel); if (existsSync(p)) return p; }
  return undefined;
}
const FULL = ['chrome-mac-arm64/Google Chrome for Testing.app/Contents/MacOS/Google Chrome for Testing', 'chrome-mac-x64/Google Chrome for Testing.app/Contents/MacOS/Google Chrome for Testing',
  'chrome-linux64/chrome', 'chrome-linux/chrome', 'chrome-win64/chrome.exe', 'chrome-win/chrome.exe'];
const SHELL = ['chrome-headless-shell-mac-arm64/chrome-headless-shell', 'chrome-headless-shell-mac-x64/chrome-headless-shell',
  'chrome-headless-shell-linux64/chrome-headless-shell', 'chrome-headless-shell-win64/chrome-headless-shell.exe'];

function candidates() {
  if (executable) return [{ label: executable, opts: { executablePath: executable } }];
  if (channel) return [{ label: `channel ${channel}`, opts: { channel } }];
  const list = [{ label: 'Playwright default Chromium', opts: {} }];
  const full = cachedBrowser('chromium', FULL), shell = cachedBrowser('chromium_headless_shell', SHELL);
  if (full) list.push({ label: `cached Chromium ${full}`, opts: { executablePath: full } });
  if (shell) list.push({ label: `cached headless shell ${shell}`, opts: { executablePath: shell } });
  list.push({ label: 'channel chrome', opts: { channel: 'chrome' } }, { label: 'channel msedge', opts: { channel: 'msedge' } });
  return list;
}

// ---- the page-side check -----------------------------------------------------------------------------------------
/** Runs inside the page: { adapter, requiredFeatures, results: [{ name, messages: [{ type, text, line, col }] }] }. */
async function inPage(shaders) {
  if (!navigator.gpu) throw new Error('navigator.gpu is undefined (WebGPU unavailable)');
  const adapter = await navigator.gpu.requestAdapter();
  if (!adapter) throw new Error('navigator.gpu.requestAdapter() returned null (no WebGPU adapter)');
  // optional features some translated modules `enable` (clip distances, subgroups, f16)
  const requiredFeatures = ['clip-distances', 'subgroups', 'shader-f16'].filter((f) => adapter.features.has(f));
  const device = await adapter.requestDevice({ requiredFeatures });
  const timeout = (p, what) => Promise.race([p, new Promise((_, rej) => setTimeout(() => rej(new Error(`timeout waiting for ${what}`)), 15000))]);
  const results = [];
  for (const s of shaders) {
    const module = device.createShaderModule({ label: s.name, code: s.wgsl });
    const info = await timeout(module.getCompilationInfo(), `getCompilationInfo(${s.name})`);
    results.push({ name: s.name, messages: info.messages.map((m) => ({ type: m.type, text: m.message, line: m.lineNum, col: m.linePos })) });
  }
  const i = adapter.info ?? {};
  return { adapter: [i.vendor, i.architecture, i.description].filter(Boolean).join(' / ') || 'unknown', requiredFeatures, results };
}

/** Launch one candidate and run the check; throws if it cannot launch or has no WebGPU adapter. */
async function tryCandidate(c) {
  const browser = await chromium.launch({ headless: true, ...c.opts });
  try {
    const page = await browser.newPage();
    // WebGPU needs a secure context and about:blank is not one; http://localhost is. Serve a stub page for it (no network involved).
    await page.route('http://localhost/**', (route) => route.fulfill({ contentType: 'text/html', body: '<!doctype html><title>check-wgsl</title>' }));
    await page.goto('http://localhost/');
    return { version: browser.version(), out: await page.evaluate(inPage, shaders) };
  } finally { await browser.close(); }
}

// ---- main --------------------------------------------------------------------------------------------------------
let result;
const why = [];
for (const c of candidates()) {
  try { result = { ...(await tryCandidate(c)), label: c.label }; break; } catch (e) { why.push(`  - ${c.label}: ${e.message.split('\n')[0]}`); }
}
if (!result) {
  console.error(`browser-check: no usable WebGPU browser found:\n${why.join('\n')}\nbrowser-check: use --no-browser-check to skip this validation`);
  process.exit(1);
}
console.log(`browser-check: ${result.label} (Chromium ${result.version}), adapter ${result.out.adapter}; device features: ${result.out.requiredFeatures.join(', ') || 'none'}`);
if (why.length) console.log(`browser-check: skipped candidates:\n${why.join('\n')}`);
let failed = 0;
for (const r of result.out.results) {
  const errors = r.messages.filter((m) => m.type === 'error');
  console.log(`${errors.length ? 'FAIL' : 'PASS'} ${r.name} (${errors.length} error(s), ${r.messages.length - errors.length} warning/info)`);
  for (const m of r.messages) console.log(`  ${m.type} ${m.line}:${m.col} ${m.text.split('\n')[0]}`);
  if (errors.length) failed++;
}
if (failed) { console.error(`browser-check: FAILED (${failed} of ${result.out.results.length} shader(s))`); process.exit(1); }
console.log(`browser-check: all ${result.out.results.length} shader(s) compile`);
