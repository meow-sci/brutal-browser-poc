// Usage: node src/spv-layout-lint.mjs a.spv [b.spv ...]   (exit 3 if any hazard found)
//
// Detects the std140 -> WGSL layout hazard: naga's SPIR-V front end ignores ArrayStride / MatrixStride when it builds
// the WGSL type, so a block member whose SPIR-V stride differs from the WGSL natural stride is read at the WRONG offset
// (experiment: out/browser-check.result.json "std140Probe") or is rejected (UnsupportedMatrixStride).
//   WGSL natural array stride: 32-bit scalar 4, vec2 8, vec3/vec4 16.   WGSL natural matrix column stride: 2-row 8, 3/4-row 16.
import { readFileSync } from 'node:fs';
const OP = { Int: 21, Float: 22, Vector: 23, Matrix: 24, Array: 28, RuntimeArray: 29, Struct: 30, Decorate: 71, MemberDecorate: 72 };
let bad = 0;
for (const path of process.argv.slice(2)) {
  const raw = readFileSync(path); const w = new Uint32Array(raw.buffer.slice(raw.byteOffset, raw.byteOffset + raw.byteLength));
  const type = new Map(), arrayStride = new Map(), memberMatrixStride = [];
  for (let i = 5; i < w.length;) {
    const wc = w[i] >>> 16, op = w[i] & 0xffff;
    if (op === OP.Int || op === OP.Float) type.set(w[i + 1], { k: 'scalar', bytes: w[i + 2] / 8 });
    else if (op === OP.Vector) type.set(w[i + 1], { k: 'vec', comp: w[i + 2], n: w[i + 3] });
    else if (op === OP.Matrix) type.set(w[i + 1], { k: 'mat', col: w[i + 2], cols: w[i + 3] });
    else if (op === OP.Array || op === OP.RuntimeArray) type.set(w[i + 1], { k: 'array', elem: w[i + 2] });
    else if (op === OP.Struct) type.set(w[i + 1], { k: 'struct', members: [...w.slice(i + 2, i + wc)] });
    else if (op === OP.Decorate && w[i + 2] === 6) arrayStride.set(w[i + 1], w[i + 3]);
    else if (op === OP.MemberDecorate && w[i + 3] === 7) memberMatrixStride.push({ struct: w[i + 1], member: w[i + 2], stride: w[i + 4] });
    i += wc;
  }
  const scalarBytes = (t) => (t.k === 'scalar' ? t.bytes : t.k === 'vec' ? (type.get(t.comp).bytes) : null);
  const wgslStride = (t) => { if (!t) return null; if (t.k === 'scalar') return t.bytes; if (t.k === 'vec') { const b = scalarBytes(t); return t.n === 2 ? 2 * b : 4 * b; } return null; };
  const findings = [];
  for (const [id, stride] of arrayStride) { const t = type.get(id); const el = type.get(t?.elem); const nat = wgslStride(el);
    if (nat && nat !== stride) findings.push(`array %${id}: SPIR-V ArrayStride ${stride} but WGSL natural stride ${nat} (${el.k}${el.k === 'vec' ? el.n : ''})`); }
  // matrices: WGSL matCx2 columns are 8 bytes apart; std140 uses 16 -> naga rejects (UnsupportedMatrixStride) / mis-reads
  const stripArrays = (t) => { while (t && t.k === 'array') t = type.get(t.elem); return t; };
  for (const m of memberMatrixStride) {
    const memberType = stripArrays(type.get(type.get(m.struct)?.members?.[m.member]));
    if (memberType?.k === 'mat' && type.get(memberType.col)?.n === 2 && m.stride !== 8) findings.push(`struct %${m.struct} member ${m.member}: matrix with 2-row columns has MatrixStride ${m.stride}, WGSL natural is 8 (naga: UnsupportedMatrixStride)`);
  }
  if (findings.length) { bad++; console.log(`LAYOUT-HAZARD ${path}\n  ` + findings.join('\n  ')); } else console.log(`layout ok ${path}`);
}
process.exitCode = bad ? 3 : 0;
