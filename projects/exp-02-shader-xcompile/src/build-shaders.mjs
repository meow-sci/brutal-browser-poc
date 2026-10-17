// Prototype of the build-time pipeline (strategy A):
//   node src/build-shaders.mjs [shaderDir=shaders] [outDir=out/web-shaders]
// For every top-level *.vert|*.frag|*.comp:  glslc -> spirv-val -> spv-prepare -> layout lint -> naga-wasm -> patch-wgsl -> manifest.
// Emits <outDir>/<name>.spv (the ORIGINAL glslc output the app will pass to vkCreateShaderModule), <name>.wgsl and manifest.json keyed by
// sha256(original spv bytes).
import { readdirSync, readFileSync, writeFileSync, mkdirSync, mkdtempSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { join, basename, extname } from 'node:path';
import { tmpdir } from 'node:os';
import { parseSpirv, validate, writeWgsl, parseWgsl, NagaError } from 'naga-wasm';
import { patchDirectives, patchPush, reflectBindings } from './patch-wgsl.mjs';

const [shaderDir = 'shaders', outDir = 'out/web-shaders'] = process.argv.slice(2);
const ENV = process.env.VK_ENV ?? 'vulkan1.1';
mkdirSync(outDir, { recursive: true });
const tmp = mkdtempSync(join(tmpdir(), 'build-shaders-'));
const run = (cmd, args) => { const r = spawnSync(cmd, args, { encoding: 'utf8' }); if (r.status !== 0) throw new Error(`${cmd} ${args.join(' ')}\n${r.stderr}`); return r.stdout; };
const stageOf = { '.vert': 'vertex', '.frag': 'fragment', '.comp': 'compute' };
const wgslOk = (code) => { try { using m = parseWgsl(code); using _i = validate(m); return true; } catch (e) { if (e instanceof NagaError) return false; throw e; } };

const manifest = { generator: 'exp-02 build-shaders.mjs', vulkanEnv: ENV, samplerBindingOffset: 500, pushGroup: 3, shaders: {} };
for (const f of readdirSync(shaderDir).filter((x) => stageOf[extname(x)]).sort()) {
  const name = basename(f), src = join(shaderDir, f);
  try {
    const spvPath = join(outDir, name + '.spv');
    run('glslc', [`--target-env=${ENV}`, '-O', '-I', shaderDir, src, '-o', spvPath]);
    run('spirv-val', [`--target-env`, ENV, spvPath]);
    const orig = readFileSync(spvPath), sha = createHash('sha256').update(orig).digest('hex');
    const prepared = join(tmp, name + '.prepared.spv');
    run('node', ['src/spv-prepare.mjs', spvPath, prepared]);
    const lint = spawnSync('node', ['src/spv-layout-lint.mjs', prepared], { encoding: 'utf8' });
    if (lint.status === 3) throw new Error('layout hazard: ' + lint.stdout.trim());
    using module = parseSpirv(new Uint8Array(readFileSync(prepared)));
    using info = validate(module);
    let wgsl = writeWgsl(module, info);
    let push = null;
    if (/var<(immediate|push_constant)>/.test(wgsl)) {
      for (const mode of ['uniform', 'storage']) {        // pick the first mode whose result validates (uniform rules reject std430 packing)
        const r = patchPush(wgsl, { mode, group: 3, binding: 0 });
        if (wgslOk(r.code)) { push = { mode, group: 3, binding: 0 }; wgsl = r.code; break; }
      }
      if (!push) throw new Error('push constants fit neither uniform nor storage');
    }
    wgsl = patchDirectives(wgsl).code;
    if (!wgslOk(wgsl)) throw new Error('final WGSL does not validate');
    writeFileSync(join(outDir, name + '.wgsl'), wgsl);
    manifest.shaders[sha] = {
      source: src, spv: name + '.spv', wgsl: name + '.wgsl', stage: stageOf[extname(f)],
      entryPoints: [...wgsl.matchAll(/@(vertex|fragment|compute)[^{]*?fn\s+(\w+)/g)].map((m) => ({ name: m[2], stage: m[1] })),
      bindings: reflectBindings(wgsl), push,
      overrides: [...wgsl.matchAll(/@id\((\d+)\)\s*override\s+\w+\s*:\s*(\w+)/g)].map((m) => ({ id: +m[1], type: m[2] })),
      yFlip: 'naga-spirv-frontend',
    };
    console.log(`OK   ${name}  sha256=${sha.slice(0, 12)}...  bindings=${manifest.shaders[sha].bindings.length} push=${push ? push.mode : 'none'}`);
  } catch (e) { console.log(`FAIL ${name}: ${String(e.message).split('\n').slice(0, 3).join(' | ')}`); }
}
writeFileSync(join(outDir, 'manifest.json'), JSON.stringify(manifest, null, 2));
console.log(`wrote ${join(outDir, 'manifest.json')} (${Object.keys(manifest.shaders).length} shaders)`);
