// Usage: node src/spv2wgsl.mjs <in.spv> [out.wgsl]
// SPIR-V -> WGSL through naga-wasm (naga 30.x compiled to WebAssembly). Prints timing + naga's formatted error.
import { readFileSync, writeFileSync } from 'node:fs';
import { parseSpirv, validate, writeWgsl, NagaError, nagaVersion } from 'naga-wasm';

const [, , inPath, outPath] = process.argv;
const bytes = new Uint8Array(readFileSync(inPath));
const t0 = performance.now();
try {
  using module = parseSpirv(bytes);
  const t1 = performance.now();
  using info = validate(module);
  const t2 = performance.now();
  const wgsl = writeWgsl(module, info);
  const t3 = performance.now();
  if (outPath) writeFileSync(outPath, wgsl);
  console.log(`OK ${inPath} naga=${nagaVersion} parse=${(t1 - t0).toFixed(2)}ms validate=${(t2 - t1).toFixed(2)}ms write=${(t3 - t2).toFixed(2)}ms total=${(t3 - t0).toFixed(2)}ms wgsl_bytes=${wgsl.length}`);
} catch (e) {
  if (e instanceof NagaError) {
    console.log(`FAIL ${inPath} kind=${e.kind} message=${e.message}`);
    console.log(e.formatted);
    process.exitCode = 2;
  } else if (e instanceof WebAssembly.RuntimeError) {
    console.log(`FAIL ${inPath} kind=panic message=${e.message} (naga panicked inside wasm)`);
    process.exitCode = 3;
  } else throw e;
}
