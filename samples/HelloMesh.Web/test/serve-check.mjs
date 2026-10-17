// Browser-free check of the dotnet-wwwroot plugin: every file the .NET runtime and main.ts will request must exist and
// have a sane MIME type (no SPA-fallback HTML, .wasm = application/wasm).
//   node test/serve-check.mjs [--preview] [--url http://localhost:5173/] [--modules a.js,b.js]
// Reads the boot config embedded in _framework/dotnet.js (assemblies, wasm, js modules, icu, pdb ...) and GETs each entry.
import { parseArgs } from 'node:util';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createServer, preview } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const { values: a } = parseArgs({ options: { preview: { type: 'boolean', default: false }, url: { type: 'string' }, modules: { type: 'string' } } });

let server;
const url = a.url ?? (server = a.preview ? await preview({ root, logLevel: 'warn' }) : await (await createServer({ root, logLevel: 'warn' })).listen()).resolvedUrls.local[0];
const abs = (p) => new URL(p, url);
const bad = [];
let n = 0;
// Static hosts (GitHub Pages) send application/javascript where Vite sends text/javascript; both are JavaScript MIME types for module scripts.
const isType = (type, expected) => type.startsWith(expected) || (expected === 'text/javascript' && type.startsWith('application/javascript'));
async function get(route, expectType) {
  const res = await fetch(route instanceof URL ? route : abs(route), { headers: { 'accept-encoding': 'identity' } });
  const type = res.headers.get('content-type') ?? '';
  const body = Buffer.from(await res.arrayBuffer());
  n++;
  if (!res.ok || type.startsWith('text/html') || (expectType && !isType(type, expectType)) || !body.length) bad.push(`${res.status} ${type || '-'} ${route}`);
  return body;
}

try {
  const entry = abs('_framework/dotnet.js');
  const js = (await get(entry, 'text/javascript')).toString('utf8');
  const cfg = /\/\*json-start\*\/([\s\S]*?)\/\*json-end\*\//.exec(js);
  if (!cfg) bad.push('no inline boot config in _framework/dotnet.js (WasmInlineBootConfig=false? fetch dotnet.boot.js instead)');
  else {
    const config = JSON.parse(cfg[1]);
    console.log(`mainAssemblyName ${config.mainAssemblyName}`);
    for (const [kind, group] of Object.entries(config.resources ?? {})) {
      if (typeof group !== 'object') continue; // 'hash'
      for (const e of Array.isArray(group) ? group : Object.entries(group).map(([name]) => ({ name }))) {
        await get(new URL(e.name, entry), e.name.endsWith('.wasm') ? 'application/wasm' : undefined);
      }
      console.log(`  ${kind}: ${Array.isArray(group) ? group.length : Object.keys(group).length} files`);
    }
  }
  const modules = a.modules ? a.modules.split(',') : ['webgpu-glue.js', 'brutal-browser.js', 'brutal-browser-audio.js'];
  for (const m of modules) await get(m, 'text/javascript');
  console.log(`  glue modules: ${modules.join(', ')}`);
} finally {
  await server?.close?.();
}
console.log(bad.length ? `FAIL: ${bad.length} problem(s) in ${n} requests:\n  ${bad.join('\n  ')}` : `OK: ${n} requests, all 200 with plausible content types (${url})`);
process.exit(bad.length ? 1 : 0);
