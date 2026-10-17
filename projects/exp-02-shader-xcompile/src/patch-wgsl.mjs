// WGSL post-processing patches for the Vulkan-on-WebGPU layer (text level, no WGSL parser needed).
//
//   node src/patch-wgsl.mjs <in.wgsl> <out.wgsl> [--y-flip] [--directives] [--push uniform|storage|keep] [--push-group 3] [--push-binding 0]
//
//  --y-flip            wrap every @vertex entry point: original body becomes `<name>_vk_inner` and the wrapper negates
//                      the @builtin(position).y of the result. ONLY for WGSL that was NOT produced by naga's SPIR-V front end
//                      (naga already negates Y by default - flipping twice would undo it). Needed for slangc -target wgsl.
//  --directives        add `diagnostic(off, derivative_uniformity);`, `enable subgroups;` (if subgroup builtins are used), strip native-only `@coherent`.
//  --push uniform      `var<immediate>` / `var<push_constant>` -> `@group(G) @binding(B) var<uniform>` (emulated push constants,
//                      works in every browser; the layer supplies a uniform buffer at that group). `enable chromium_experimental_push_constant;`
//                      is removed.
//  --push storage      same, but as `var<storage, read>`: keeps std430 tight packing (float[4] arrays etc. that `uniform` rejects: stride must be a multiple of 16).
//  --push keep         keep `var<immediate>` and make sure `requires immediate_address_space;` is present (Chrome 149+ native immediates).
//
// Library use: import { patchWgsl } from './patch-wgsl.mjs'
import { readFileSync, writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

// ---- small helpers ---------------------------------------------------------------------------------------------
function matchClose(src, openIdx, open, close) {
  let depth = 0;
  for (let i = openIdx; i < src.length; i++) {
    if (src[i] === open) depth++;
    else if (src[i] === close && --depth === 0) return i;
  }
  throw new Error(`unbalanced ${open}${close} at ${openIdx}`);
}
function splitTopLevel(s) {          // split on commas not nested in () or <>
  const out = []; let depth = 0, cur = '';
  for (const ch of s) {
    if (ch === '(' || ch === '<') depth++;
    if (ch === ')' || ch === '>') depth--;
    if (ch === ',' && depth === 0) { out.push(cur); cur = ''; } else cur += ch;
  }
  if (cur.trim()) out.push(cur);
  return out.map((p) => p.trim()).filter(Boolean);
}
const ATTR_RE = /@\w+(\([^)]*\))?\s*/g;
const stripAttrs = (s) => s.replace(ATTR_RE, '').trim();

// ---- patch 1: Y-flip wrapper for @vertex entry points ----------------------------------------------------------
export function patchYFlip(src) {
  const re = /@vertex\s+fn\s+(\w+)\s*\(/g;
  let m, out = '', last = 0, count = 0;
  while ((m = re.exec(src))) {
    const name = m[1];
    const open = m.index + m[0].length - 1;
    const close = matchClose(src, open, '(', ')');
    const paramsRaw = src.slice(open + 1, close);
    const bodyOpen = src.indexOf('{', close);
    const retRaw = src.slice(close + 1, bodyOpen).replace(/^\s*->\s*/, '').trim();     // '' if void
    const bodyClose = matchClose(src, bodyOpen, '{', '}');
    if (!retRaw) throw new Error(`vertex entry ${name} returns nothing - no position to flip`);

    const params = splitTopLevel(paramsRaw).map((p) => {
      const colon = p.lastIndexOf(':');
      const head = p.slice(0, colon), type = p.slice(colon + 1).trim();
      return { raw: p, name: stripAttrs(head), type };
    });
    // where is the position output?
    let posExpr;
    if (/@builtin\(\s*position\s*\)/.test(retRaw)) posExpr = '';                       // returns a bare vec4 position
    else {
      const sm = new RegExp(`struct\\s+${retRaw}\\s*\\{([\\s\\S]*?)\\}`).exec(src);
      if (!sm) throw new Error(`cannot find struct ${retRaw} returned by ${name}`);
      const pm = /@builtin\(\s*position\s*\)\s*(\w+)\s*:/.exec(sm[1]);
      if (!pm) throw new Error(`struct ${retRaw} has no @builtin(position) member`);
      posExpr = '.' + pm[1];
    }
    const inner = `${name}_vk_inner`;
    const innerSig = `fn ${inner}(${params.map((p) => `${p.name}: ${p.type}`).join(', ')}) -> ${stripAttrs(retRaw)}`;
    const wrapper =
      `\n\n// vk-layer: Vulkan clip space is Y-down, WebGPU is Y-up -> negate position.y\n` +
      `@vertex\nfn ${name}(${paramsRaw.trim()}) -> ${retRaw} {\n` +
      `    var vk_out = ${inner}(${params.map((p) => p.name).join(', ')});\n` +
      `    vk_out${posExpr}.y = -vk_out${posExpr}.y;\n    return vk_out;\n}\n`;
    out += src.slice(last, m.index) + innerSig + ' ' + src.slice(bodyOpen, bodyClose + 1) + wrapper;
    last = bodyClose + 1; re.lastIndex = last; count++;
  }
  return { code: out + src.slice(last), count };
}

// ---- patch 2: push constants -> uniform buffer (or keep as immediates) -----------------------------------------
export function patchPush(src, { mode = 'uniform', group = 3, binding = 0 } = {}) {
  const re = /(?:@\w+(?:\([^)]*\))?\s*)*var<\s*(?:push_constant|immediate)\s*>\s*(\w+)\s*:\s*([^;]+);/g;
  let n = 0;
  let code = src.replace(re, (_all, name, type) => {
    n++;
    if (mode === 'uniform') return `@group(${group}) @binding(${binding}) var<uniform> ${name}: ${type.trim()};`;
    if (mode === 'storage') return `@group(${group}) @binding(${binding}) var<storage, read> ${name}: ${type.trim()};`;
    return `var<immediate> ${name}: ${type.trim()};`;
  });
  code = code.replace(/^\s*enable\s+chromium_experimental_push_constant\s*;\s*\n/m, '');
  if (n && mode === 'keep' && !/^\s*requires\s+immediate_address_space\s*;/m.test(code))
    code = 'requires immediate_address_space;\n' + code;
  return { code, count: n };
}

