// Usage: node src/browser-naga.mjs -> writes out/browser-naga.html
// Strategy-B demo: naga-wasm running INSIDE the browser. Fetches naga_bg.wasm via init(), fetches a Vulkan SPIR-V module,
// translates it to WGSL, lets Tint compile that WGSL, and reports timings and byte counts.
import { writeFileSync } from 'node:fs';
const html = `<!doctype html><meta charset="utf-8"><title>naga-in-browser</title><pre id="out">running...</pre>
<script type="module">
const result = { userAgent: navigator.userAgent };
try {
  const t0 = performance.now();
  const naga = await import('/node_modules/naga-wasm/dist/index.js');          // plain ESM, no bundler
  const t1 = performance.now();
  const wasmUrl = new URL('/node_modules/naga-wasm/dist/wasm/naga_bg.wasm', location.href);
  const resp = await fetch(wasmUrl); const wasmBytes = (await resp.clone().arrayBuffer()).byteLength;
  await naga.init({ module_or_path: resp });                                    // streaming compile + instantiate
  const t2 = performance.now();
  result.nagaVersion = naga.nagaVersion; result.wasmBytes = wasmBytes;
  result.timingsMs = { importEsm: +(t1 - t0).toFixed(1), fetchCompileInstantiate: +(t2 - t1).toFixed(1) };
  const adapter = await navigator.gpu.requestAdapter(); const device = await adapter.requestDevice();
  const files = ['mesh.vert', 'mesh.frag', 'mesh_push.vert'];
  result.shaders = {};
  for (const f of files) {
    const spv = new Uint8Array(await (await fetch('/out/' + f + '.spv')).arrayBuffer());
    const a = performance.now();
    const wgsl = naga.translate({ from: 'spirv', to: 'wgsl', source: spv });
    const b = performance.now();
    const info = await device.createShaderModule({ code: wgsl }).getCompilationInfo();
    const c = performance.now();
    // second call: warm
    const w0 = performance.now(); naga.translate({ from: 'spirv', to: 'wgsl', source: spv }); const w1 = performance.now();
    result.shaders[f] = { spvBytes: spv.length, wgslBytes: wgsl.length, translateFirstMs: +(b - a).toFixed(2), translateWarmMs: +(w1 - w0).toFixed(2), tintCompileMs: +(c - b).toFixed(2),
      tint: info.messages.length ? info.messages.map((m) => m.type + ': ' + m.message.split('\\n')[0]) : 'OK' };
  }
  // error path inside the browser
  try { naga.translate({ from: 'spirv', to: 'wgsl', source: new Uint8Array(await (await fetch('/out/mesh_combined.frag.spv')).arrayBuffer()) }); result.errorPath = 'no error?!'; }
  catch (e) { result.errorPath = { name: e.name, kind: e.kind, message: e.message }; }
} catch (e) { result.error = String(e && e.stack || e); }
const text = JSON.stringify(result, null, 1);
document.getElementById('out').textContent = text;
await fetch('/result', { method: 'POST', body: text });
</script>`;
writeFileSync('out/browser-naga.html', html); console.log('wrote out/browser-naga.html');
