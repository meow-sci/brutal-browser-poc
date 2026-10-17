// SPIR-V -> SPIR-V preparation pass that makes Vulkan-flavoured modules acceptable to naga / WebGPU.
//
//   node spv-prepare.mjs in.spv out.spv [--offset 500] [--spirv-opt <path>] [--target-env vulkan1.1]
//   library: import { prepareSpirv } from './spv-prepare.mjs'
//
//  (A) Combined image samplers (GLSL `sampler2D`). WebGPU has separate textures and samplers, so
//        1. `spirv-opt --split-combined-image-sampler` replaces each combined variable with an (image, sampler) pair that BOTH keep
//           the original (set, binding) - naga/WebGPU reject two resources at one slot, hence
//        2. every sampler variable that collides with a non-sampler at the same (set, binding) moves to binding + OFFSET.
//      Convention: the texture keeps binding b, its sampler lives at b + 500 (b must be < 500). The Vulkan->WebGPU layer builds the
//      bind group layout with the same rule for VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER. Modules without combined samplers skip
//      spirv-opt entirely. Genuine `uniform sampler` variables (no colliding texture) are never touched.
//  (B) Drop `NonReadable` on storage-buffer blocks (GLSL `writeonly buffer`): WGSL has no write-only storage buffers and naga rejects
//      them; read_write is a safe superset. Storage images keep their NonReadable (write-only storage textures exist in WGSL).
//
// If neither (A) nor (B) applies the input bytes are returned untouched.
import { readFileSync, writeFileSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { OP, DEC, STORAGE, instructions, loadWords, wordsToBuffer, parseModule, stripArrays, combinedSamplerVariables } from './spv-util.mjs';
import { findTool, run } from './exec-util.mjs';

export const SAMPLER_BINDING_OFFSET = 500;

/**
 * @param {Uint8Array} bytes  SPIR-V module
 * @returns {{ bytes: Uint8Array, changed: boolean, combined: number, samplersMoved: number, nonReadableStripped: number }}
 */
export function prepareSpirv(bytes, { offset = SAMPLER_BINDING_OFFSET, spirvOpt, targetEnv = 'vulkan1.1' } = {}) {
  let words = loadWords(bytes);
  const combined = combinedSamplerVariables(parseModule(words)).length;

  // (A) split combined image samplers with spirv-opt, then move the new samplers out of the way
  let samplersMoved = 0;
  if (combined > 0) {
    words = splitCombinedSamplers(words, { spirvOpt, targetEnv });
    samplersMoved = offsetSamplerBindings(words, offset);
  }
  // (B) strip NonReadable from storage-buffer declarations
  const { words: outWords, stripped } = stripNonReadable(words);

  if (combined === 0 && stripped === 0) return { bytes, changed: false, combined, samplersMoved, nonReadableStripped: 0 };
  return { bytes: new Uint8Array(wordsToBuffer(outWords)), changed: true, combined, samplersMoved, nonReadableStripped: stripped };
}

function splitCombinedSamplers(words, { spirvOpt, targetEnv }) {
  const dir = mkdtempSync(join(tmpdir(), 'spv-prepare-'));
  try {
    const inPath = join(dir, 'in.spv'), outPath = join(dir, 'split.spv');
    writeFileSync(inPath, wordsToBuffer(words));
    run(findTool('spirv-opt', { explicit: spirvOpt, env: 'SPIRV_OPT' }), [`--target-env=${targetEnv}`, '--split-combined-image-sampler', inPath, '-o', outPath], 'spirv-opt --split-combined-image-sampler');
    return loadWords(readFileSync(outPath));
  } finally { rmSync(dir, { recursive: true, force: true }); }
}

/** Mutates `words`: sampler variables sharing (set, binding) with a non-sampler get binding + offset. Returns how many moved. */
function offsetSamplerBindings(words, offset) {
  const m = parseModule(words);
  const isSamplerVar = (v) => { const p = m.types.get(v.type); return p?.kind === 'pointer' && stripArrays(m, p.pointee)?.kind === 'sampler'; };
  // locate the Binding operand word of every variable
  const bindingWordAt = new Map();
  for (const { op, at } of instructions(words)) if (op === OP.Decorate && words[at + 2] === DEC.Binding) bindingWordAt.set(words[at + 1], at + 3);
  const slotOf = (v) => `${m.decorations.get(v.id)?.get(DEC.DescriptorSet)?.[0] ?? 0}:${words[bindingWordAt.get(v.id)]}`;
  const occupied = new Map();                                    // "set:binding" -> variables
  for (const v of m.variables) if (bindingWordAt.has(v.id)) { const k = slotOf(v); (occupied.get(k) ?? occupied.set(k, []).get(k)).push(v); }
  let moved = 0;
  for (const v of m.variables.filter((x) => bindingWordAt.has(x.id) && isSamplerVar(x))) {
    if (!occupied.get(slotOf(v)).some((o) => !isSamplerVar(o))) continue;
    const b = words[bindingWordAt.get(v.id)];
    if (b >= offset) throw new Error(`combined image sampler at binding ${b} cannot be split: bindings must be < ${offset} (sampler goes to binding + ${offset})`);
    words[bindingWordAt.get(v.id)] = b + offset;
    moved++;
  }
  return moved;
}

function stripNonReadable(words) {
  const m = parseModule(words);
  const storageOf = new Map(m.variables.map((v) => [v.id, v.storage]));
  const kept = [...words.slice(0, 5)];
  let stripped = 0;
  for (const { op, wc, at } of instructions(words)) {
    const memberOfBlock = op === OP.MemberDecorate && words[at + 3] === DEC.NonReadable;                       // OpMemberDecorate %struct n NonReadable
    const onBufferVar = op === OP.Decorate && words[at + 2] === DEC.NonReadable && (storageOf.get(words[at + 1]) ?? STORAGE.UniformConstant) !== STORAGE.UniformConstant;
    if (memberOfBlock || onBufferVar) { stripped++; continue; }
    for (let j = 0; j < wc; j++) kept.push(words[at + j]);
  }
  return { words: stripped ? new Uint32Array(kept) : words, stripped };
}

// ---- CLI -------------------------------------------------------------------------------------------------------
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const args = process.argv.slice(2);
  const opt = (name, dflt) => { const i = args.indexOf(name); if (i < 0) return dflt; const v = args[i + 1]; args.splice(i, 2); return v; };
  const offset = Number(opt('--offset', SAMPLER_BINDING_OFFSET)), spirvOpt = opt('--spirv-opt'), targetEnv = opt('--target-env', 'vulkan1.1');
  const [inPath, outPath] = args;
  if (!inPath || !outPath) { console.error('usage: node spv-prepare.mjs in.spv out.spv [--offset 500] [--spirv-opt <path>] [--target-env vulkan1.1]'); process.exit(2); }
  try {
    const r = prepareSpirv(new Uint8Array(readFileSync(inPath)), { offset, spirvOpt, targetEnv });
    writeFileSync(outPath, r.bytes);
    console.log(`prepare: ${r.combined} combined sampler(s), ${r.samplersMoved} sampler binding(s) moved by +${offset}, ${r.nonReadableStripped} NonReadable stripped${r.changed ? '' : ' (unchanged)'} (${inPath} -> ${outPath})`);
  } catch (e) { console.error(`prepare: ${e.message}`); process.exit(1); }
}