// ---- tiny binding reflection straight from WGSL text (useful for building bind group layouts) -------------------
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

// ---- patch 3: module-scope directives / native-only attributes that browsers need or reject ------------------------
//  * `diagnostic(off, derivative_uniformity);`  GLSL freely samples/derives in divergent control flow; WGSL's uniformity analysis
//     rejects that as an error unless the diagnostic is turned off (Tint's own CLI has --allow-non-uniform-derivatives for this).
//  * `enable subgroups;`   naga emits subgroupAdd() etc. WITHOUT the enable directive (Tint then errors).
//  * `@coherent`           wgpu-native-only attribute naga emits for GLSL `coherent buffer`; not valid WGSL in browsers -> stripped.
export function patchDirectives(src) {
  let code = src, report = {};
  if (/@coherent\b/.test(code)) { code = code.replace(/@coherent\s*/g, ''); report.strippedCoherent = true; }
  const pre = [];
  if (/\bsubgroup[A-Z]\w*\s*\(/.test(code) && !/^\s*enable\s+subgroups\s*;/m.test(code)) pre.push('enable subgroups;');
  if (!/diagnostic\(\s*off\s*,\s*derivative_uniformity\s*\)/.test(code)) pre.push('diagnostic(off, derivative_uniformity);');
  // `enable` directives must come first, then diagnostic directives; put ours before everything else.
  const m = code.match(/^(?:\s*(?:enable|requires|diagnostic)[^;]*;\s*)*/);
  code = pre.join('\n') + (pre.length ? '\n' : '') + code;
  report.prefixed = pre;
  return { code, report };
}

export function patchWgsl(src, opts = {}) {
  let code = src; const report = {};
  if (opts.directives) { const r = patchDirectives(code); code = r.code; report.directives = r.report; }
  if (opts.yFlip) { const r = patchYFlip(code); code = r.code; report.yFlipWrappers = r.count; }
  if (opts.push) { const r = patchPush(code, { mode: opts.push, group: opts.pushGroup ?? 3, binding: opts.pushBinding ?? 0 }); code = r.code; report.pushVars = r.count; }
  return { code, report };
}

// ---- CLI -------------------------------------------------------------------------------------------------------
if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const argv = process.argv.slice(2), pos = [], opt = {};
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--y-flip') opt.yFlip = true;
    else if (argv[i] === '--directives') opt.directives = true;
    else if (argv[i] === '--push') opt.push = argv[++i];
    else if (argv[i] === '--push-group') opt.pushGroup = Number(argv[++i]);
    else if (argv[i] === '--push-binding') opt.pushBinding = Number(argv[++i]);
    else pos.push(argv[i]);
  }
  const [inPath, outPath] = pos;
  const { code, report } = patchWgsl(readFileSync(inPath, 'utf8'), opt);
  writeFileSync(outPath, code);
  console.log(`patched ${inPath} -> ${outPath} ${JSON.stringify(report)}`);
}
