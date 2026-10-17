// Usage: node src/browser-check-ksa.mjs <dir-with-wgsl> <out.html>
// Generates a page that lets Tint compile every WGSL file in <dir> twice: raw, and with the layer's module-scope patches
// (`diagnostic(off, derivative_uniformity);` + `enable subgroups;` when subgroup builtins are used). Used on KSA-derived WGSL (kept outside the project).
import { readdirSync, readFileSync, writeFileSync } from 'node:fs'; import { join } from 'node:path';
const [dir, outHtml] = process.argv.slice(2);
const data = {}; for (const f of readdirSync(dir).filter((x) => x.endsWith('.wgsl'))) data[f] = readFileSync(join(dir, f), 'utf8');
const html = `<!doctype html><meta charset="utf-8"><title>ksa-wgsl-check</title><pre id="out">running...</pre><script type="module">
const SRC = ${JSON.stringify(data)};
const T = (p, ms, what) => Promise.race([p, new Promise((_, rej) => setTimeout(() => rej(new Error('timeout ' + what)), ms))]);
const result = { count: Object.keys(SRC).length, shaders: {} };
try {
  const adapter = await navigator.gpu.requestAdapter();
  const want = ['clip-distances', 'subgroups', 'shader-f16'].filter((f) => adapter.features.has(f));
  const device = await adapter.requestDevice({ requiredFeatures: want });
  const patch = (code0) => { const code = code0.replace(/@coherent\\s*/g, ''); return (/\\bsubgroup[A-Z]/.test(code) && !/^enable subgroups/m.test(code) ? 'enable subgroups;\\n' : '') + 'diagnostic(off, derivative_uniformity);\\n' + code; };
  for (const [name, code] of Object.entries(SRC)) {
    const r = {};
    for (const [label, src] of [['raw', code], ['patched', patch(code)]]) {
      const info = await T(device.createShaderModule({ code: src }).getCompilationInfo(), 20000, name);
      const errs = info.messages.filter((m) => m.type === 'error').map((m) => m.message.split('\\n')[0]);
      r[label] = errs.length ? errs.slice(0, 2) : 'OK';
    }
    result.shaders[name] = r;
  }
} catch (e) { result.error = String(e); }
const text = JSON.stringify(result);
document.getElementById('out').textContent = 'done';
await fetch('/result', { method: 'POST', body: text });
</script>`;
writeFileSync(outHtml, html); console.log(`wrote ${outHtml} (${Object.keys(data).length} WGSL files)`);
