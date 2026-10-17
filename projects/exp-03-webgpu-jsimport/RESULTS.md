# exp-03 results (raw)

Generated from `test/out/` and `test/out/extra/*/` on 2026-10-16. Machine: Apple M4 Pro (Mac16,7, 14 cores, 48 GB), macOS 27.0.1 (26A434).
Toolchain: .NET SDK 10.0.100, runtime pack `Microsoft.NETCore.App.Runtime.Mono.browser-wasm` 10.0.1 (gitHash `fad253f5`),
`dotnet publish -c Release` with the **interpreter** (no AOT, no `wasm-tools` workload) and the default jiterpreter. Node 24.18, puppeteer-core 25.14.0.
Timer: `performance.now()` resolution is 5 µs (the page is `crossOriginIsolated` because the server sends COOP/COEP). C# `Stopwatch.Frequency` = 1e9, but its readings are quantized to 5 µs, which suggests it is backed by the same clock.

## Commands

```sh
cd projects/exp-03-webgpu-jsimport/WebGpuTriangle && rm -rf bin/Release/net10.0/publish && dotnet publish -c Release
cd ../test && npm install
export E3_SCRATCH=<scratch dir for temp profiles>
node run.mjs --browser edge                                                        # -> out/        (= "Edge run1")
node run.mjs --browser edge --out out/extra/edge-run2                              # "Edge run2"
node run.mjs --browser edge --out out/extra/edge-run3                              # "Edge run3"
node run.mjs --browser edge --query nojiterp=1 --out out/extra/edge-nojiterp      # jiterpreter off
node run.mjs --browser cft  --out out/extra/cft                                    # Chrome for Testing 151
node run.mjs --browser edge --out out/extra/edge-swiftshader --flags "--enable-unsafe-webgpu --use-webgpu-adapter=swiftshader --enable-unsafe-swiftshader"
node run.mjs --browser shell --flags "--use-angle=metal" --out out/extra/shell-angle-metal
node probe/probe.mjs                                                               # -> out/probe-results.json
node summarize.mjs "Edge run1=out/results.json" ... > out/summary.md
```

Every run exited with code 0, which means the in-page pixel check and the screenshot pixel check both passed. No `WEBGPU ERROR`, `DEVICE LOST`, `FATAL` or `pageerror` lines appeared in any `console.log`.

## Benchmark summary (all configurations)

