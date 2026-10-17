// Usage: node src/glsl2wgsl.mjs <file.vert|frag|comp> [out.wgsl]
// Direct GLSL -> WGSL with naga's *GLSL front end* (secondary/less mature than the SPIR-V front end). No SPIR-V involved.
import { readFileSync, writeFileSync } from 'node:fs';
import { parseGlsl, validate, writeWgsl, NagaError } from 'naga-wasm';
const [, , inPath, outPath] = process.argv;
const ext = inPath.split('.').pop();
const stage = { vert: 'vertex', frag: 'fragment', comp: 'compute' }[ext];
try {
  using m = parseGlsl(readFileSync(inPath, 'utf8'), { stage });
  using i = validate(m);
  const wgsl = writeWgsl(m, i);
  if (outPath) writeFileSync(outPath, wgsl);
  console.log(`OK ${inPath} (GLSL front end)`);
} catch (e) {
  if (!(e instanceof NagaError)) throw e;
  console.log(`FAIL ${inPath} kind=${e.kind}\n${e.formatted.split('\n').slice(0, 14).join('\n')}`);
  process.exitCode = 2;
}
