// Usage: node src/browser-check.mjs  -> writes out/browser-check.html (self-contained; WGSL inlined)
// Open it in a WebGPU browser (or run run-browser-check.sh for headless Edge/Chrome) to let Tint (the real browser validator)
// compile every generated WGSL file. Result is printed into <pre id="out"> as JSON and POSTed to /result (see browser-check-server.mjs).
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
const files = [
  'out/wasm/mesh.vert.wgsl', 'out/wasm/mesh.frag.wgsl', 'out/wasm/mesh_combined.prepared.frag.wgsl',
  'out/patched/mesh.vert.wrapper-flip.wgsl', 'out/patched/mesh.slang.flip.wgsl',
  'out/wasm/mesh_push.vert.wgsl', 'out/patched/mesh_push.vert.immediates.wgsl', 'out/patched/mesh_push.vert.push-uniform.wgsl',
  'out/patched/mesh_push_array.push-uniform.wgsl', 'out/patched/mesh_push_array.push-storage.wgsl',
  'out/slang/mesh.slang.wgsl', 'out/slang26/mesh.slang.wgsl', 'out/slang/mesh.slang.via-naga.wgsl', 'out/slang26/mesh_push_portable.wgsl', 'out/slang/mesh_push_portable.wgsl',
  'out/slang26/probe_registers.wgsl',
  'out/probes/specconst_simple.frag.wgsl', 'out/probes/clipdist.vert.wgsl', 'out/probes/subgroup.comp.wgsl', 'out/probes/texarray_const.frag.wgsl',
  'out/probes/double.comp.wgsl', 'out/probes/int64.comp.wgsl', 'out/probes/storageimage.comp.wgsl', 'out/probes/atomics.comp.wgsl',
  'out/probes/sampler_types.frag.wgsl', 'out/probes/msaa_fetch.frag.wgsl', 'out/probes/uniform_layout.frag.wgsl', 'out/probes/builtins.frag.wgsl',
].filter(existsSync);
const data = Object.fromEntries(files.map((f) => [f, readFileSync(f, 'utf8')]));
// patched variants generated on the fly (module-scope directives the layer must add)
if (existsSync('out/probes/builtins.frag.wgsl')) data['PATCH diagnostic(off, derivative_uniformity) + builtins.frag'] = 'diagnostic(off, derivative_uniformity);\n' + readFileSync('out/probes/builtins.frag.wgsl', 'utf8');
if (existsSync('out/probes/subgroup.comp.wgsl')) data['PATCH enable subgroups + subgroup.comp'] = 'enable subgroups;\n' + readFileSync('out/probes/subgroup.comp.wgsl', 'utf8');
const mk = (f) => (existsSync(f) ? readFileSync(f, 'utf8') : '');
const LAYER = { meshVert: mk('out/wasm/mesh.vert.wgsl'), meshFragCombined: mk('out/wasm/mesh_combined.prepared.frag.wgsl'),
  pushVert: mk('out/patched/mesh_push.vert.push-uniform.wgsl'), pushFrag: mk('out/patched/mesh_push.prepared.frag.push-uniform.wgsl') };
