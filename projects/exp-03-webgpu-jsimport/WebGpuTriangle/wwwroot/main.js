// exp-03: C# -> WebGPU via [JSImport]; spinning triangle + interop benchmarks.
// Console markers consumed by test/run.mjs:
//   FRAMES DONE, PIXEL CHECK PASS|FAIL, GATE WAITING (only with ?gate), STREAM FRAMES DONE,
//   STREAM PIXEL CHECK PASS|FAIL, BENCH ..., BENCH_JSON {...}, ALL DONE, FATAL ...
import { dotnet } from './_framework/dotnet.js';
import { createImports, initGlue, get, streamStats, liveHandleCount } from './webgpu-glue.js';

const statusEl = document.getElementById('status');
const log = (...a) => { const s = a.join(' '); console.log(s); statusEl.textContent += s + '\n'; };
const params = new URLSearchParams(location.search);
const FRAMES = Number(params.get('frames') ?? 120);
const results = { env: {}, frames: {}, pixel: {}, bench: {} };
window.__e3 = results;

const median = (a) => { const s = [...a].sort((x, y) => x - y); const m = s.length >> 1; return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2; };
const mean = (a) => a.reduce((x, y) => x + y, 0) / a.length;
const r3 = (x) => Math.round(x * 1000) / 1000;
const stats = (a) => ({ n: a.length, median: r3(median(a)), mean: r3(mean(a)), min: r3(Math.min(...a)), max: r3(Math.max(...a)) });
const nextFrame = () => new Promise(r => requestAnimationFrame(r));

