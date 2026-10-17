// Usage: node src/spv-prepare.mjs in.spv out.spv [--offset 500] [--spirv-opt <path>]
//
// SPIR-V -> SPIR-V preparation pass that makes Vulkan-flavoured modules acceptable to naga/WebGPU:
//  (A) split combined image samplers (+ move the sampler to binding+OFFSET)   [described below]
//  (B) drop `NonReadable` member decorations on storage-buffer blocks (GLSL `writeonly buffer`): naga would emit a
//      write-only storage buffer, which WGSL does not have; read_write is a safe superset.
//
// Step 1: `spirv-opt --split-combined-image-sampler` replaces every combined image sampler variable/parameter with an
//         (image, sampler) pair that BOTH keep the original (set, binding). WebGPU/naga reject two resources with the same
//         (group, binding), so
// Step 2: this script rewrites the Binding decoration of every sampler variable that collides with a non-sampler
//         variable at the same (set, binding): newBinding = binding + SAMPLER_BINDING_OFFSET.
//         Genuine `layout(binding=N) uniform sampler` variables (no colliding texture) are left untouched.
//
// Convention (see docs): texture keeps binding b, its sampler lives at b + 500. The Vulkan->WebGPU layer builds
// the bind group layout with the same rule for VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER.
import { readFileSync, writeFileSync, mkdtempSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const args = process.argv.slice(2);
const opt = (name, dflt) => { const i = args.indexOf(name); if (i < 0) return dflt; const v = args[i + 1]; args.splice(i, 2); return v; };
const OFFSET = Number(opt('--offset', '500'));
const SPIRV_OPT = opt('--spirv-opt', 'spirv-opt');
const [inPath, outPath] = args;

const tmp = join(mkdtempSync(join(tmpdir(), 'splitsamp-')), 'split.spv');
const r = spawnSync(SPIRV_OPT, ['--split-combined-image-sampler', inPath, '-o', tmp], { encoding: 'utf8' });
if (r.status !== 0) { console.error(r.stderr); process.exit(r.status ?? 1); }

const raw = readFileSync(tmp);
const words = new Uint32Array(raw.buffer.slice(raw.byteOffset, raw.byteOffset + raw.byteLength));
const OP = { TypeSampler: 26, TypeArray: 28, TypeRuntimeArray: 29, TypePointer: 32, Variable: 59, Decorate: 71 };
const DEC = { Binding: 33, DescriptorSet: 34 };

// pass 1: types / variables / decorations
const samplerTypes = new Set(), pointee = new Map(), varType = new Map(), bindingWord = new Map(), setOf = new Map();
const elemOf = new Map(); // array type id -> element type id
for (let i = 5; i < words.length;) {
  const wc = words[i] >>> 16, op = words[i] & 0xffff;
  if (op === OP.TypeSampler) samplerTypes.add(words[i + 1]);
  else if (op === OP.TypeArray || op === OP.TypeRuntimeArray) elemOf.set(words[i + 1], words[i + 2]);
  else if (op === OP.TypePointer) pointee.set(words[i + 1], words[i + 3]);
  else if (op === OP.Variable) varType.set(words[i + 2], words[i + 1]);
  else if (op === OP.Decorate) {
    if (words[i + 2] === DEC.Binding) bindingWord.set(words[i + 1], i + 3);
    if (words[i + 2] === DEC.DescriptorSet) setOf.set(words[i + 1], words[i + 3]);
  }
  i += wc;
}
const isSamplerType = (t) => samplerTypes.has(t) || (elemOf.has(t) && isSamplerType(elemOf.get(t)));
const samplerVars = new Set();
for (const [v, pt] of varType) if (isSamplerType(pointee.get(pt))) samplerVars.add(v);

// binding -> variables, keyed by "set:binding"
const slot = (v) => `${setOf.get(v) ?? 0}:${words[bindingWord.get(v)]}`;
const occupied = new Map();
for (const v of bindingWord.keys()) { const k = slot(v); (occupied.get(k) ?? occupied.set(k, []).get(k)).push(v); }
let patched = 0;
for (const v of samplerVars) {
  if (!bindingWord.has(v)) continue;
  const mates = occupied.get(slot(v)).filter((o) => !samplerVars.has(o));
  if (mates.length) { words[bindingWord.get(v)] += OFFSET; patched++; }
}
// (B) strip OpMemberDecorate <struct> <member> NonReadable (decoration 24)
const OP_MEMBER_DECORATE = 72, DEC_NONREADABLE = 25;   // Decoration 24 = NonWritable, 25 = NonReadable
const varStorage = new Map();                          // result id -> storage class (UniformConstant = 0 -> images: keep)
for (let i = 5; i < words.length; i += words[i] >>> 16) if ((words[i] & 0xffff) === OP.Variable) varStorage.set(words[i + 2], words[i + 3]);
const kept = [...words.slice(0, 5)]; let stripped = 0;
for (let i = 5; i < words.length;) {
  const wc = words[i] >>> 16, op = words[i] & 0xffff;
  if (op === OP_MEMBER_DECORATE && words[i + 3] === DEC_NONREADABLE) { stripped++; i += wc; continue; }
  if (op === OP.Decorate && words[i + 2] === DEC_NONREADABLE && (varStorage.get(words[i + 1]) ?? 0) !== 0) { stripped++; i += wc; continue; }
  for (let j = 0; j < wc; j++) kept.push(words[i + j]);
  i += wc;
}
writeFileSync(outPath, Buffer.from(new Uint32Array(kept).buffer));
console.log(`prepare: split-combined + sampler binding offset +${OFFSET}: ${patched} sampler variable(s) moved; ${stripped} NonReadable member decoration(s) stripped (${inPath} -> ${outPath})`);