const LAYOUT_PROBES = { hazard: { wgsl: mk('out/probes/std140_layout.comp.wgsl'), std140Expected: [4, 12, 20, 24, 27, 33] }, safe: { wgsl: mk('out/probes/std140_ok.comp.wgsl'), std140Expected: [1, 10, 22, 23, 29, 43] } };
const tri = Object.fromEntries(['naga-flip', 'noflip', 'wrapper-flip'].filter((k) => existsSync(`out/tri/tri.vert.${k}.wgsl`)).map((k) => [k, readFileSync(`out/tri/tri.vert.${k}.wgsl`, 'utf8')]));
const html = `<!doctype html><meta charset="utf-8"><title>wgsl-check</title><pre id="out">running...</pre>
<script type="module">
const SRC = ${JSON.stringify(data)};
const TRI = ${JSON.stringify(tri)};
const LAYER = ${JSON.stringify(LAYER)};
const LAYOUT_PROBES = ${JSON.stringify(LAYOUT_PROBES)};
const T = (p, ms = 8000, what = 'op') => Promise.race([p, new Promise((_, rej) => setTimeout(() => rej(new Error('timeout: ' + what)), ms))]);
const result = { userAgent: navigator.userAgent, hasGpu: !!navigator.gpu, shaders: {} };
try {
  result.wgslLanguageFeatures = navigator.gpu ? [...navigator.gpu.wgslLanguageFeatures] : [];
  const adapter = navigator.gpu && await navigator.gpu.requestAdapter();
  if (!adapter) throw new Error('no adapter');
  result.adapterInfo = { vendor: adapter.info?.vendor, architecture: adapter.info?.architecture, description: adapter.info?.description };
  const l = adapter.limits;
  result.limits = { maxBindGroups: l.maxBindGroups, maxBindingsPerBindGroup: l.maxBindingsPerBindGroup, maxImmediateSize: l.maxImmediateSize,
    maxSampledTexturesPerShaderStage: l.maxSampledTexturesPerShaderStage, maxSamplersPerShaderStage: l.maxSamplersPerShaderStage,
    maxStorageBuffersInVertexStage: l.maxStorageBuffersInVertexStage, maxUniformBufferBindingSize: l.maxUniformBufferBindingSize };
  result.features = [...adapter.features].filter((f) => /clip|subgroup|f16|bgra|indirect|dual|float32|depth/.test(f));
  const want = ['clip-distances', 'subgroups', 'shader-f16'].filter((f) => adapter.features.has(f));
  const device = await adapter.requestDevice({ requiredFeatures: want });
  result.deviceFeatures = want;
  for (const [name, code] of Object.entries(SRC)) {
    const m = device.createShaderModule({ code });
    const info = await T(m.getCompilationInfo(), 8000, 'getCompilationInfo ' + name);
    result.shaders[name] = info.messages.length ? info.messages.map((x) => x.type + ': ' + x.message.split('\\n')[0]) : 'OK';
  }
  // ---- pipeline layout with immediates + Y-flip / front-face render test (real Tint + real GPU) -------------------------
  result.pipelineLayout = {};
  for (const size of [64, 80]) {
    device.pushErrorScope('validation');
    try { device.createPipelineLayout({ bindGroupLayouts: [], immediateSize: size }); } catch (e) { result.pipelineLayout['immediateSize ' + size] = 'throws ' + e; }
    const err = await T(device.popErrorScope(), 8000, 'popErrorScope(layout)');
    result.pipelineLayout['immediateSize ' + size] ??= err ? 'validation error: ' + err.message.split('\\n')[0] : 'OK';
  }
  const FS = '@fragment fn fs() -> @location(0) vec4f { return vec4f(1, 0, 0, 1); }';
  const N = 16;
  result.render = {};
  for (const variant of Object.keys(TRI)) for (const frontFace of ['ccw', 'cw']) {
    const key = variant + ' / frontFace=' + frontFace + ' / cull=back';
    try {
      device.pushErrorScope('validation');
      const vs = device.createShaderModule({ code: TRI[variant] }), fs = device.createShaderModule({ code: FS });
      const pipeline = device.createRenderPipeline({ layout: 'auto',
        vertex: { module: vs, entryPoint: 'main' }, fragment: { module: fs, entryPoint: 'fs', targets: [{ format: 'rgba8unorm' }] },
        primitive: { topology: 'triangle-list', frontFace, cullMode: 'back' } });
      const tex = device.createTexture({ size: [N, N], format: 'rgba8unorm', usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC });
      const buf = device.createBuffer({ size: N * 256, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
      const enc = device.createCommandEncoder();
      const pass = enc.beginRenderPass({ colorAttachments: [{ view: tex.createView(), clearValue: [0, 0, 0, 1], loadOp: 'clear', storeOp: 'store' }] });
      pass.setPipeline(pipeline); pass.draw(3); pass.end();
      enc.copyTextureToBuffer({ texture: tex }, { buffer: buf, bytesPerRow: 256 }, [N, N]);
      device.queue.submit([enc.finish()]);
      await T(buf.mapAsync(GPUMapMode.READ), 8000, 'mapAsync ' + key);
      const px = new Uint8Array(buf.getMappedRange());
      const rows = []; let red = 0;
      for (let y = 0; y < N; y++) { let r = ''; for (let x = 0; x < N; x++) { const on = px[y * 256 + x * 4] > 128; r += on ? '#' : '.'; red += on; } rows.push(r); }
      const err = await T(device.popErrorScope(), 8000, 'popErrorScope(render)');
      result.render[key] = { redPixels: red, error: err ? err.message : undefined, image: rows };
    } catch (e) { result.render[key] = { error: String(e) }; }
  }


  // ---- the Vulkan-layer conventions in a real pipeline: combined sampler = texture@b + sampler@(b+500); push constants = uniform@group3/binding0
  result.layerPipelines = {};
  const V = GPUShaderStage.VERTEX, F = GPUShaderStage.FRAGMENT;
  const targets = [{ format: 'rgba8unorm' }];
  const tryPipe = async (name, fn) => { device.pushErrorScope('validation'); let e0; try { fn(); } catch (e) { e0 = String(e); }
    const err = await T(device.popErrorScope(), 8000, name); result.layerPipelines[name] = e0 ?? (err ? err.message.split('\\n')[0] : 'OK'); };
  await tryPipe('A: set0{ubo@0, texture@1, sampler@501} mesh.vert + split-combined frag', () => {
    const bgl0 = device.createBindGroupLayout({ entries: [
      { binding: 0, visibility: V | F, buffer: { type: 'uniform' } },
      { binding: 1, visibility: F, texture: { sampleType: 'float' } },
      { binding: 501, visibility: F, sampler: { type: 'filtering' } } ] });
    device.createRenderPipeline({ layout: device.createPipelineLayout({ bindGroupLayouts: [bgl0] }),
      vertex: { module: device.createShaderModule({ code: LAYER.meshVert }), entryPoint: 'main',
        buffers: [{ arrayStride: 12, attributes: [{ shaderLocation: 0, offset: 0, format: 'float32x3' }] }, { arrayStride: 12, attributes: [{ shaderLocation: 1, offset: 0, format: 'float32x3' }] }, { arrayStride: 8, attributes: [{ shaderLocation: 2, offset: 0, format: 'float32x2' }] }] },
      fragment: { module: device.createShaderModule({ code: LAYER.meshFragCombined }), entryPoint: 'main', targets } });
  });
  await tryPipe('B: push constants emulated as uniform@group3/binding0 (sets 1,2 empty)', () => {
    const bgl0 = device.createBindGroupLayout({ entries: [
      { binding: 0, visibility: V | F, buffer: { type: 'uniform' } },
      { binding: 1, visibility: F, texture: { sampleType: 'float' } },
      { binding: 501, visibility: F, sampler: { type: 'filtering' } } ] });
    const empty = device.createBindGroupLayout({ entries: [] });
    const bgl3 = device.createBindGroupLayout({ entries: [{ binding: 0, visibility: V | F, buffer: { type: 'uniform' } }] });
    device.createRenderPipeline({ layout: device.createPipelineLayout({ bindGroupLayouts: [bgl0, empty, empty, bgl3] }),
      vertex: { module: device.createShaderModule({ code: LAYER.pushVert }), entryPoint: 'main',
        buffers: [{ arrayStride: 12, attributes: [{ shaderLocation: 0, offset: 0, format: 'float32x3' }] }, { arrayStride: 12, attributes: [{ shaderLocation: 1, offset: 0, format: 'float32x3' }] }, { arrayStride: 8, attributes: [{ shaderLocation: 2, offset: 0, format: 'float32x2' }] }] },
      fragment: { module: device.createShaderModule({ code: LAYER.pushFrag }), entryPoint: 'main', targets } });
  });

  // ---- std140 UBO layout: which byte offsets does the translated shader really read? (slot i holds the value i) ----------------
  result.std140Probe = {};
  for (const [name, probe] of Object.entries(LAYOUT_PROBES)) {
    try {
      if (!probe.wgsl) { result.std140Probe[name] = { error: 'missing wgsl' }; continue; }
      device.pushErrorScope('validation');
      const mod = device.createShaderModule({ code: probe.wgsl });
      const pipe = device.createComputePipeline({ layout: 'auto', compute: { module: mod, entryPoint: 'main' } });
      const ubo = device.createBuffer({ size: 256, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
      device.queue.writeBuffer(ubo, 0, new Float32Array(Array.from({ length: 64 }, (_, i) => i)));
      const outb = device.createBuffer({ size: 64, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC });
      const rb = device.createBuffer({ size: 64, usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST });
      const bg = device.createBindGroup({ layout: pipe.getBindGroupLayout(0), entries: [{ binding: 0, resource: { buffer: ubo } }, { binding: 1, resource: { buffer: outb } }] });
      const enc = device.createCommandEncoder(); const p = enc.beginComputePass(); p.setPipeline(pipe); p.setBindGroup(0, bg); p.dispatchWorkgroups(1); p.end();
      enc.copyBufferToBuffer(outb, 0, rb, 0, 64); device.queue.submit([enc.finish()]);
      await T(rb.mapAsync(GPUMapMode.READ), 8000, 'mapAsync std140 ' + name);
      const observed = [...new Float32Array(rb.getMappedRange()).slice(0, 6)];
      const err = await T(device.popErrorScope(), 8000, 'popErrorScope(std140)');
      result.std140Probe[name] = { observed, std140Expected: probe.std140Expected, matchesStd140: JSON.stringify(observed) === JSON.stringify(probe.std140Expected), validationError: err ? err.message.split('\\n')[0] : undefined };
    } catch (e) { result.std140Probe[name] = { error: String(e) }; }
  }
} catch (e) { result.error = String(e); }

const text = JSON.stringify(result, null, 1);
document.getElementById('out').textContent = text;
try { await fetch('/result', { method: 'POST', body: text }); } catch {}
</script>`;
writeFileSync('out/browser-check.html', html);
console.log(`wrote out/browser-check.html (${files.length} WGSL files inlined)`);
