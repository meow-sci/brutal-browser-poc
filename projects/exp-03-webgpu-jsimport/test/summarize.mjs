// Summarize one or more results.json files into a Markdown table (one column per run).
// Usage: node summarize.mjs label1=path/to/results.json label2=... > table.md
import fs from 'node:fs';

const runs = process.argv.slice(2).map((a) => {
  const [label, file] = a.includes('=') ? a.split('=') : [a, a];
  return { label, r: JSON.parse(fs.readFileSync(file, 'utf8')) };
});
const med = (a) => { const s = a.filter((x) => typeof x === 'number' && !Number.isNaN(x)).sort((x, y) => x - y); if (!s.length) return NaN; const m = s.length >> 1; return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2; };
const fmt = (x, d = 3) => (typeof x === 'number' && !Number.isNaN(x) ? (Math.abs(x) >= 100 ? x.toFixed(1) : x.toFixed(d).replace(/\.?0+$/, '')) : 'n/a');

const rows = [
  ['Adapter', (r) => `${r.env.adapter.vendor}/${r.env.adapter.architecture}${r.env.adapter.isFallbackAdapter ? ' (fallback)' : ''}`, 'text'],
  ['Browser', (r) => r.driver?.browserVersion, 'text'],
  ['jiterpreter', (r) => r.env.jiterpreter, 'text'],
  ['PIXEL CHECK (GPU readback, individual path)', (r) => (r.pixel.individual.pass ? 'PASS' : 'FAIL'), 'text'],
  ['PIXEL CHECK (GPU readback, stream path)', (r) => (r.pixel.stream.pass ? 'PASS' : 'FAIL'), 'text'],
  ['Screenshot pixel check', (r) => (r.driver?.screenshotPixelCheck?.pass ? 'PASS' : 'FAIL'), 'text'],
  ['toDataURL same task / after present', (r) => `${r.pixel.individual.toDataURL_sameTask.matches ? 'ok' : 'blank'} / ${r.pixel.individual.toDataURL_afterPresent.matches ? 'ok' : 'blank'}`, 'text'],
  ['.NET start (dotnet.create→runMain) ms', (r) => r.env.dotnetStartMs],
  ['Frame (1 draw, 11 JSImport calls) CPU ms median', (r) => r.frames.individual.cpuMs.median],
  ['Frame (1 draw, stream, 1 call) CPU ms median', (r) => r.frames.stream.cpuMs.median],
  ['rAF interval ms median', (r) => r.frames.individual.rafIntervalMs.median],
  ['(a0) managed non-inlined call ns', (r) => r.bench.a0_managedCall_baseline.nsPerCall],
  ['(a1) JSImport int→int ns/call', (r) => r.bench.a1_jsimport_noop_int.nsPerCall],
  ['(a2) JSImport 5 ints→int ns/call', (r) => r.bench.a2_jsimport_noop_5ints.nsPerCall],
  ['(a3) JSImport JSObject→int ns/call', (r) => r.bench.a3_jsimport_noop_JSObject.nsPerCall],
  ['(a5) JSImport returning same JSObject ns/call', (r) => r.bench.a5_jsimport_return_same_JSObject.nsPerCall],
  ['(a6) JSImport returning new JSObject (+Dispose) ns/call', (r) => r.bench.a6_jsimport_return_new_JSObject_disposed.nsPerCall],
  ['(a4) JSExport int→int, JS→C# ns/call', (r) => r.bench.a4_jsexport_echo_JS_to_CS.nsPerCall],
  ['(b1) writeBuffer 64 B, ptr+heap view ns/call', (r) => r.bench.b1_writeBuffer64_ptr.nsPerCall],
  ['(b2) writeBuffer 64 B, Span→MemoryView.slice() ns/call', (r) => r.bench.b2_writeBuffer64_span_memoryview.nsPerCall],
  ['(b0) writeBuffer 64 B, pure JS ns/call', (r) => r.bench.b0_writeBuffer64_pureJS_baseline.nsPerCall],
  ['(b3) writeBuffer 64 B, pure JS from heap view ns/call', (r) => r.bench.b3_writeBuffer64_pureJS_fromHeapView.nsPerCall],
  ['(c1) 1000 draws, individual JSImport ms/frame', (r) => r.bench.c1_1000draws_individual_jsimport.wallMsPerFrame.median],
  ['(c2) 1000 draws, command stream ms/frame', (r) => r.bench.c2_1000draws_command_stream.wallMsPerFrame.median],
  ['(c2) … of which C# encode ms', (r) => r.bench.c2_1000draws_command_stream.csEncodeMs.median],
  ['(c2) … of which JS replay ms', (r) => r.bench.c2_1000draws_command_stream.jsReplayMs.median],
  ['(c0) 1000 draws, pure JS ms/frame', (r) => r.bench.c0_1000draws_pureJS_baseline.wallMsPerFrame.median],
  ['(d1) 100 pipelines C# sync: call ms', (r) => med(r.bench.d1_100pipelines_cs_sync_cold.map((x) => x.csCreatePipelineSyncMs))],
  ['(d1) 100 pipelines C# sync cold: until queue idle ms', (r) => med(r.bench.d1_100pipelines_cs_sync_cold.map((x) => x.untilQueueIdleMs))],
  ['(d1w) 100 pipelines C# sync warm repeat: until idle ms', (r) => r.bench.d1w_100pipelines_cs_sync_warm_repeat[0].untilQueueIdleMs],
  ['(d2) 100 identical pipelines (dedup): until idle ms', (r) => med(r.bench.d2_100pipelines_cs_sync_sameShader_dedup.map((x) => x.untilQueueIdleMs))],
  ['(d4) 100 pipelines C# async (Task.WhenAll) cold ms', (r) => med(r.bench.d4_100pipelines_cs_async_cold.map((x) => x.jsWallMs))],
  ['(d3) 100 pipelines pure-JS async cold ms', (r) => med(r.bench.d3_100pipelines_pureJS_async_cold.map((x) => x.allResolvedMs))],
  ['(d5) 100 pipelines pure-JS sync cold: until idle ms', (r) => med(r.bench.d5_100pipelines_pureJS_sync_cold.map((x) => x.untilQueueIdleMs))],
  ['(e1) onSubmittedWorkDone, nothing submitted ms', (r) => r.bench.e1_onSubmittedWorkDone_idle_noSubmit.median],
  ['(e2) onSubmittedWorkDone after empty submit ms', (r) => r.bench.e2_onSubmittedWorkDone_emptySubmit.median],
  ['(e3) onSubmittedWorkDone after 1000-draw frame ms', (r) => r.bench.e3_onSubmittedWorkDone_after1000drawFrame.median],
  ['(e4) mapAsync(READ) 256 B round trip ms', (r) => r.bench.e4_mapAsync_read_256B.median],
];

const safe = (f, r) => { try { return f(r); } catch { return NaN; } };
let out = `| Metric | ${runs.map((x) => x.label).join(' | ')} |\n|---|${runs.map(() => '---:').join('|')}|\n`;
for (const [name, f, kind] of rows) {
  out += `| ${name} | ${runs.map(({ r }) => { const v = safe(f, r); return kind === 'text' ? (v ?? 'n/a') : fmt(v); }).join(' | ')} |\n`;
}
process.stdout.write(out);
