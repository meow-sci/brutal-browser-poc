// Layout / feature lint for Vulkan SPIR-V that is going to be translated to WGSL.
//
//   node spv-layout-lint.mjs a.spv [b.spv ...]     (exit 3 if any hazard is found)
//   library: import { lintSpirv } from './spv-layout-lint.mjs'
//
// 1. std140 -> WGSL layout hazards. naga's SPIR-V front end ignores ArrayStride / MatrixStride when it builds the WGSL type, so a block
//    member whose SPIR-V stride differs from the WGSL natural stride is read at the WRONG offset (or rejected):
//      - arrays of 32-bit scalars or vec2 in a std140 block (SPIR-V stride 16, WGSL stride 4 / 8)   -> author them as vec4[N]
//      - matrices with 2-row columns (mat2, mat3x2, ...; MatrixStride 16, WGSL 8)                     -> naga: UnsupportedMatrixStride
//    WGSL natural array stride: scalar 4, vec2 8, vec3/vec4 16.   WGSL natural matrix column stride: 2-row 8, 3/4-row 16.
//    Not covered: arrays of structs and nested-struct padding (spot-check those by hand).
// 2. WebGPU has no 64-bit types (Float64 / Int64 capabilities).
import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { CAP, DEC, loadWords, parseModule, stripArrays } from './spv-util.mjs';

/** @returns {string[]} human-readable findings; empty when the module is fine */
export function lintSpirv(bytes) {
  const m = parseModule(loadWords(bytes));
  const findings = [];

  const wgslArrayStride = (t) => {
    if (!t) return null;
    if (t.kind === 'float' || t.kind === 'int') return t.bytes;
    if (t.kind === 'vector') { const b = m.types.get(t.comp).bytes; return t.count === 2 ? 2 * b : 4 * b; }
    return null;                                                    // matrices, structs, nested arrays: not checked
  };
  const describe = (t) => (t.kind === 'vector' ? `vec${t.count}` : 'scalar');

  for (const [typeId, decs] of m.decorations) {
    const stride = decs.get(DEC.ArrayStride)?.[0];
    const arr = m.types.get(typeId);
    if (stride === undefined || (arr?.kind !== 'array' && arr?.kind !== 'runtimeArray')) continue;
    const elem = m.types.get(arr.elem), natural = wgslArrayStride(elem);
    if (natural && natural !== stride) findings.push(`array %${typeId}: SPIR-V ArrayStride ${stride} but WGSL natural stride ${natural} (${describe(elem)} elements; use vec4[N] in std140 blocks)`);
  }
  for (const [key, decs] of m.memberDecorations) {
    const stride = decs.get(DEC.MatrixStride)?.[0];
    if (stride === undefined) continue;
    const [struct, member] = key.split(':').map(Number);
    const memberType = stripArrays(m, m.types.get(struct)?.members?.[member]);
    if (memberType?.kind === 'matrix' && m.types.get(memberType.col)?.count === 2 && stride !== 8) {
      findings.push(`struct %${struct} member ${member}: matrix with 2-row columns has MatrixStride ${stride}, WGSL natural is 8 (naga: UnsupportedMatrixStride)`);
    }
  }
  if (m.capabilities.has(CAP.Float64)) findings.push('Float64 capability (double): WebGPU has no 64-bit floats');
  if (m.capabilities.has(CAP.Int64)) findings.push('Int64 capability (int64/uint64): WebGPU has no 64-bit integers');
  return findings;
}

// ---- CLI -------------------------------------------------------------------------------------------------------
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  let bad = 0;
  for (const path of process.argv.slice(2)) {
    const findings = lintSpirv(new Uint8Array(readFileSync(path)));
    if (findings.length) { bad++; console.log(`LAYOUT-HAZARD ${path}\n  ${findings.join('\n  ')}`); } else console.log(`layout ok ${path}`);
  }
  process.exitCode = bad ? 3 : 0;
}
