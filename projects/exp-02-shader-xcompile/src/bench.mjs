// Usage: node src/bench.mjs [iterations] file1.spv file2.spv ...
// Measures (a) cold module load+compile of naga_bg.wasm in Node, (b) per-shader SPIR-V->WGSL latency (cold first call + warm stats).
import { readFileSync, statSync } from 'node:fs';
const iters = Number(process.argv[2] ?? 200);
const files = process.argv.slice(3);
const wasmPath = new URL('../node_modules/naga-wasm/dist/wasm/naga_bg.wasm', import.meta.url);
console.log(`naga_bg.wasm raw bytes: ${statSync(wasmPath).size}`);
const t0 = performance.now();
const { parseSpirv, validate, writeWgsl } = await import('naga-wasm'); // node entry: readFileSync + sync WebAssembly.Module compile + instantiate
const t1 = performance.now();
console.log(`cold import + wasm compile/instantiate: ${(t1 - t0).toFixed(1)} ms`);
const pct = (a, p) => a[Math.min(a.length - 1, Math.floor(a.length * p))];
for (const f of files) {
  const bytes = new Uint8Array(readFileSync(f));
  const one = () => { using m = parseSpirv(bytes); using i = validate(m); return writeWgsl(m, i); };
  const c0 = performance.now(); let out;
  try { out = one(); } catch (e) { console.log(`${f}: FAIL (${e.message})`); continue; }
  const cold = performance.now() - c0;
  for (let i = 0; i < 20; i++) one(); // warm-up
  const ts = [];
  for (let i = 0; i < iters; i++) { const s = performance.now(); one(); ts.push(performance.now() - s); }
  ts.sort((a, b) => a - b);
  const mean = ts.reduce((a, b) => a + b, 0) / ts.length;
  console.log(`${f}: spv=${bytes.length}B wgsl=${out.length}B first-call=${cold.toFixed(2)}ms warm mean=${mean.toFixed(2)}ms p50=${pct(ts, .5).toFixed(2)}ms p95=${pct(ts, .95).toFixed(2)}ms (n=${iters})`);
}