Statistics: (a)/(b) give the median of 7 runs of 10,000 calls, after one warm-up run, in ns per call (C# `Stopwatch` around the C# loop). (c) gives the median of 30 rAF frames after 5 warm-up frames, in JS wall time around the `[JSExport]` call. (d) gives one value per run: 3 runs per variant (median shown), except d1w, which is a single run. (e) gives the median of 30 iterations.

| Metric | Edge run1 | Edge run2 | Edge run3 | Edge no-jiterp | CfT 151 | Edge SwiftShader | shell angle-metal |
|---|---:|---:|---:|---:|---:|---:|---:|
| Adapter | apple/metal-3 | apple/metal-3 | apple/metal-3 | apple/metal-3 | apple/metal-3 | google/swiftshader (fallback) | apple/metal-3 |
| Browser | Edg/155.0.4283.45 | Edg/155.0.4283.45 | Edg/155.0.4283.45 | Edg/155.0.4283.45 | Chrome/151.0.7922.34 | Edg/155.0.4283.45 | HeadlessChrome/151.0.7922.34 |
| jiterpreter | default (enabled) | default (enabled) | default (enabled) | disabled via withRuntimeOptions | default (enabled) | default (enabled) | default (enabled) |
| PIXEL CHECK (GPU readback, individual path) | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| PIXEL CHECK (GPU readback, stream path) | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| Screenshot pixel check | PASS | PASS | PASS | PASS | PASS | PASS | PASS |
| toDataURL same task / after present | ok / ok | ok / ok | ok / ok | ok / ok | ok / ok | ok / ok | ok / ok |
| .NET start (dotnet.create→runMain) ms | 55.05 | 55.645 | 57.94 | 54.615 | 60.02 | 55.005 | 57.09 |
| Frame (1 draw, 11 JSImport calls) CPU ms median | 0.085 | 0.13 | 0.125 | 0.11 | 0.12 | 0.12 | 0.12 |
| Frame (1 draw, stream, 1 call) CPU ms median | 0.08 | 0.105 | 0.1 | 0.1 | 0.107 | 0.085 | 0.055 |
| rAF interval ms median | 16.665 | 16.665 | 16.665 | 16.665 | 16.665 | 16.665 | 8.33 |
| (a0) managed non-inlined call ns | 8 | 7.99 | 8.49 | 12.5 | 19 | 8.5 | 12 |
| (a1) JSImport int→int ns/call | 145.5 | 152.5 | 147.0 | 240.5 | 195.0 | 155.0 | 162.5 |
| (a2) JSImport 5 ints→int ns/call | 233.0 | 225.0 | 237.5 | 352.0 | 230.0 | 232.0 | 209.0 |
| (a3) JSImport JSObject→int ns/call | 147.0 | 151.5 | 147.5 | 191.0 | 139.5 | 147.5 | 130.5 |
| (a5) JSImport returning same JSObject ns/call | 254.5 | 251.0 | 256.5 | 297.0 | 233.5 | 272.0 | 226.5 |
| (a6) JSImport returning new JSObject (+Dispose) ns/call | 588.5 | 581.0 | 623.0 | 641.5 | 560.5 | 596.5 | 550.0 |
| (a4) JSExport int→int, JS→C# ns/call | 218.0 | 216.0 | 221.0 | 209.0 | 159.0 | 213.5 | 152.0 |
| (b1) writeBuffer 64 B, ptr+heap view ns/call | 273.5 | 270.5 | 285.5 | 381.5 | 236.0 | 285.0 | 218.5 |
| (b2) writeBuffer 64 B, Span→MemoryView.slice() ns/call | 648.5 | 611.0 | 663.5 | 666.0 | 658.5 | 656.0 | 627.5 |
| (b0) writeBuffer 64 B, pure JS ns/call | 83.5 | 81 | 86 | 81.5 | 58 | 84 | 54.5 |
| (b3) writeBuffer 64 B, pure JS from heap view ns/call | 89.5 | 86.5 | 92.5 | 85 | 60 | 91 | 57.5 |
| (c1) 1000 draws, individual JSImport ms/frame | 0.565 | 1.14 | 0.6 | 1.288 | 1.293 | 0.602 | 0.648 |
| (c2) 1000 draws, command stream ms/frame | 0.215 | 0.415 | 0.228 | 0.192 | 0.208 | 0.21 | 0.328 |
| (c2) … of which C# encode ms | 0.06 | 0.13 | 0.06 | 0.05 | 0.072 | 0.06 | 0.145 |
| (c2) … of which JS replay ms | 0.115 | 0.213 | 0.12 | 0.11 | 0.102 | 0.11 | 0.143 |
| (c0) 1000 draws, pure JS ms/frame | 0.1 | 0.148 | 0.115 | 0.102 | 0.075 | 0.095 | 0.132 |
| (d1) 100 pipelines C# sync: call ms | 0.515 | 0.55 | 0.515 | 0.485 | 0.53 | 0.53 | 0.485 |
| (d1) 100 pipelines C# sync cold: until queue idle ms | 642.4 | 657.9 | 645.9 | 593.2 | 615.0 | 96.76 | 562.7 |
| (d1w) 100 pipelines C# sync warm repeat: until idle ms | 2.6 | 3.055 | 2.68 | 2.115 | 2.285 | 2.58 | 2.31 |
| (d2) 100 identical pipelines (dedup): until idle ms | 1.63 | 1.01 | 0.925 | 1.17 | 1.385 | 0.95 | 1.46 |
| (d4) 100 pipelines C# async (Task.WhenAll) cold ms | 83.34 | 84.855 | 89.11 | 89.63 | 86.365 | 64.42 | 85.84 |
| (d3) 100 pipelines pure-JS async cold ms | 76.31 | 77.23 | 74.65 | 74.015 | 73.4 | 59.815 | 74.68 |
| (d5) 100 pipelines pure-JS sync cold: until idle ms | 596.0 | 606.9 | 688.2 | 619.5 | 567.0 | 98.68 | 556.5 |
| (e1) onSubmittedWorkDone, nothing submitted ms | 0.107 | 0.2 | 0.125 | 0.08 | 0.12 | 0.105 | 0.118 |
| (e2) onSubmittedWorkDone after empty submit ms | 0.37 | 0.175 | 0.165 | 0.165 | 0.205 | 0.12 | 0.165 |
| (e3) onSubmittedWorkDone after 1000-draw frame ms | 1.827 | 1.137 | 0.958 | 1.075 | 1.428 | 14.995 | 1.05 |
| (e4) mapAsync(READ) 256 B round trip ms | 0.125 | 0.06 | 0.045 | 0.06 | 0.105 | 0.06 | 0.1 |

Notes:
* `Frame (1 draw ...)` is the JS-measured CPU time of `App.Frame(t)`/`App.FrameStream(t)` inside the rAF callback. That covers the matrix update, `writeBuffer`, encoder, pass, draw and submit.
* (c1) varies between runs: it is either about 0.6 ms or about 1.1–1.3 ms per frame. The bimodality shows up across browsers too, which points to CPU power state or jiterpreter trace timing. (c2)/(c0) are much more stable.
* (d1) "call ms" counts only the time spent in the 100 sync `createRenderPipeline` JSImport calls. "until queue idle" adds the time until `queue.onSubmittedWorkDone()` resolves, which is when the GPU process has actually compiled all of them. Every cold shader embeds a unique integer constant `K = nonce*2000+slot`, so no cache can serve it.
* An exploratory run, made before the nonce was added, reused shader texts from an *earlier browser session* that had its own fresh profile. In that run (d1) dropped to **30–35 ms per 100** instead of ~600 ms. This points to a cross-process shader cache outside the Chromium profile, probably the OS Metal shader cache.

## Headless WebGPU flag matrix (`test/probe/probe.mjs`, `out/probe-results.json`)

| Browser / mode | Extra flags | Adapter | vendor / architecture | isFallbackAdapter | preferred format | maxBufferSize |
|---|---|---|---|---|---|---|
| Edge 155 (--headless=new) | `(none)` | yes | apple / metal-3 | False | bgra8unorm | 4294967292 |
| Edge 155 (--headless=new) | `--enable-unsafe-webgpu` | yes | apple / metal-3 | False | bgra8unorm | 4294967292 |
| Edge 155 (--headless=new) | `--enable-unsafe-webgpu --use-angle=metal` | yes | apple / metal-3 | False | bgra8unorm | 4294967292 |
| Edge 155 (--headless=new) | `--enable-unsafe-webgpu --use-webgpu-adapter=swiftshader --enable-unsafe-swiftshader` | yes | google / swiftshader | True | bgra8unorm | 1073741824 |
| Chrome for Testing 151 (--headless=new) | `(none)` | yes | apple / metal-3 | False | bgra8unorm | 4294967292 |
| Chrome for Testing 151 (--headless=new) | `--enable-unsafe-webgpu` | yes | apple / metal-3 | False | bgra8unorm | 4294967292 |
| Chrome for Testing 151 (--headless=new) | `--enable-unsafe-webgpu --use-angle=metal` | yes | apple / metal-3 | False | bgra8unorm | 4294967292 |
| Chrome for Testing 151 (--headless=new) | `--enable-unsafe-webgpu --use-webgpu-adapter=swiftshader --enable-unsafe-swiftshader` | yes | google / swiftshader | True | bgra8unorm | 1073741824 |
| chrome-headless-shell 151 (--headless) | `(none)` | **null** |  /  |  |  |  |
| chrome-headless-shell 151 (--headless) | `--enable-unsafe-webgpu` | yes | google / swiftshader | True | bgra8unorm | 1073741824 |
| chrome-headless-shell 151 (--headless) | `--enable-unsafe-webgpu --use-angle=metal` | yes | apple / metal-3 | False | bgra8unorm | 4294967292 |
| chrome-headless-shell 151 (--headless) | `--enable-unsafe-webgpu --use-webgpu-adapter=swiftshader --enable-unsafe-swiftshader` | yes | google / swiftshader | True | bgra8unorm | 1073741824 |

The `shell angle-metal` benchmark run passed `--use-angle=metal` *alone*, without `--enable-unsafe-webgpu`, to chrome-headless-shell and also got the `apple/metal-3` adapter. With no flags at all, headless-shell returns no adapter.
`adapter.info` is available directly (no `requestAdapterInfo()` call is needed). `device` and `description` are empty strings.
The hardware adapter reports `maxUniformBufferBindingSize` = 65536 and `minUniformBufferOffsetAlignment` = 256.

## Environment probe (Edge run1)

```json
{
 "crossOriginIsolated": true,
 "perfNowResolutionMs": 0.005,
 "adapter": {
  "vendor": "apple",
  "architecture": "metal-3",
  "device": "",
  "description": "",
  "isFallbackAdapter": false
 },
 "preferredFormat": "bgra8unorm",
 "dotnetStartMs": 55.05,
 "runtimeBuildInfo": {
  "productVersion": "10.0.1",
  "gitHash": "fad253f51b461736dfd3cd9c15977bb7493becef",
  "buildConfiguration": "Release",
  "wasmEnableThreads": false,
  "wasmEnableSIMD": true,
  "wasmEnableExceptionHandling": true
 },
 "jiterpreterOptions": {
  "enableTraces": 1,
  "enableInterpEntry": 1,
  "enableJitCall": 1
 },
 "heap": {
  "localHeapViewU8": "function",
  "getHeapU8": "function",
  "Module": "object",
  "Module_HEAPU8": "object",
  "Module_HEAPU8_isSameObjectAsLocalHeapViewU8": true,
  "localHeapViewU8_twice_sameObject": true,
  "heapBytesBefore": 33554432,
  "staleViewByteLengthBeforeGrow": 33554432,
  "staleViewByteLengthAfterGrow": 0,
  "freshViewByteLength": 294387712,
  "staleIsFresh": false
 },
 "writeBufferOnMappedBuffer": "[Buffer (unlabeled)] used in submit while mapped.",
 "liveHandlesAtEnd": 12
}
```

* `heap`: `localHeapViewU8()` returns the very object `Module.HEAPU8`, and repeated calls return the same object, so the call allocates nothing. After `App.GrowHeap(256)`, the wasm memory grew from 32 MiB to 280.75 MiB. That is 256 MiB plus allocator overhead from `NativeMemory.Alloc` of 256 MiB. The previously fetched view then reported `byteLength` 0 (detached).
* `writeBufferOnMappedBuffer`: calling `queue.writeBuffer` on a buffer that is still mapped from `mappedAtCreation: true` raises the validation error `"[Buffer (unlabeled)] used in submit while mapped."`. For that reason the vertex upload uses `getMappedRange()` + copy + `unmap()` (`copyToMappedRange`).
* `liveHandlesAtEnd` = 12. That is exactly the persistent objects: device, queue, 3 buffers, shader, 2 bind group layouts, 2 bind groups and 2 pipelines. Transient handles (pass, encoder, command buffer) are released automatically, and benchmark pipelines and shaders are released explicitly.

## Pixel checks (Edge run1)

```json
{"pass": true, "expect": {"center": [85, 85, 85], "corner": [26, 51, 77]}, "gpuReadback": {"center": [84, 86, 85, 255], "corner": [26, 51, 77, 255], "format": "bgra8unorm"}, "toDataURL_sameTask": {"center": [84, 86, 85, 255], "corner": [26, 51, 77, 255], "w": 640, "h": 480, "matches": true}, "toDataURL_afterPresent": {"center": [84, 86, 85, 255], "corner": [26, 51, 77, 255], "w": 640, "h": 480, "matches": true}}
{"pass": true, "w": 640, "h": 480, "center": [84, 86, 85, 255], "corner": [26, 51, 77, 255]}
```

The expected center colour is the average of the red, green and blue vertex colours, (85,85,85) ±10, because the triangle's centroid is at the origin. The expected corner colour is the clear colour (0.1,0.2,0.3) → (26,51,77) ±3.
All three readback methods agree: `copyTextureToBuffer`+`mapAsync`, `canvas.toDataURL()` (both in the same task and after presentation), and the CDP element screenshot.

## Primary run console (Edge run1, `test/out/console.log`; Canvas2D warnings and the long `BENCH_JSON` line omitted)

```text
[+0.004s] [driver] browser=edge exe=/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge headless=new args=["--no-first-run","--no-default-browser-check"] url=http://localhost:57644/index.html?gate=1 root=<repo>/projects/exp-03-webgpu-jsimport/WebGpuTriangle/bin/Release/net10.0/publish/wwwroot
[+0.292s] [driver] browser.version()=Edg/155.0.4283.45
[+0.352s] [http] 404 http://localhost:57644/favicon.ico
[+0.352s] [error] Failed to load resource: the server responded with a status of 404 (Not Found)
[+0.352s] [log] ENV {"userAgent":"Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/155.0.0.0 Safari/537.36 Edg/155.0.0.0","crossOriginIsolated":true,"perfNowResolutionMs":0.005,"adapter":{"vendor":"apple","architecture":"metal-3","device":"","description":"","isFallbackAdapter":false},"preferredFormat":"bgra8unorm"}
[+0.407s] [log] dotnet Main: .NET 10.0.1 browser-wasm IsDynamicCodeCompiled=False IsDynamicCodeSupported=True Stopwatch.Frequency=1000000000 IsHighResolution=True
[+0.408s] [log] JITERPRETER {"enableTraces":1,"enableInterpEntry":1,"enableJitCall":1}
[+0.408s] [log] DOTNET {"startMs":55.05,"productVersion":"10.0.1","gitHash":"fad253f51b461736dfd3cd9c15977bb7493becef","buildConfiguration":"Release","wasmEnableThreads":false,"wasmEnableSIMD":true,"wasmEnableExceptionHandling":true}
[+0.426s] [log] INIT init ok in 8.73 ms: format=Bgra8Unorm device=2 queue=3 vbo=4 ubo=5 uboMany=6 shader=7 pipeline=12 pipelineDyn=13 bg=10 bgMany=11 matrixPtr=0x1680b80 streamPtr=0x16da020
[+0.441s] [log] HEAP {"localHeapViewU8":"function","getHeapU8":"function","Module":"object","Module_HEAPU8":"object","Module_HEAPU8_isSameObjectAsLocalHeapViewU8":true,"localHeapViewU8_twice_sameObject":true,"heapBytesBefore":33554432,"staleViewByteLengthBeforeGrow":33554432,"staleViewByteLengthAfterGrow":0,"freshViewByteLength":294387712,"staleIsFresh":false}
[+0.444s] [log] MAPPED WRITE CHECK [Buffer (unlabeled)] used in submit while mapped.
[+2.430s] [log] FRAMES DONE {"frames":120,"cpuMs":{"n":120,"median":0.085,"mean":0.102,"min":0.03,"max":1.335},"rafIntervalMs":{"n":119,"median":16.665,"mean":16.666,"min":16.66,"max":16.67}}
[+2.470s] [log] PIXEL CHECK PASS {"pass":true,"expect":{"center":[85,85,85],"corner":[26,51,77]},"gpuReadback":{"center":[84,86,85,255],"corner":[26,51,77,255],"format":"bgra8unorm"},"toDataURL_sameTask":{"center":[84,86,85,255],"corner":[26,51,77,255],"w":640,"h":480,"matches":true},"toDataURL_afterPresent":{"center":[84,86,85,255],"corner":[26,51,77,255],"w":640,"h":480,"matches":true}}
[+2.470s] [log] GATE WAITING
[+2.572s] [driver] SCREENSHOT PIXEL CHECK PASS {"w":640,"h":480,"center":[84,86,85,255],"corner":[26,51,77,255]}
[+4.562s] [log] STREAM FRAMES DONE {"frames":120,"cpuMs":{"n":120,"median":0.08,"mean":0.079,"min":0.02,"max":0.52},"rafIntervalMs":{"n":119,"median":16.665,"mean":16.666,"min":16.66,"max":16.67}}
[+4.600s] [log] STREAM PIXEL CHECK PASS {"pass":true,"expect":{"center":[85,85,85],"corner":[26,51,77]},"gpuReadback":{"center":[84,86,85,255],"corner":[26,51,77,255],"format":"bgra8unorm"},"toDataURL_sameTask":{"center":[84,86,85,255],"corner":[26,51,77,255],"w":640,"h":480,"matches":true},"toDataURL_afterPresent":{"center":[84,86,85,255],"corner":[26,51,77,255],"w":640,"h":480,"matches":true}}
[+4.602s] [log] BENCH a0_managedCall_baseline 8 ns/call {"calls":10000,"runs":7,"csMs":{"n":7,"median":0.08,"mean":0.08,"min":0.07,"max":0.09},"wallMs":{"n":7,"median":0.08,"mean":0.081,"min":0.07,"max":0.09},"nsPerCall":8}
[+4.618s] [log] BENCH a1_jsimport_noop_int 145.5 ns/call {"calls":10000,"runs":7,"csMs":{"n":7,"median":1.455,"mean":1.536,"min":1.38,"max":1.785},"wallMs":{"n":7,"median":1.455,"mean":1.539,"min":1.38,"max":1.785},"nsPerCall":145.5}
[+4.640s] [log] BENCH a2_jsimport_noop_5ints 232.99 ns/call {"calls":10000,"runs":7,"csMs":{"n":7,"median":2.33,"mean":2.329,"min":2.26,"max":2.415},"wallMs":{"n":7,"median":2.33,"mean":2.334,"min":2.26,"max":2.43},"nsPerCall":232.99}
[+4.654s] [log] BENCH a3_jsimport_noop_JSObject 147 ns/call {"calls":10000,"runs":7,"csMs":{"n":7,"median":1.47,"mean":1.506,"min":1.38,"max":1.645},"wallMs":{"n":7,"median":1.47,"mean":1.508,"min":1.38,"max":1.65},"nsPerCall":147}
[+4.679s] [log] BENCH a5_jsimport_return_same_JSObject 254.5 ns/call {"calls":10000,"runs":7,"csMs":{"n":7,"median":2.545,"mean":2.636,"min":2.5,"max":3.015},"wallMs":{"n":7,"median":2.55,"mean":2.641,"min":2.505,"max":3.025},"nsPerCall":254.5}
[+4.737s] [log] BENCH a6_jsimport_return_new_JSObject_disposed 588.5 ns/call {"calls":10000,"runs":7,"csMs":{"n":7,"median":5.885,"mean":5.931,"min":5.495,"max":6.585},"wallMs":{"n":7,"median":5.89,"mean":5.943,"min":5.5,"max":6.615},"nsPerCall":588.5}
[+4.756s] [log] BENCH a4_jsexport_echo_JS_to_CS 218 ns/call {"calls":10000,"runs":7,"csMs":{"n":7,"median":2.18,"mean":2.171,"min":2.105,"max":2.245},"wallMs":{"n":7,"median":2.18,"mean":2.174,"min":2.105,"max":2.25},"nsPerCall":218}
[+4.782s] [log] BENCH b1_writeBuffer64_ptr 273.5 ns/call {"calls":10000,"runs":7,"csMs":{"n":7,"median":2.735,"mean":2.92,"min":2.54,"max":3.98},"wallMs":{"n":7,"median":2.75,"mean":2.929,"min":2.545,"max":3.99},"nsPerCall":273.5}
[+4.838s] [log] BENCH b2_writeBuffer64_span_memoryview 648.5 ns/call {"calls":10000,"runs":7,"csMs":{"n":7,"median":6.485,"mean":6.667,"min":5.81,"max":7.915},"wallMs":{"n":7,"median":6.505,"mean":6.683,"min":5.82,"max":7.93},"nsPerCall":648.5}
[+4.845s] [log] BENCH b0_writeBuffer64_pureJS_baseline 83.5 ns/call {"calls":10000,"runs":7,"csMs":{"n":7,"median":0.835,"mean":0.831,"min":0.755,"max":0.89},"wallMs":{"n":7,"median":0.84,"mean":0.832,"min":0.755,"max":0.89},"nsPerCall":83.5}
[+4.852s] [log] BENCH b3_writeBuffer64_pureJS_fromHeapView 89.5 ns/call {"calls":10000,"runs":7,"csMs":{"n":7,"median":0.895,"mean":0.897,"min":0.875,"max":0.93},"wallMs":{"n":7,"median":0.895,"mean":0.897,"min":0.875,"max":0.93},"nsPerCall":89.5}
[+5.781s] [log] BENCH c1_1000draws_individual_jsimport 0.565 ms/frame {"draws":1000,"frames":30,"wallMsPerFrame":{"n":30,"median":0.565,"mean":0.618,"min":0.48,"max":1.55},"csMsPerFrame":{"n":30,"median":0.533,"mean":0.594,"min":0.475,"max":1.53}}
[+6.365s] [log] BENCH c2_1000draws_command_stream 0.215 ms/frame {"draws":1000,"frames":30,"wallMsPerFrame":{"n":30,"median":0.215,"mean":0.236,"min":0.18,"max":0.605},"csMsPerFrame":{"n":30,"median":0.188,"mean":0.212,"min":0.165,"max":0.57},"csEncodeMs":{"n":30,"median":0.06,"mean":0.084,"min":0.055,"max":0.45},"jsReplayMs":{"n":30,"median":0.115,"mean":0.116,"min":0.095,"max":0.2}}
[+6.947s] [log] BENCH c0_1000draws_pureJS_baseline 0.1 ms/frame {"draws":1000,"frames":30,"wallMsPerFrame":{"n":30,"median":0.1,"mean":0.103,"min":0.085,"max":0.12}}
[+7.075s] [log] BENCH d0_warmup_10 {"n":10,"distinctShaders":true,"csShaderModuleMs":0.225,"csJsonBuildMs":0.05,"csCreatePipelineSyncMs":0.125,"callWallMs":1.32,"untilQueueIdleMs":111.15,"msPerPipelineSync":0.013,"msPerPipelineUntilIdle":11.115}
[+7.747s] [log] BENCH d1_100pipelines_cs_sync_cold {"n":100,"distinctShaders":true,"csShaderModuleMs":0.82,"csJsonBuildMs":0.94,"csCreatePipelineSyncMs":0.515,"callWallMs":2.44,"untilQueueIdleMs":671.245,"msPerPipelineSync":0.005,"msPerPipelineUntilIdle":6.712}
[+8.390s] [log] BENCH d1_100pipelines_cs_sync_cold {"n":100,"distinctShaders":true,"csShaderModuleMs":0.665,"csJsonBuildMs":0.22,"csCreatePipelineSyncMs":0.55,"callWallMs":1.625,"untilQueueIdleMs":642.42,"msPerPipelineSync":0.006,"msPerPipelineUntilIdle":6.424}
[+8.992s] [log] BENCH d1_100pipelines_cs_sync_cold {"n":100,"distinctShaders":true,"csShaderModuleMs":0.425,"csJsonBuildMs":1.395,"csCreatePipelineSyncMs":0.48,"callWallMs":2.485,"untilQueueIdleMs":601.25,"msPerPipelineSync":0.005,"msPerPipelineUntilIdle":6.013}
[+8.995s] [log] BENCH d1w_100pipelines_cs_sync_warm_repeat {"n":100,"distinctShaders":true,"csShaderModuleMs":0.995,"csJsonBuildMs":0.175,"csCreatePipelineSyncMs":0.455,"callWallMs":1.775,"untilQueueIdleMs":2.6,"msPerPipelineSync":0.005,"msPerPipelineUntilIdle":0.026}
[+8.998s] [log] BENCH d2_100pipelines_cs_sync_sameShader_dedup {"n":100,"distinctShaders":false,"csShaderModuleMs":0,"csJsonBuildMs":0.15,"csCreatePipelineSyncMs":0.48,"callWallMs":0.76,"untilQueueIdleMs":1.63,"msPerPipelineSync":0.005,"msPerPipelineUntilIdle":0.016}
[+9.003s] [log] BENCH d2_100pipelines_cs_sync_sameShader_dedup {"n":100,"distinctShaders":false,"csShaderModuleMs":0,"csJsonBuildMs":3.715,"csCreatePipelineSyncMs":0.515,"callWallMs":4.695,"untilQueueIdleMs":5.75,"msPerPipelineSync":0.005,"msPerPipelineUntilIdle":0.058}
[+9.004s] [log] BENCH d2_100pipelines_cs_sync_sameShader_dedup {"n":100,"distinctShaders":false,"csShaderModuleMs":0.005,"csJsonBuildMs":0.12,"csCreatePipelineSyncMs":0.57,"callWallMs":0.77,"untilQueueIdleMs":0.98,"msPerPipelineSync":0.006,"msPerPipelineUntilIdle":0.01}
[+9.264s] [log] BENCH d4_100pipelines_cs_async_cold [{"csShaderModuleMs":0.285,"csIssueMs":1.91,"csAllResolvedMs":91.48,"jsWallMs":92.605},{"csShaderModuleMs":0.31,"csIssueMs":1.465,"csAllResolvedMs":82.44,"jsWallMs":82.615},{"csShaderModuleMs":0.27,"csIssueMs":1.23,"csAllResolvedMs":83.18,"jsWallMs":83.34}]
[+9.493s] [log] BENCH d3_100pipelines_pureJS_async_cold [{"issueMs":1.06,"allResolvedMs":74.885},{"issueMs":0.905,"allResolvedMs":77.485},{"issueMs":0.88,"allResolvedMs":76.31}]
[+11.295s] [log] BENCH d5_100pipelines_pureJS_sync_cold [{"issueMs":0.29,"untilQueueIdleMs":579.995},{"issueMs":0.33,"untilQueueIdleMs":608.085},{"issueMs":0.32,"untilQueueIdleMs":596}]
[+11.780s] [log] BENCH e1_onSubmittedWorkDone_idle_noSubmit {"n":30,"median":0.107,"mean":0.146,"min":0.055,"max":0.82}
[+12.281s] [log] BENCH e2_onSubmittedWorkDone_emptySubmit {"n":30,"median":0.37,"mean":0.377,"min":0.16,"max":1.295}
[+12.781s] [log] BENCH e3_onSubmittedWorkDone_after1000drawFrame {"n":30,"median":1.827,"mean":1.824,"min":1.14,"max":2.54}
[+12.785s] [log] BENCH e4_mapAsync_read_256B {"n":30,"median":0.125,"mean":0.13,"min":0.11,"max":0.24}
[+12.785s] [log] ALL DONE
[+12.850s] [driver] RESULT in-page PIXEL CHECK PASS; screenshot check PASS
```
