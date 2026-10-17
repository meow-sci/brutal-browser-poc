// Vulkan SPIR-V -> WGSL + manifest.json (docs/architecture.md section 6).
//
//   node build-shaders.mjs --spv-dir <dir with *.spv> --out <dir> [--no-browser-check] [--glslc <path>] [--target-env vulkan1.1]
//
// Input: the *.spv files that tools/Shaders.targets compiled with `glslc --target-env=vulkan1.1 -O`. Nothing is recompiled here: the
// manifest key is the SHA-256 of the ORIGINAL file bytes, which must be byte-identical to what the game embeds and hands to
// vkCreateShaderModule.
//
// Per shader:  spirv-val -> reflect -> spv-prepare (split combined samplers, +500, NonReadable) -> layout lint -> naga SPIR-V->WGSL
//              (Y flip: naga's default) -> patch-wgsl (push constants, directives) -> naga WGSL re-parse+validate -> reflect -> manifest.
// Output: <out>/<name>.wgsl for every shader and <out>/manifest.json. Exit code 1 if ANY shader fails (nothing is written then).
import { readdirSync, readFileSync, writeFileSync, mkdirSync, existsSync, statSync, rmSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { parseSpirv, parseWgsl, validate, writeWgsl, NagaError } from 'naga-wasm';
import { findTool, run } from './exec-util.mjs';
import { loadWords, parseModule, pushConstantBlockEnd } from './spv-util.mjs';
import { prepareSpirv, SAMPLER_BINDING_OFFSET } from './spv-prepare.mjs';
import { lintSpirv } from './spv-layout-lint.mjs';
import { patchPush, patchDirectives, reflectBindings, bindingKind, reflectEntryPoints, reflectOverrides } from './patch-wgsl.mjs';

const TOOLS_DIR = dirname(fileURLToPath(import.meta.url));
// 128 bytes = the ICD's maxPushConstantsSize (src/Brutal.Vulkan.WebGpu/Profile.cs); one 256-byte ring slot holds a block
const PUSH_GROUP = 3, PUSH_BINDING = 0, PUSH_MAX_BYTES = 128;
const MANIFEST_VERSION = 1;

const USAGE = `usage: node build-shaders.mjs --spv-dir <dir with *.spv> --out <dir> [--no-browser-check] [--glslc <path>] [--target-env vulkan1.1]
  --spv-dir           directory with the glslc output (<name>.<ext>.spv)
  --out               output directory (<name>.wgsl and manifest.json)
  --no-browser-check  skip the headless-Chromium createShaderModule check (check-wgsl.mjs)
  --glslc             glslc executable, used only to record its version in the manifest (default: glslc from PATH / Vulkan SDK)
  --target-env        spirv-val target environment (default vulkan1.1; must match what glslc used)`;

// ---- helpers -------------------------------------------------------------------------------------------------------
class ShaderError extends Error {
  constructor(step, message) { super(`${step}: ${message}`); }
}
const firstLines = (text, n = 8) => String(text).trim().split('\n').slice(0, n).join('\n    ');
const sha256 = (bytes) => createHash('sha256').update(bytes).digest('hex');
const byName = (a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0);        // code-unit order: identical on every machine

/** Short advice for the naga errors authors actually hit. */
const HINTS = [
  [/PointSize/, 'gl_PointSize cannot be written on WebGPU: remove the write'],
  [/SpecConstantOp/, 'use specialization constants directly; arithmetic on them in constant expressions (SpecConstantOp) is not importable'],
  [/ShaderNonUniform|NonUniform/, 'bindless / non-uniform resource indexing is not supported'],
  [/PhysicalStorageBuffer/, 'buffer device addresses are not supported'],
  [/CopyLogical|DemoteToHelperInvocation/, 'compile with --target-env=vulkan1.1 (SPIR-V 1.3)'],
];
const withHint = (msg) => { const h = HINTS.find(([re]) => re.test(msg)); return h ? `${msg} [hint: ${h[1]}]` : msg; };

/** Run one pipeline step and attach its name to any error. */
function step(name, fn) {
  try { return fn(); } catch (e) {
    if (e instanceof ShaderError) throw e;
    if (e instanceof NagaError) throw new ShaderError(name, withHint(`${e.kind} error: ${firstLines(e.formatted || e.message)}`));
    if (e instanceof WebAssembly.RuntimeError) throw new ShaderError(name, `naga panicked inside wasm (${e.message}); unsupported SPIR-V construct`);
    throw new ShaderError(name, firstLines(e.message));
  }
}

/** Does naga's WGSL front end + validator accept this text? (Tint is the final judge: see check-wgsl.mjs.) */
function wgslCheck(code) {
  try { using module = parseWgsl(code); using _info = validate(module); return null; } catch (e) {
    if (e instanceof NagaError) return e.formatted || e.message;
    throw e;
  }
}

function nagaWasmVersion() {
  let dir = dirname(fileURLToPath(import.meta.resolve('naga-wasm')));
  for (let i = 0; i < 6; i++, dir = dirname(dir)) {
    const pkg = join(dir, 'package.json');
    if (existsSync(pkg)) { const j = JSON.parse(readFileSync(pkg, 'utf8')); if (j.name === 'naga-wasm') return j.version; }
  }
  throw new Error('cannot locate node_modules/naga-wasm/package.json (run `npm ci` in tools/)');
}

function glslcVersion(explicit) {
  try { return run(findTool('glslc', { explicit, env: 'GLSLC' }), ['--version']).split('\n')[0].trim(); } catch (e) {
    console.error(`warning: could not run glslc --version (${firstLines(e.message, 1)}); recording generator.glslc = "unknown"`);
    return 'unknown';
  }
}

// ---- one shader ----------------------------------------------------------------------------------------------------
function translate(name, spvPath, original, { spirvVal, targetEnv }) {
  // 1. spirv-val on the exact bytes the game will embed
  step('spirv-val', () => run(spirvVal, ['--target-env', targetEnv, spvPath], `spirv-val --target-env ${targetEnv}`));

  // 2. what is this module? (one entry point, a stage WebGPU knows)
  const mod = step('spirv-reflect', () => parseModule(loadWords(original)));
  if (mod.entryPoints.length !== 1) throw new ShaderError('spirv-reflect', `expected exactly 1 entry point, found ${mod.entryPoints.length} (${mod.entryPoints.map((e) => e.name).join(', ') || 'none'})`);
  const [entry] = mod.entryPoints;
  if (!['vertex', 'fragment', 'compute'].includes(entry.model)) throw new ShaderError('spirv-reflect', `unsupported execution model '${entry.model}'`);

  // 3. combined samplers -> texture@b + sampler@b+500, NonReadable (only touches the module when needed)
  const prepared = step('spv-prepare', () => prepareSpirv(original, { targetEnv }));

  // 4. std140 hazards, 64-bit types
  const findings = step('layout-lint', () => lintSpirv(prepared.bytes));
  if (findings.length) throw new ShaderError('layout-lint', findings.join('; '));

  // 5. SPIR-V -> WGSL (naga flips Y by default, which is what Vulkan clip space needs)
  let wgsl = step('naga spirv->wgsl', () => {
    using module = parseSpirv(prepared.bytes);
    using info = validate(module);
    return writeWgsl(module, info);
  });

  // 6. push constants: uniform at group 3 binding 0, else storage when the block needs std430 packing (float[N] arrays etc.)
  let push = { mode: 'none', size: 0 };
  if (/\bvar<\s*(immediate|push_constant)\s*>/.test(wgsl)) {
    if (reflectBindings(wgsl).some((b) => b.group === PUSH_GROUP && b.binding === PUSH_BINDING)) {
      throw new ShaderError('push-constants', `descriptor set ${PUSH_GROUP} binding ${PUSH_BINDING} is reserved for emulated push constants but the shader also uses it`);
    }
    // the 128-byte limit applies to the block as declared in the SPIR-V
    const pushEnd = step('push-constants', () => pushConstantBlockEnd(mod));
    if (pushEnd > PUSH_MAX_BYTES) {
      throw new ShaderError('push-constants', `push-constant block is ${pushEnd} bytes, the web pipeline supports at most ${PUSH_MAX_BYTES} (move the rest into a UBO)`);
    }
    const errors = [];
    for (const mode of ['uniform', 'storage']) {
      const patched = patchPush(wgsl, { mode, group: PUSH_GROUP, binding: PUSH_BINDING }).code;
      const err = wgslCheck(patched);
      if (!err) { wgsl = patched; push = { mode, size: Math.ceil(pushEnd / 16) * 16 }; break; }   // size: block rounded up to 16, the WGSL uniform binding granularity
      errors.push(`${mode}: ${firstLines(err, 3)}`);
    }
    if (push.mode === 'none') throw new ShaderError('push-constants', `block fits neither var<uniform> nor var<storage>\n    ${errors.join('\n    ')}`);
  }

  // 7. module directives, then the final gate: naga must re-parse and validate what we emit
  wgsl = patchDirectives(wgsl).code;
  const err = wgslCheck(wgsl.replace(/^enable subgroups;\n/m, ''));       // naga's WGSL parser does not know `enable subgroups;` (Tint does)
  if (err) throw new ShaderError('wgsl-validate', `patched WGSL does not validate\n    ${firstLines(err, 6)}`);

  // 8. reflect the final WGSL
  const entryPoints = reflectEntryPoints(wgsl);
  if (entryPoints.length !== 1) throw new ShaderError('wgsl-reflect', `expected 1 entry point in the WGSL, found ${entryPoints.length}`);
  if (entryPoints[0].stage !== entry.model) throw new ShaderError('wgsl-reflect', `SPIR-V entry point is ${entry.model} but the WGSL says ${entryPoints[0].stage}`);
  const bindings = step('wgsl-reflect', () => reflectBindings(wgsl)
    .map((b) => ({ group: b.group, binding: b.binding, kind: bindingKind(b), name: b.name }))
    .sort((a, b) => a.group - b.group || a.binding - b.binding));
  const overrides = Object.fromEntries(reflectOverrides(wgsl).map((o) => [String(o.id), { name: o.name, type: o.type }]));

  return { name, stage: entry.model, entryPoint: entryPoints[0].name, wgsl, push, bindings, overrides };
}

// ---- main ----------------------------------------------------------------------------------------------------------
function main() {
  let args;
  try {
    args = parseArgs({
      options: {
        'spv-dir': { type: 'string' }, out: { type: 'string' },
        'no-browser-check': { type: 'boolean', default: false }, glslc: { type: 'string' }, 'target-env': { type: 'string', default: 'vulkan1.1' },
        help: { type: 'boolean', short: 'h', default: false },
      },
      allowPositionals: false,
    }).values;
  } catch (e) { console.error(`${e.message}\n${USAGE}`); return 2; }
  if (args.help) { console.log(USAGE); return 0; }
  if (!args['spv-dir'] || !args.out) { console.error(USAGE); return 2; }

  const targetEnv = args['target-env'];
  const outDir = resolve(args.out), manifestPath = join(outDir, 'manifest.json');

  // collect *.spv from --spv-dir (tools/Shaders.targets deletes .spv files whose ShaderSource is gone before this runs)
  const spvDir = resolve(args['spv-dir']);
  if (!existsSync(spvDir) || !statSync(spvDir).isDirectory()) { console.error(`error: --spv-dir '${args['spv-dir']}' is not a directory`); return 2; }
  const files = readdirSync(spvDir).filter((f) => f.endsWith('.spv')).map((f) => ({ name: basename(f, '.spv'), path: join(spvDir, f) }));
  if (files.length === 0) console.error(`warning: no *.spv files in '${spvDir}'`);
  files.sort(byName);

  let tools;                                   // spirv-opt is only looked up (inside spv-prepare) when a shader has combined samplers
  try { tools = { spirvVal: findTool('spirv-val', { env: 'SPIRV_VAL' }), targetEnv }; } catch (e) { console.error(`error: ${e.message}`); return 2; }

  mkdirSync(outDir, { recursive: true });
  const shaders = {}, failures = [];
  for (const { name, path } of files) {
    try {
      const original = new Uint8Array(readFileSync(path));
      const sha = sha256(original);
      if (shaders[sha]) {
        console.error(`warning: ${name} is byte-identical to ${shaders[sha].name}; one manifest entry (keyed by the hash) serves both`);
        writeFileSync(join(outDir, `${name}.wgsl`), shaders[sha].wgsl);
        continue;
      }
      const entry = translate(name, path, original, tools);
      shaders[sha] = entry;
      writeFileSync(join(outDir, `${name}.wgsl`), entry.wgsl);
      console.log(`OK ${name} sha256=${sha.slice(0, 8)} bindings=${entry.bindings.length} push=${entry.push.mode}`);
    } catch (e) {
      failures.push(name);
      console.error(`FAIL ${name}: ${e.message}`);
    }
  }
  if (failures.length) {
    rmSync(manifestPath, { force: true });     // never leave a manifest that does not describe the current shaders
    console.error(`error: ${failures.length} of ${files.length} shader(s) failed (${failures.join(', ')}); manifest not written`);
    return 1;
  }

  const manifest = {
    version: MANIFEST_VERSION,
    generator: { glslc: glslcVersion(args.glslc), naga: nagaWasmVersion() },
    conventions: { combinedSamplerOffset: SAMPLER_BINDING_OFFSET, pushConstantGroup: PUSH_GROUP, pushConstantBinding: PUSH_BINDING },
    shaders: Object.fromEntries(Object.entries(shaders).sort(([, a], [, b]) => byName(a, b))),
  };
  writeFileSync(manifestPath, JSON.stringify(manifest, null, 2) + '\n');
  console.log(`wrote ${manifestPath} (${Object.keys(shaders).length} shader${Object.keys(shaders).length === 1 ? '' : 's'})`);

  if (!args['no-browser-check'] && Object.keys(shaders).length) {
    const r = spawnSync(process.execPath, [join(TOOLS_DIR, 'check-wgsl.mjs'), manifestPath], { stdio: 'inherit' });
    if (r.status !== 0) { console.error('error: browser check failed (pass --no-browser-check to skip it)'); return 1; }
  }
  return 0;
}

process.exitCode = main();
