// Usage: node src/bench-all.mjs <results.tsv> <dir-with-spv>  : translate every module marked OK once (cold process, sequential) and report totals.
import { readFileSync, statSync } from 'node:fs'; import { join } from 'node:path';
import { parseSpirv, validate, writeWgsl } from 'naga-wasm';
const [tsv, dir] = process.argv.slice(2);
const keys = readFileSync(tsv, 'utf8').split('\n').filter((l) => l.split('\t')[1] === 'OK').map((l) => l.split('\t')[0].replace(/\//g, '_'));
let totalMs = 0, spvBytes = 0, wgslBytes = 0, worst = { ms: 0, k: '' }; const t0 = performance.now();
for (const k of keys) {
  const b = new Uint8Array(readFileSync(join(dir, k + '.spv'))); spvBytes += b.length;
  const s = performance.now(); using m = parseSpirv(b); using i = validate(m); const w = writeWgsl(m, i); const ms = performance.now() - s;
  totalMs += ms; wgslBytes += w.length; if (ms > worst.ms) worst = { ms, k };
}
console.log(`modules=${keys.length} total=${totalMs.toFixed(0)} ms (mean ${(totalMs / keys.length).toFixed(1)} ms) spv=${(spvBytes / 1024).toFixed(0)} KB wgsl=${(wgslBytes / 1024).toFixed(0)} KB worst=${worst.ms.toFixed(1)} ms (${worst.k})`);
