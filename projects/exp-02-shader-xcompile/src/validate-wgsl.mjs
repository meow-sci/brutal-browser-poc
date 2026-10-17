// Usage: node src/validate-wgsl.mjs <file.wgsl> [more.wgsl ...]
// Parse + validate WGSL with naga-wasm's WGSL front end (no browser needed). Exit code 2 on any failure.
import { readFileSync } from 'node:fs';
import { parseWgsl, validate, NagaError, nagaVersion } from 'naga-wasm';
let failed = 0;
for (const p of process.argv.slice(2)) {
  try {
    using m = parseWgsl(readFileSync(p, 'utf8'));
    using _i = validate(m);
    console.log(`WGSL-VALID ${p} (naga ${nagaVersion})`);
  } catch (e) {
    if (!(e instanceof NagaError)) throw e;
    failed++;
    console.log(`WGSL-INVALID ${p} kind=${e.kind}\n${e.formatted}`);
  }
}
process.exitCode = failed ? 2 : 0;
