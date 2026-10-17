// Assertions on the manifest produced for the valid fixtures:  node assert-manifest.mjs <manifest.json> <dir with the .spv files>
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFileSync, readdirSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';

const [manifestPath, spvDir] = process.argv.slice(2);
const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
const outDir = dirname(manifestPath);

// SHA-256 of a file the way a human would check it: shasum (macOS / Linux perl) or sha256sum.
function shasum(file) {
  for (const [cmd, args] of [['shasum', ['-a', '256']], ['sha256sum', []]]) {
    try { return execFileSync(cmd, [...args, file], { encoding: 'utf8' }).split(/\s+/)[0]; } catch (e) { if (e.code !== 'ENOENT') throw e; }
  }
  throw new Error('neither shasum nor sha256sum is available');
}

// ---- top level (docs/architecture.md section 6) ------------------------------------------------------------------
assert.equal(manifest.version, 1);
assert.deepEqual(Object.keys(manifest), ['version', 'generator', 'conventions', 'shaders']);
assert.match(manifest.generator.glslc, /^shaderc /, 'generator.glslc is the first line of `glslc --version`');
const nagaPkg = JSON.parse(readFileSync(new URL('../node_modules/naga-wasm/package.json', import.meta.url), 'utf8'));
assert.equal(manifest.generator.naga, nagaPkg.version);
assert.deepEqual(manifest.conventions, { combinedSamplerOffset: 500, pushConstantGroup: 3, pushConstantBinding: 0 });

// ---- keys are the lowercase sha256 of the exact .spv bytes -----------------------------------------------------------
const spvFiles = readdirSync(spvDir).filter((f) => f.endsWith('.spv'));
assert.equal(Object.keys(manifest.shaders).length, spvFiles.length, 'one manifest entry per .spv');
const byName = {};
for (const f of spvFiles) {
  const key = shasum(join(spvDir, f));
  assert.match(key, /^[0-9a-f]{64}$/);
  const entry = manifest.shaders[key];
  assert.ok(entry, `manifest has no entry keyed by shasum of ${f} (${key})`);
  assert.equal(entry.name, basename(f, '.spv'));
  byName[entry.name] = entry;
}

// ---- every entry has the documented shape and a consistent WGSL --------------------------------------------------------
const KINDS = new Set(['uniform', 'storage', 'texture', 'sampler', 'storageTexture']);
for (const [name, e] of Object.entries(byName)) {
  assert.deepEqual(Object.keys(e), ['name', 'stage', 'entryPoint', 'wgsl', 'push', 'bindings', 'overrides'], `${name}: field order`);
  assert.ok(['vertex', 'fragment', 'compute'].includes(e.stage), `${name}: stage`);
  assert.equal(e.entryPoint, 'main');
  assert.ok(['none', 'uniform', 'storage'].includes(e.push.mode), `${name}: push.mode ${e.push.mode}`);
  assert.equal(typeof e.push.size, 'number');
  assert.equal(e.push.mode === 'none', e.push.size === 0);
  for (const b of e.bindings) { assert.ok(KINDS.has(b.kind), `${name}: kind ${b.kind}`); assert.deepEqual(Object.keys(b), ['group', 'binding', 'kind', 'name']); }
  assert.equal(readFileSync(join(outDir, `${name}.wgsl`), 'utf8'), e.wgsl, `${name}.wgsl equals the inline text`);
  assert.match(e.wgsl, /^diagnostic\(off, derivative_uniformity\);/, `${name}: derivative_uniformity diagnostic is off`);
  assert.doesNotMatch(e.wgsl, /@coherent|var<immediate>|var<push_constant>|chromium_experimental_push_constant/, `${name}: native-only constructs are gone`);
  assert.match(e.wgsl, new RegExp(`@${e.stage}\\s[^{]*fn ${e.entryPoint}\\(`), `${name}: entry point is in the WGSL`);
}
const slots = (e) => e.bindings.map((b) => `${b.group}:${b.binding}:${b.kind}`);

// ---- the HelloMesh contract ------------------------------------------------------------------------------------------
assert.equal(byName['mesh.vert'].stage, 'vertex');
assert.deepEqual(slots(byName['mesh.vert']), ['0:0:uniform']);
assert.deepEqual(byName['mesh.vert'].push, { mode: 'none', size: 0 });
assert.match(byName['mesh.vert'].wgsl, /\.y = -\(/, 'mesh.vert: naga negated position.y (Y flip is on)');
assert.match(byName['mesh.vert'].wgsl, /@location\(0\) param: vec3<f32>, @location\(1\) param_1: vec3<f32>, @location\(2\) param_2: vec2<f32>/, 'mesh.vert: vertex inputs at locations 0/1/2');

assert.equal(byName['mesh.frag'].stage, 'fragment');
assert.deepEqual(slots(byName['mesh.frag']), ['0:0:uniform', '0:1:texture', '0:2:sampler'], 'separate texture@1 + sampler@2 are left alone');
assert.match(byName['mesh.frag'].wgsl, /-> @location\(0\) vec4<f32>/);

// combined sampler: texture keeps binding 1, sampler moves to 1 + 500
assert.deepEqual(slots(byName['combined.frag']), ['0:0:uniform', '0:1:texture', '0:501:sampler']);

// push constants: 64 bytes -> uniform buffer at group 3 binding 0
assert.deepEqual(byName['push.vert'].push, { mode: 'uniform', size: 64 });
assert.deepEqual(slots(byName['push.vert']), ['3:0:uniform']);
assert.match(byName['push.vert'].wgsl, /@group\(3\) @binding\(0\) var<uniform> /);
// std430-only block (float[4]) -> storage buffer at the same slot
assert.deepEqual(byName['push_array.vert'].push, { mode: 'storage', size: 48 });
assert.deepEqual(slots(byName['push_array.vert']), ['3:0:storage']);
assert.match(byName['push_array.vert'].wgsl, /@group\(3\) @binding\(0\) var<storage, read> /);

// write-only SSBO was made readable, compute stage
assert.equal(byName['writeonly.comp'].stage, 'compute');
assert.deepEqual(slots(byName['writeonly.comp']), ['0:0:storage', '0:1:uniform']);
assert.match(byName['writeonly.comp'].wgsl, /var<storage, read_write>/);

// specialization constant -> override keyed by its SpecId
assert.deepEqual(Object.keys(byName['spec.frag'].overrides), ['7']);
assert.equal(byName['spec.frag'].overrides['7'].type, 'f32');
assert.match(byName['spec.frag'].wgsl, /@id\(7\) override /);

console.log(`manifest OK: ${spvFiles.length} shaders, keys == shasum of the .spv files`);