try {
  // ------------------------------------------------------------------ 1. WebGPU before .NET
  if (!navigator.gpu) throw new Error('navigator.gpu is undefined');
  const adapter = await navigator.gpu.requestAdapter();
  if (!adapter) throw new Error('WEBGPU UNAVAILABLE: requestAdapter() returned null');
  const info = adapter.info ?? (adapter.requestAdapterInfo ? await adapter.requestAdapterInfo() : {});
  const device = await adapter.requestDevice();
  device.addEventListener('uncapturederror', (e) => console.error('WEBGPU ERROR', e.error?.message));
  device.lost.then((i) => console.error('WEBGPU DEVICE LOST', i.reason, i.message));
  const canvas = document.getElementById('gpu');
  const context = canvas.getContext('webgpu');
  const format = navigator.gpu.getPreferredCanvasFormat();
  context.configure({
    device, format, alphaMode: 'opaque',
    usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC, // COPY_SRC for pixel readback
  });
  const gpu = { adapter, device, queue: device.queue, context, format };

  let a = performance.now(), b; do { b = performance.now(); } while (b === a);
  results.env = {
    userAgent: navigator.userAgent, crossOriginIsolated: self.crossOriginIsolated, perfNowResolutionMs: r3(b - a),
    adapter: { vendor: info.vendor, architecture: info.architecture, device: info.device, description: info.description, isFallbackAdapter: info.isFallbackAdapter },
    preferredFormat: format,
  };
  log('ENV', JSON.stringify(results.env));

  // ------------------------------------------------------------------ 2. start .NET
  const tStart = performance.now();
  let builder = dotnet;
  if (params.has('nojiterp')) {
    // interpreter only: disable the jiterpreter (trace JIT, interp-entry and jit-call wrappers)
    builder = builder.withRuntimeOptions(['--no-jiterpreter-traces-enabled', '--no-jiterpreter-interp-entry-enabled', '--no-jiterpreter-jit-call-enabled']);
  }
  results.env.jiterpreter = params.has('nojiterp') ? 'disabled via withRuntimeOptions' : 'default (enabled)';
  const runtime = await builder.create();
  const { setModuleImports, getAssemblyExports, getConfig, runMain } = runtime;
  initGlue(runtime, gpu);
  setModuleImports('webgpu', createImports());
  const App = (await getAssemblyExports(getConfig().mainAssemblyName)).App;
  await runMain();
  results.env.dotnetStartMs = r3(performance.now() - tStart);
  results.env.runtimeBuildInfo = runtime.runtimeBuildInfo;
  try {
    const o = runtime.INTERNAL?.jiterpreter_get_options?.();
    if (o) results.env.jiterpreterOptions = { enableTraces: o.enableTraces, enableInterpEntry: o.enableInterpEntry, enableJitCall: o.enableJitCall };
  } catch (e) { results.env.jiterpreterOptions = 'unavailable: ' + e; }
  log('JITERPRETER', JSON.stringify(results.env.jiterpreterOptions));
  log('DOTNET', JSON.stringify({ startMs: results.env.dotnetStartMs, ...runtime.runtimeBuildInfo }));

  // heap accessor probe
  const heap = {
    localHeapViewU8: typeof runtime.localHeapViewU8,
    getHeapU8: typeof runtime.getHeapU8,
    Module: typeof runtime.Module,
    Module_HEAPU8: typeof runtime.Module?.HEAPU8,
    Module_HEAPU8_isSameObjectAsLocalHeapViewU8: runtime.Module?.HEAPU8 === runtime.localHeapViewU8(),
    localHeapViewU8_twice_sameObject: runtime.localHeapViewU8() === runtime.localHeapViewU8(),
    heapBytesBefore: runtime.localHeapViewU8().byteLength,
  };

  log('INIT', App.Init());

  // memory-growth hazard: a cached view is detached after memory.grow
  const stale = runtime.localHeapViewU8();
  const staleLenBefore = stale.byteLength;
  App.GrowHeap(256);
  const fresh = runtime.localHeapViewU8();
  Object.assign(heap, { staleViewByteLengthBeforeGrow: staleLenBefore, staleViewByteLengthAfterGrow: stale.byteLength, freshViewByteLength: fresh.byteLength, staleIsFresh: stale === fresh });
  results.env.heap = heap;
  log('HEAP', JSON.stringify(heap));

  // queue.writeBuffer on a buffer that is still mapped (mappedAtCreation) -> validation error?
  device.pushErrorScope('validation');
  const mb = device.createBuffer({ size: 16, usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST, mappedAtCreation: true });
  device.queue.writeBuffer(mb, 0, new Uint8Array(16));
  const mappedErr = await device.popErrorScope();
  mb.destroy();
  results.env.writeBufferOnMappedBuffer = mappedErr ? mappedErr.message.split('\n')[0] : 'no error';
  log('MAPPED WRITE CHECK', results.env.writeBufferOnMappedBuffer);

  // ------------------------------------------------------------------ 3. frames
  async function runFrames(n, fn) {
    const cpu = [], stamps = [];
    for (let k = 0; k < n; k++) {
      const t = await nextFrame();
      stamps.push(t);
      const c0 = performance.now();
      fn(t);
      cpu.push(performance.now() - c0);
    }
    const intervals = stamps.slice(1).map((t, i) => t - stamps[i]);
    return { frames: n, cpuMs: stats(cpu), rafIntervalMs: stats(intervals) };
  }

  results.frames.individual = await runFrames(FRAMES, (t) => App.Frame(t));
  log('FRAMES DONE', JSON.stringify(results.frames.individual));

  // ------------------------------------------------------------------ 4. pixel checks
  const W = canvas.width, H = canvas.height;
  const pts = { center: [W >> 1, H >> 1], corner: [4, 4] };
  const expect = { center: [85, 85, 85], corner: [Math.round(0.1 * 255), Math.round(0.2 * 255), Math.round(0.3 * 255)] };
  const near = (p, e, tol) => p.every((v, i) => i > 2 || Math.abs(v - e[i]) <= tol);

  async function readbackCurrentTexture() {
    const tex = context.getCurrentTexture(); // same texture C# just rendered into (same task, not yet presented)
    const bpr = Math.ceil((tex.width * 4) / 256) * 256;
    const buf = device.createBuffer({ size: bpr * tex.height, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
    const enc = device.createCommandEncoder();
    enc.copyTextureToBuffer({ texture: tex }, { buffer: buf, bytesPerRow: bpr }, [tex.width, tex.height]);
    device.queue.submit([enc.finish()]);
    await buf.mapAsync(GPUMapMode.READ);
    const data = new Uint8Array(buf.getMappedRange()).slice();
    buf.unmap(); buf.destroy();
    const bgra = tex.format.startsWith('bgra');
    const px = ([x, y]) => { const o = y * bpr + x * 4; return bgra ? [data[o + 2], data[o + 1], data[o], data[o + 3]] : [data[o], data[o + 1], data[o + 2], data[o + 3]]; };
    return { center: px(pts.center), corner: px(pts.corner), format: tex.format };
  }

  async function decodeDataUrl(url) {
    const img = new Image();
    img.src = url;
    await img.decode();
    const c = new OffscreenCanvas(img.width, img.height);
    const g = c.getContext('2d');
    g.drawImage(img, 0, 0);
    const px = ([x, y]) => [...g.getImageData(x, y, 1, 1).data];
    return { center: px(pts.center), corner: px(pts.corner), w: img.width, h: img.height };
  }

  async function pixelCheck(label, renderFn) {
    renderFn(1234);
    const urlSameTask = canvas.toDataURL('image/png'); // same task as rendering, before presentation
    const gpuPx = await readbackCurrentTexture();
    await nextFrame(); await nextFrame();
    const urlLater = canvas.toDataURL('image/png');   // after the frame was presented
    const sameTask = await decodeDataUrl(urlSameTask);
    const later = await decodeDataUrl(urlLater);
    const pass = near(gpuPx.center, expect.center, 10) && near(gpuPx.corner, expect.corner, 3);
    const r = {
      pass, expect, gpuReadback: gpuPx,
      toDataURL_sameTask: { ...sameTask, matches: near(sameTask.center, expect.center, 10) && near(sameTask.corner, expect.corner, 3) },
      toDataURL_afterPresent: { ...later, matches: near(later.center, expect.center, 10) && near(later.corner, expect.corner, 3) },
    };
    log(`${label} ${pass ? 'PASS' : 'FAIL'}`, JSON.stringify(r));
    return r;
  }

  results.pixel.individual = await pixelCheck('PIXEL CHECK', (t) => App.Frame(t));

  if (params.has('gate')) {
    // keep the canvas stable while the test driver takes its screenshot
    await new Promise((resolve) => { window.__releaseGate = resolve; log('GATE WAITING'); });
  }

  results.frames.stream = await runFrames(FRAMES, (t) => App.FrameStream(t));
  log('STREAM FRAMES DONE', JSON.stringify(results.frames.stream));
  results.pixel.stream = await pixelCheck('STREAM PIXEL CHECK', (t) => App.FrameStream(t));

  // ------------------------------------------------------------------ 5. benchmarks
  const N = 10000, RUNS = 7;
  const B = results.bench;
  function benchCs(name, fn, n = N) {
    fn(n); // warm-up (interpreter / jiterpreter trace compilation)
    const cs = [], wall = [];
    for (let r = 0; r < RUNS; r++) {
      const w0 = performance.now();
      cs.push(fn(n));
      wall.push(performance.now() - w0);
    }
    const res = { calls: n, runs: RUNS, csMs: stats(cs), wallMs: stats(wall), nsPerCall: r3((median(cs) * 1e6) / n) };
    B[name] = res;
    log('BENCH', name, `${res.nsPerCall} ns/call`, JSON.stringify(res));
    return res;
  }

  // (a) empty calls
  benchCs('a0_managedCall_baseline', (n) => App.BenchManagedCall(n));
  benchCs('a1_jsimport_noop_int', (n) => App.BenchNoop(n));
  benchCs('a2_jsimport_noop_5ints', (n) => App.BenchNoop5(n));
  benchCs('a3_jsimport_noop_JSObject', (n) => App.BenchNoopObj(n));
  benchCs('a5_jsimport_return_same_JSObject', (n) => App.BenchReturnObj(n, false));
  benchCs('a6_jsimport_return_new_JSObject_disposed', (n) => App.BenchReturnObj(n, true));
  benchCs('a4_jsexport_echo_JS_to_CS', (n) => { const t = performance.now(); let s = 0; for (let i = 0; i < n; i++) s += App.Echo(i); return performance.now() - t; });

  // (b) writeBuffer 64 bytes
  benchCs('b1_writeBuffer64_ptr', (n) => App.BenchWriteBuffer(n));
  benchCs('b2_writeBuffer64_span_memoryview', (n) => App.BenchWriteBufferSpan(n));
  {
    const jb = device.createBuffer({ size: 64, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
    const src = new Float32Array(16);
    benchCs('b0_writeBuffer64_pureJS_baseline', (n) => { const t = performance.now(); for (let i = 0; i < n; i++) device.queue.writeBuffer(jb, 0, src); return performance.now() - t; });
    benchCs('b3_writeBuffer64_pureJS_fromHeapView', (n) => { const t = performance.now(); for (let i = 0; i < n; i++) device.queue.writeBuffer(jb, 0, runtime.localHeapViewU8(), 4096, 64); return performance.now() - t; });
    await device.queue.onSubmittedWorkDone();
    jb.destroy();
  }

  // (c) 1000 draws: individual JSImport calls vs one replayCommandStream vs pure JS
  const DRAWS = 1000, CFRAMES = 30;
  const [hVbo, , , hPipeDyn, , hBgMany] = App.GetHandles().split(',').map(Number);
  async function manyDraws(name, fn) {
    for (let k = 0; k < 5; k++) { await nextFrame(); fn(); } // warm-up
    const wall = [], cs = [], enc = [], replay = [];
    for (let k = 0; k < CFRAMES; k++) {
      await nextFrame();
      const w0 = performance.now();
      const c = fn();
      wall.push(performance.now() - w0);
      if (typeof c === 'number') cs.push(c);
      if (name.includes('stream')) { enc.push(App.LastStreamEncodeMs()); replay.push(streamStats.lastReplayMs); }
    }
    await device.queue.onSubmittedWorkDone();
    const res = { draws: DRAWS, frames: CFRAMES, wallMsPerFrame: stats(wall) };
    if (cs.length) res.csMsPerFrame = stats(cs);
    if (enc.length) { res.csEncodeMs = stats(enc); res.jsReplayMs = stats(replay); }
    B[name] = res;
    log('BENCH', name, `${res.wallMsPerFrame.median} ms/frame`, JSON.stringify(res));
  }
  await manyDraws('c1_1000draws_individual_jsimport', () => App.FrameManyDraws(DRAWS, false));
  await manyDraws('c2_1000draws_command_stream', () => App.FrameManyDraws(DRAWS, true));
  {
    const vbo = get(hVbo), pipe = get(hPipeDyn), bg = get(hBgMany);
    const dyn = new Uint32Array(1);
    await manyDraws('c0_1000draws_pureJS_baseline', () => {
      const e = device.createCommandEncoder();
      const p = e.beginRenderPass({ colorAttachments: [{ view: context.getCurrentTexture().createView(), clearValue: [0.1, 0.2, 0.3, 1], loadOp: 'clear', storeOp: 'store' }] });
      p.setPipeline(pipe); p.setVertexBuffer(0, vbo);
      for (let i = 0; i < DRAWS; i++) { dyn[0] = i * 256; p.setBindGroup(0, bg, dyn, 0, 1); p.draw(3, 1, 0, 0); }
      p.end(); device.queue.submit([e.finish()]);
    });
  }
  // leave the many-draws grid on screen for the final screenshot
  App.FrameManyDraws(DRAWS, true);
  await nextFrame();

  // (d) render pipeline creation cost
  // Every shader gets a unique integer constant K = NONCE*2000 + slot so that neither Dawn's dedup, Chrome's
  // in-memory blob cache nor the macOS Metal compiler cache (keyed on MSL text) can serve it ("cold").
  // d1w deliberately repeats d1 run 1's shaders after they were released ("warm").
  const NONCE = 1 + Math.floor(Math.random() * 7998);
  const kOf = (slot) => NONCE * 2000 + slot;
  results.env.pipelineNonce = NONCE;
  const jsWgsl = (k) => `const K: f32 = ${k}.0;
struct U { m: mat4x4f };
@group(0) @binding(0) var<uniform> u: U;
struct VOut { @builtin(position) pos: vec4f, @location(0) color: vec3f };
@vertex fn vs(@location(0) p: vec2f, @location(1) c: vec3f) -> VOut { var o: VOut; o.pos = u.m * vec4f(p, 0.0, 1.0); o.color = c; return o; }
@fragment fn fs(i: VOut) -> @location(0) vec4f { return vec4f(i.color * K, 1.0); }`;
  const jsPipelineDesc = (module) => ({
    layout: 'auto',
    vertex: { module, entryPoint: 'vs', buffers: [{ arrayStride: 20, attributes: [{ format: 'float32x2', offset: 0, shaderLocation: 0 }, { format: 'float32x3', offset: 8, shaderLocation: 1 }] }] },
    fragment: { module, entryPoint: 'fs', targets: [{ format }] },
  });
  async function pipelines(name, n, distinct, kBase) {
    await device.queue.onSubmittedWorkDone();
    const t0 = performance.now();
    const [shaderMs, pipeMs, jsonMs] = App.BenchPipelines(n, distinct, kBase).split(';').map(Number);
    const tCall = performance.now() - t0;
    await device.queue.onSubmittedWorkDone(); // the GPU process has executed every create* by now
    const tDone = performance.now() - t0;
    const res = { n, distinctShaders: distinct, csShaderModuleMs: shaderMs, csJsonBuildMs: jsonMs, csCreatePipelineSyncMs: pipeMs, callWallMs: r3(tCall), untilQueueIdleMs: r3(tDone), msPerPipelineSync: r3(pipeMs / n), msPerPipelineUntilIdle: r3(tDone / n) };
    (B[name] ??= []).push(res);
    log('BENCH', name, JSON.stringify(res));
  }
  await pipelines('d0_warmup_10', 10, true, kOf(0));
  for (let s = 1; s <= 3; s++) await pipelines('d1_100pipelines_cs_sync_cold', 100, true, kOf(100 * s));
  await pipelines('d1w_100pipelines_cs_sync_warm_repeat', 100, true, kOf(100));
  for (let s = 1; s <= 3; s++) await pipelines('d2_100pipelines_cs_sync_sameShader_dedup', 100, false, 0);
  {
    // C#-driven createRenderPipelineAsync: Task<int> per pipeline, Task.WhenAll in C#
    const runs = [];
    for (let s = 1; s <= 3; s++) {
      await device.queue.onSubmittedWorkDone();
      const t0 = performance.now();
      const [shaderMs, issueMs, doneMs] = (await App.BenchPipelinesAsync(100, kOf(300 + 100 * s))).split(';').map(Number);
      runs.push({ csShaderModuleMs: shaderMs, csIssueMs: issueMs, csAllResolvedMs: doneMs, jsWallMs: r3(performance.now() - t0) });
    }
    B.d4_100pipelines_cs_async_cold = runs;
    log('BENCH d4_100pipelines_cs_async_cold', JSON.stringify(runs));
  }
  {
    // pure-JS createRenderPipelineAsync x100 (distinct shaders)
    const runs = [];
    for (let s = 1; s <= 3; s++) {
      await device.queue.onSubmittedWorkDone();
      const t0 = performance.now();
      const ps = [];
      for (let i = 0; i < 100; i++) ps.push(device.createRenderPipelineAsync(jsPipelineDesc(device.createShaderModule({ code: jsWgsl(kOf(600 + 100 * s + i)) }))));
      const tIssue = performance.now() - t0;
      await Promise.all(ps);
      runs.push({ issueMs: r3(tIssue), allResolvedMs: r3(performance.now() - t0) });
    }
    B.d3_100pipelines_pureJS_async_cold = runs;
    log('BENCH d3_100pipelines_pureJS_async_cold', JSON.stringify(runs));
  }
  {
    // pure-JS sync createRenderPipeline x100 (distinct shaders) + wait for queue idle
    const runs = [];
    for (let s = 1; s <= 3; s++) {
      await device.queue.onSubmittedWorkDone();
      const t0 = performance.now();
      for (let i = 0; i < 100; i++) device.createRenderPipeline(jsPipelineDesc(device.createShaderModule({ code: jsWgsl(kOf(900 + 100 * s + i)) })));
      const tIssue = performance.now() - t0;
      await device.queue.onSubmittedWorkDone();
      runs.push({ issueMs: r3(tIssue), untilQueueIdleMs: r3(performance.now() - t0) });
    }
    B.d5_100pipelines_pureJS_sync_cold = runs;
    log('BENCH d5_100pipelines_pureJS_sync_cold', JSON.stringify(runs));
  }

  // (e) queue.onSubmittedWorkDone round trip
  async function workDone(name, prep, iters = 30) {
    const dt = [];
    for (let k = 0; k < iters; k++) {
      await nextFrame();
      prep();
      const t0 = performance.now();
      await device.queue.onSubmittedWorkDone();
      dt.push(performance.now() - t0);
    }
    B[name] = stats(dt);
    log('BENCH', name, JSON.stringify(B[name]));
  }
  await workDone('e1_onSubmittedWorkDone_idle_noSubmit', () => { });
  await workDone('e2_onSubmittedWorkDone_emptySubmit', () => device.queue.submit([device.createCommandEncoder().finish()]));
  await workDone('e3_onSubmittedWorkDone_after1000drawFrame', () => App.FrameManyDraws(DRAWS, true));
  {
    // async map round-trip of a small buffer (relevant to vkMapMemory/readback emulation)
    const rb = device.createBuffer({ size: 256, usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST });
    const dt = [];
    for (let k = 0; k < 30; k++) { const t0 = performance.now(); await rb.mapAsync(GPUMapMode.READ); dt.push(performance.now() - t0); rb.unmap(); }
    B.e4_mapAsync_read_256B = stats(dt);
    log('BENCH e4_mapAsync_read_256B', JSON.stringify(B.e4_mapAsync_read_256B));
    rb.destroy();
  }

  results.env.liveHandlesAtEnd = liveHandleCount();
  log('BENCH_JSON', JSON.stringify(results));
  log('ALL DONE');
} catch (e) {
  console.error('FATAL', e?.stack ?? String(e));
  log('FATAL', String(e));
}
