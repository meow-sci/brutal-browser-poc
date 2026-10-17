// WGSL post-processing for the Vulkan-on-WebGPU layer (text level; a WGSL parser is not needed).
//
//   node patch-wgsl.mjs <in.wgsl> <out.wgsl> [--directives] [--push uniform|storage] [--push-group 3] [--push-binding 0]
//   library: import { patchPush, patchDirectives, reflectBindings, ... } from './patch-wgsl.mjs'
//
//  --push uniform   `var<immediate>` / `var<push_constant>` -> `@group(G) @binding(B) var<uniform>` (emulated push constants; works in
//                   every browser, the Vulkan layer supplies a uniform buffer at that group). The obsolete
//                   `enable chromium_experimental_push_constant;` is removed.
//  --push storage   same, but `var<storage, read>`: keeps std430 packing for blocks with float[N] arrays etc. that `uniform` rejects
//                   (uniform arrays need a stride that is a multiple of 16).
//  --directives     `diagnostic(off, derivative_uniformity);`, `enable subgroups;` (when subgroup functions are used), strip `@coherent`.
//
// The web ICD serves push constants only as `uniform` or `storage` (manifest push.mode); WebGPU immediates are not used.

import { readFileSync, writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

// ---- patch 1: push constants -> uniform / storage buffer -------------------------------------------------------
export function patchPush(src, { mode = 'uniform', group = 3, binding = 0 } = {}) {
  const re = /(?:@\w+(?:\([^)]*\))?\s*)*var<\s*(?:push_constant|immediate)\s*>\s*(\w+)\s*:\s*([^;]+);/g;   // naga >= 30 says immediate, older releases push_constant
  let count = 0;
  let code = src.replace(re, (_all, name, type) => {
    count++;
    if (mode === 'uniform') return `@group(${group}) @binding(${binding}) var<uniform> ${name}: ${type.trim()};`;
    if (mode === 'storage') return `@group(${group}) @binding(${binding}) var<storage, read> ${name}: ${type.trim()};`;
    throw new Error(`unknown push mode '${mode}' (uniform | storage)`);
  });
  code = code.replace(/^\s*enable\s+chromium_experimental_push_constant\s*;\s*\n/m, '');
  return { code, count };
}

// ---- patch 2: module-scope directives and native-only attributes ------------------------------------------------
//  * `diagnostic(off, derivative_uniformity);`  GLSL freely samples / takes derivatives in divergent control flow; WGSL's uniformity
//     analysis rejects that as an error unless the diagnostic is switched off.
//  * `enable subgroups;`  naga emits subgroupAdd() etc. WITHOUT the enable directive, Tint then errors.
//  * `@coherent`          wgpu-native-only attribute naga emits for GLSL `coherent buffer`; not valid WGSL in browsers -> stripped.
export function patchDirectives(src) {
  let code = src;
  const report = {};
  if (/@coherent\b/.test(code)) { code = code.replace(/@coherent\s*/g, ''); report.strippedCoherent = true; }
  const pre = [];
  if (/\bsubgroup[A-Z]\w*\s*\(/.test(code) && !/^\s*enable\s+subgroups\s*;/m.test(code)) pre.push('enable subgroups;');
  if (!/diagnostic\(\s*off\s*,\s*derivative_uniformity\s*\)/.test(code)) pre.push('diagnostic(off, derivative_uniformity);');
  report.prefixed = pre;
  // directives must precede all declarations: put ours first (their relative order is irrelevant)
  return { code: pre.length ? pre.join('\n') + '\n' + code : code, report };
}

// ---- reflection straight from the WGSL text ----------------------------------------------------------------------
/** Resource variables: [{ group, binding, space, name, type }]; `space` is uniform | storage | handle. */
export function reflectBindings(src) {
  const out = [];
  const re = /((?:@\w+\([^)]*\)\s*)+)var(?:<([^>]+)>)?\s+(\w+)\s*:\s*([^;]+);/g;
  let m;
  while ((m = re.exec(src))) {
    const g = /@group\((\d+)\)/.exec(m[1]), b = /@binding\((\d+)\)/.exec(m[1]);
    if (g && b) out.push({ group: +g[1], binding: +b[1], space: m[2]?.split(',')[0].trim() ?? 'handle', name: m[3], type: m[4].trim() });
  }
  return out;
}

/** Manifest binding kind: uniform | storage | texture | sampler | storageTexture. Throws for resources WebGPU cannot express. */
export function bindingKind({ space, type }) {
  if (space === 'uniform' || space === 'storage') return space;
  if (/^binding_array\b/.test(type)) throw new Error(`binding arrays are not supported by WebGPU/Tint (${type})`);
  if (/^sampler(_comparison)?$/.test(type)) return 'sampler';
  if (/^texture_storage_/.test(type)) return 'storageTexture';
  if (/^texture_/.test(type)) return 'texture';
  throw new Error(`unsupported resource '${type}' in address space '${space}'`);
}

/** Entry points: [{ stage, name }]. */
export function reflectEntryPoints(src) {
  return [...src.matchAll(/@(vertex|fragment|compute)\b[^{;]*?\bfn\s+(\w+)/g)].map((m) => ({ stage: m[1], name: m[2] }));
}

/** Pipeline-overridable constants (from SPIR-V spec constants): [{ id, name, type }]. */
export function reflectOverrides(src) {
  return [...src.matchAll(/@id\((\d+)\)\s*override\s+(\w+)\s*:\s*([\w<>]+)/g)].map((m) => ({ id: +m[1], name: m[2], type: m[3] }));
}

// ---- convenience wrapper + CLI ---------------------------------------------------------------------------------
export function patchWgsl(src, opts = {}) {
  let code = src;
  const report = {};
  if (opts.push) { const r = patchPush(code, { mode: opts.push, group: opts.pushGroup ?? 3, binding: opts.pushBinding ?? 0 }); code = r.code; report.pushVars = r.count; }
  if (opts.directives) { const r = patchDirectives(code); code = r.code; report.directives = r.report; }
  return { code, report };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const argv = process.argv.slice(2), pos = [], opt = {};
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--directives') opt.directives = true;
    else if (argv[i] === '--push') opt.push = argv[++i];
    else if (argv[i] === '--push-group') opt.pushGroup = Number(argv[++i]);
    else if (argv[i] === '--push-binding') opt.pushBinding = Number(argv[++i]);
    else pos.push(argv[i]);
  }
  const [inPath, outPath] = pos;
  if (!inPath || !outPath) { console.error('usage: node patch-wgsl.mjs <in.wgsl> <out.wgsl> [--directives] [--push uniform|storage] [--push-group 3] [--push-binding 0]'); process.exit(2); }
  const { code, report } = patchWgsl(readFileSync(inPath, 'utf8'), opt);
  writeFileSync(outPath, code);
  console.log(`patched ${inPath} -> ${outPath} ${JSON.stringify(report)}`);
}
