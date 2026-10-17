---
type: Experiment Report
title: "exp-03: C# → WebGPU through [JSImport] on .NET 10 browser-wasm (spinning triangle and interop cost)"
description: A prototype that drives WebGPU from C# through int handles, wasm-heap pointer reads, JSON pipeline descriptors and a batched binary command stream, verified in headless Edge/Chromium with pixel checks, with measured per-call, batching, pipeline-creation and queue round-trip costs that feed the Vulkan-on-WebGPU layer design.
tags: [experiment, webgpu, dotnet-wasm, jsimport, jsexport, interop, performance, command-stream, headless, puppeteer, vulkan-on-webgpu]
sources:
  - id: project
    resource: ../../../projects/exp-03-webgpu-jsimport/README.md
    title: exp-03 project (WebGpuTriangle + test driver); build, serve and test instructions
  - id: results
    resource: ../../../projects/exp-03-webgpu-jsimport/RESULTS.md
    title: exp-03 raw results (7 configurations, flag matrix, primary console log)
  - id: glue
    resource: ../../../projects/exp-03-webgpu-jsimport/WebGpuTriangle/wwwroot/webgpu-glue.js
    title: webgpu-glue.js (handle table, heap reads, pipeline JSON, replayCommandStream)
  - id: cs-binding
    resource: ../../../projects/exp-03-webgpu-jsimport/WebGpuTriangle/WebGpu.cs
    title: WebGpu.cs ([JSImport] declarations) and CommandStream.cs / Program.cs
  - id: ms-dotnet-interop
    resource: https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/?view=aspnetcore-10.0
    title: JavaScript [JSImport]/[JSExport] interop in .NET WebAssembly (Microsoft Learn, .NET 10; type mappings, MemoryView, performance considerations)
  - id: ms-wasm-browser-app
    resource: https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/wasm-browser-app?view=aspnetcore-10.0
    title: JavaScript [JSImport]/[JSExport] interop with a WebAssembly Browser App project (Microsoft Learn, .NET 10)
  - id: dotnet-d-ts
    resource: file:///Users/asherwin/.nuget/packages/microsoft.netcore.app.runtime.mono.browser-wasm/10.0.1/runtimes/browser-wasm/native/dotnet.d.ts
    title: dotnet.d.ts, runtime pack 10.0.1 (RuntimeAPI.Module, localHeapView*, getHeap*, IMemoryView)
  - id: webgpu-spec
    resource: https://www.w3.org/TR/webgpu/
    title: WebGPU, W3C Candidate Recommendation Draft, 15 September 2026
  - id: puppeteer-launcher
    resource: ../../../projects/exp-03-webgpu-jsimport/test/node_modules/puppeteer-core/lib/puppeteer/node/ChromeLauncher.js
    title: puppeteer-core 25.14.0 ChromeLauncher (headless true → --headless=new, 'shell' → --headless)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:07:00Z }
status: draft
---

# Purpose

This experiment is a prototype of the smallest C# → WebGPU binding that has the shape the eventual
"Vulkan-on-WebGPU" layer needs. In that layer, `Brutal.Vulkan` calls a C# Vulkan implementation through
unmanaged function pointers, and that implementation drives WebGPU. The experiment set out to answer five
questions with measurements:

1. What does one `[JSImport]` call cost on .NET 10 browser-wasm, as a fixed cost and per argument? How much do
   `JSObject` proxies cost compared with integer handles?
2. How should bulk data move between C# and JS: raw pointer plus heap view, or `Span` / `MemoryView`? Which runtime
   heap accessor works, and what goes wrong when memory grows?
3. How much does batching per-draw commands into a binary stream gain over one interop call per command?
4. What does render pipeline creation really cost, and does sync or async creation matter?
5. Does WebGPU work in headless Chromium on macOS, with which flags, and how can rendering be verified (pixels)?

Every number below comes from **interpreter mode**. The `wasm-tools` workload is not installed, so there is no AOT.
The jiterpreter is on by default, and one run turns it off.[^results]

Related corpus pages: [JS interop reference](/dotnet-wasm/js-interop-jsimport-jsexport.md),
[performance and batching design](/webgpu/performance-and-batching-design.md),
[Vulkan → WebGPU command mapping](/webgpu/vulkan-to-webgpu-command-mapping.md),
[object mapping](/webgpu/vulkan-to-webgpu-object-mapping.md),
[memory model and mapping emulation](/webgpu/memory-model-and-mapping-emulation.md),
[synchronization and async shape](/webgpu/synchronization-and-async-shape.md),
[.NET 10 runtime overview](/dotnet-wasm/runtime-overview-net10.md).

# Setup

| Item | Value |
|---|---|
| Machine | Apple M4 Pro (Mac16,7), 14 cores, 48 GB, macOS 27.0.1 (26A434) |
| .NET | SDK 10.0.100; runtime pack `Microsoft.NETCore.App.Runtime.Mono.browser-wasm` **10.0.1** (`runtimeBuildInfo`: Release, SIMD on, wasm EH on, threads off) |
| Project | `projects/exp-03-webgpu-jsimport/WebGpuTriangle`[^project], created with `dotnet new wasmbrowser` (templates `Microsoft.NET.Runtime.WebAssembly.Templates.net10` 10.0.13), `Microsoft.NET.Sdk.WebAssembly`, net10.0, `AllowUnsafeBlocks` |
| Build | `dotnet publish -c Release` (about 7 s; IL trimming; prints the "Publishing without optimizations … wasm-tools" warning). Raw bundle size 7.0 MB: `dotnet.native.wasm` 3.0 MB, CoreLib 1.1 MB, ICU 2.6 MB |
| Execution | Mono interpreter + jiterpreter (`enableTraces/InterpEntry/JitCall = 1`); the `?nojiterp=1` run sets all three to 0 via `withRuntimeOptions` |
| Serving | `test/serve.mjs` (node `http`, no dependencies): `application/wasm`, plus COOP/COEP so the page is `crossOriginIsolated` and `performance.now()` has 5 µs resolution |
| Driver | puppeteer-core 25.14.0 with `executablePath`; `headless: true` ⇒ `--headless=new`[^puppeteer-launcher] |
| Primary browser | **Microsoft Edge 155.0.4283.45**, `--headless=new`, **no extra flags** |
| Adapter | `adapter.info` = `{vendor: "apple", architecture: "metal-3", device: "", description: ""}`, `isFallbackAdapter: false`, preferred canvas format `bgra8unorm`, `minUniformBufferOffsetAlignment` 256 |
| Other configurations | Chrome for Testing 151.0.7922.34 (Playwright cache); chrome-headless-shell 151 + `--use-angle=metal`; Edge + SwiftShader; Edge with the jiterpreter off |

The headless WebGPU flag matrix (`test/probe/probe.mjs`)[^results]:

| Browser / mode | No flags | `--enable-unsafe-webgpu` | `+ --use-angle=metal` | `--use-webgpu-adapter=swiftshader --enable-unsafe-swiftshader` |
|---|---|---|---|---|
| Edge 155, `--headless=new` | **metal-3** | metal-3 | metal-3 | swiftshader (fallback) |
| Chrome for Testing 151, `--headless=new` | **metal-3** | metal-3 | metal-3 | swiftshader (fallback) |
| chrome-headless-shell 151, `--headless` | **null adapter** | swiftshader (fallback) | metal-3 | swiftshader (fallback) |

On chrome-headless-shell, `--use-angle=metal` alone, without `--enable-unsafe-webgpu`, also gives metal-3.

# Binding design

The design is in `webgpu-glue.js`[^glue] and `WebGpu.cs`/`CommandStream.cs`.[^cs-binding]

- **Order of initialization.** `main.js` runs `navigator.gpu.requestAdapter()` → `requestDevice()` and configures the
  `<canvas id="gpu">` context with `{format: getPreferredCanvasFormat(), alphaMode: 'opaque', usage: RENDER_ATTACHMENT | COPY_SRC}`.
  Only then does it call `dotnet.create()`, `setModuleImports('webgpu', createImports())`, `getAssemblyExports`
  and `runMain()`.[^ms-wasm-browser-app] C# then calls `App.Init()` (a `[JSExport]`), and JS drives
  `App.Frame(t)` from `requestAnimationFrame`.
- **Handle table.** All WebGPU objects live in a JS array: index = handle, `0` = null, and freed slots go on a free
  list. C# sees only `int`s, which map 1:1 onto Vulkan's opaque handles. Handle `1` is reserved for "the current
  canvas texture view": `getCurrentTextureView()` refreshes the slot and returns `1`, so the swapchain view needs no
  release call. Transient objects release themselves: a pass on `endPass`, an encoder on `finish`, a command buffer
  on `submit`. At the end of a full run exactly 12 handles were live, which is the persistent set, so nothing leaked.
- **Pointer reads.** C# passes `nint` (`[JSMarshalAs<JSType.Number>]`; `IntPtr ↔ Number` is a supported
  mapping[^ms-dotnet-interop]) plus a byte length. JS runs
  `queue.writeBuffer(buf, off, api.localHeapViewU8(), ptr, len)`, which needs no JS-side copy and no allocation.
  `api` is the `RuntimeAPI` returned by `dotnet.create()`.
  `localHeapViewU8()` is the typed API ("a short term view … Don't store the reference, don't use it after
  await").[^dotnet-d-ts] `Module.HEAPU8` also exists at run time but is not typed on `EmscriptenModule`.
  `getHeapU8(ptr)` reads a single byte. The C# side keeps upload data in `NativeMemory.AlignedAlloc` memory, so its
  address is stable.
- **JSON for rare and complex descriptors.** `createRenderPipelineJson(json)` receives
  `{layout:[bglHandle], vertex:{module:h, entryPoint, buffers:[…]}, fragment:{module:h, targets:[{format:<int enum>}]}, primitive:{…}}`.
  JS swaps handle ids for objects and enum ints for strings (`FORMATS[]`). `createRenderPipelineJsonAsync` does the
  same and returns `Task<int>` (`JSType.Promise<JSType.Number>`).
- **Binary records for hot data.** These are fixed-layout u32 records in wasm memory, decoded through
  `localHeapViewU32()`/`F32()`. Dynamic offsets are handed to
  `setBindGroup(i, bg, heapU32, start, length)` straight from the heap, with no array allocation.[^webgpu-spec]
- **Command stream format.** Little-endian u32 words, 4-byte aligned, `[opcode][args…]`, terminated by `END`.
  Replaying it takes **one** JSImport call, `replayCommandStream(ptr, byteLength)`, which keeps an implicit
  encoder and pass between opcodes:

  | Op | Args (u32 unless noted) |
  |---|---|
  | 0 `END` | – |
  | 1 `BEGIN_PASS` | viewH (0 = current canvas texture), loadOp, storeOp, clear r,g,b,a (f32) |
  | 2 `END_PASS` | – |
  | 3 `SET_PIPELINE` | pipelineH |
  | 4 `SET_VERTEX_BUFFER` | slot, bufferH, offset |
  | 5 `SET_BIND_GROUP` | index, bindGroupH, dynCount, dyn[dynCount] |
  | 6 `DRAW` | vertexCount, instanceCount, firstVertex, firstInstance |
  | 7 `WRITE_BUFFER` | bufferH, bufferOffset, srcPtr, size (`queue.writeBuffer`, runs immediately during replay) |
  | 8 `SUBMIT` | – (finish the implicit encoder + `queue.submit`) |

- **Frame paths.** `Frame(t)` makes 11 JSImport calls: `writeBuffer`, `createCommandEncoder`,
  `getCurrentTextureView`, `beginRenderPass`, `setPipeline`, `setVertexBuffer`, `setBindGroup`, `draw`, `endPass`,
  `finish` and `submit`. `FrameStream(t)` writes 8 records and makes 1 call. Uniforms hold a 4×4 column-major
  rotation × aspect matrix (64 B). The 1000-draw benchmark uses a 256 KB uniform buffer with a dynamic-offset
  bind group (256 B stride).

# Results

| Check | Result |
|---|---|
| `FRAMES DONE` (120 rAF frames, individual-call path) | yes; rAF interval 16.665 ms; `Frame()` CPU median 0.085–0.13 ms |
| `STREAM FRAMES DONE` (120 frames, command-stream path) | yes; `FrameStream()` CPU median 0.055–0.107 ms |
| **PIXEL CHECK** (in-page `copyTextureToBuffer` + `mapAsync`) | **PASS**: center `[84,86,85]` (expected 85,85,85 ±10), corner `[26,51,77]` (clear colour 0.1/0.2/0.3) |
| STREAM PIXEL CHECK | **PASS** (identical pixels) |
| Screenshot check (`elementHandle.screenshot`, decoded in page) | **PASS**: same pixels; `test/out/triangle.png` |
| `canvas.toDataURL()` on the WebGPU canvas | works **both** in the same task as rendering and after presentation (same pixels) |
| All 7 configurations (Edge ×3, Edge no-jiterp, CfT, Edge SwiftShader, headless-shell+metal) | all PASS, no `WEBGPU ERROR`, no device loss |
| `queue.writeBuffer` on a buffer still mapped from `mappedAtCreation` | validation error: `"[Buffer (unlabeled)] used in submit while mapped."` |
| Heap growth | the cached view went from `byteLength` 33,554,432 → **0** after `NativeMemory.Alloc(256 MiB)`; a fresh `localHeapViewU8()` reports 294,387,712 |
| .NET start (`dotnet.create()` → `runMain()`) | 55–60 ms (published, local server, warm disk) |

# Benchmarks

The values are medians over the 3 Edge runs. Ranges in brackets span all 7 configurations, excluding SwiftShader
for GPU-bound rows. (a) and (b) are the median of 7 runs × 10,000 calls after a warm-up run, timed by C#
`Stopwatch`. (c) is the median of 30 rAF frames. Full per-run table: RESULTS.md.[^results]

| # | Benchmark | Result |
|---|---|---|
| a0 | managed non-inlined call (baseline) | 8 ns |
| **a1** | **empty `[JSImport]` int→int** | **147 ns/call** [145–240]; no jiterpreter: 240 ns |
| a2 | `[JSImport]` with 5 ints → int | 233 ns/call (≈ +21 ns per extra int arg); no jiterpreter: 352 ns |
| a3 | `[JSImport]` with an existing `JSObject` arg | 147.5 ns/call (same as int) |
| a5 | `[JSImport]` returning an existing (already-proxied) `JSObject` | 254.5 ns/call |
| a6 | `[JSImport]` returning a **new** `JSObject` (+ `Dispose`) | 588.5 ns/call (4× an int handle) |
| a4 | `[JSExport]` int→int called from JS | 218 ns/call [152–221] |
| **b1** | **`writeBuffer` 64 B, `nint` + `localHeapViewU8()`** | **273.5 ns/call** [218–381] |
| b2 | `writeBuffer` 64 B, `Span<byte>` → `MemoryView.slice()` | 648.5 ns/call (2.4× b1) |
| b0 / b3 | same `writeBuffer` from pure JS (Float32Array / heap view) | 83.5 / 89.5 ns/call |
| **c1** | **1000 draws (setBindGroup+draw) as individual JSImport calls** | **0.60 ms/frame** [0.565–1.29] |
| **c2** | **1000 draws through one `replayCommandStream`** | **0.228 ms/frame** [0.19–0.415]: C# encode 0.06 ms + JS replay 0.12 ms |
| c0 | 1000 draws in pure JS (baseline) | 0.115 ms/frame [0.075–0.15] |
| d1 | 100 render pipelines, sync `createRenderPipeline` via JSON: time in the calls | 0.515 ms (≈5 µs each) |
| **d1** | … time until `onSubmittedWorkDone` resolves (GPU-process compile), cold shaders | **646 ms** (≈6.5 ms per pipeline) [563–658] |
| d5 | same, pure JS sync | 607 ms |
| **d4** | **100 pipelines `createRenderPipelineAsync` from C# (`Task<int>` + `Task.WhenAll`), cold** | **84.9 ms** [83–90] |
| d3 | same, pure JS async (`Promise.all`) | 76.3 ms |
| d1w | 100 sync pipelines, same shaders again after release (warm) | 2.7 ms |
| d2 | 100 identical pipeline descriptors (same shader module) | 1.0 ms (deduplicated) |
| d (SwiftShader) | sync cold / C# async / JS async, per 100 | 96.8 / 64.4 / 59.8 ms |
| e1 | `queue.onSubmittedWorkDone()` with nothing submitted | 0.125 ms |
| **e2** | **`onSubmittedWorkDone()` after an empty submit** | **0.175 ms** [0.12–0.37] |
| e3 | `onSubmittedWorkDone()` after a 1000-draw frame | 1.14 ms [0.96–1.83]; SwiftShader 15.0 ms |
| e4 | `mapAsync(READ)` round trip, 256 B | 0.06 ms |

# Findings

1. **A JSImport call costs about 150 ns in interpreter mode** on an M4 Pro: about 140 ns fixed, plus about 20 ns per
   extra numeric argument. With the jiterpreter off it rises to about 240 ns. The JS→C# direction (`[JSExport]`)
   costs about 220 ns. This is about 18× a managed call. That roughly matches the documented "order of magnitude
   slower" than staying inside .NET,[^ms-dotnet-interop] and it is cheap enough for per-object and per-resource calls.
2. **`JSObject` is not slower as an argument, but it is slower as a result.** Passing an existing proxy costs the
   same as an `int`. Returning one costs 1.7×, and returning a new one (proxy + GCHandle, disposed) costs 4×. It
   would also add GC and finalizer pressure for transient objects such as passes, encoders, views and command
   buffers, which matches the documented warning that `JSObject` and similar types "carry significant performance
   overhead".[^ms-dotnet-interop] **Int handles win** for anything created per frame.
3. **Batching gives 2.6× at 1000 draws** (0.60 → 0.23 ms per frame). In the runs where the individual-call path was
   slow (no jiterpreter, CfT run), batching gave up to 6.7×. The stream path is also much less sensitive to the
   jiterpreter (0.19 ms with it off). In the stream path the JS replay (0.12 ms) costs about the same as hand-written
   JS (0.115 ms), so the only overhead left is the C# encode (0.06 ms per 1000 draws, ≈ 60 ns per draw record pair).
   Per draw: ≈ 0.6 µs individual, ≈ 0.23 µs batched, ≈ 0.115 µs pure JS. For a single-draw frame the difference
   disappears (0.12 vs 0.10 ms), because WebGPU's own per-frame work dominates.
4. **Pass bulk data as `nint` + length, and read it through `localHeapViewU8()`.** The pointer path for a 64 B
   `writeBuffer` costs 273 ns, of which about 185 ns is interop. That is 2.4× cheaper than `Span<byte>` as
   `JSType.MemoryView` + `slice()`. Passing a `Span` *to* a `[JSImport]` compiles and works. A sentence in the docs
   says otherwise, but that sentence applies to the `[JSExport]` direction (see the
   [interop reference](/dotnet-wasm/js-interop-jsimport-jsexport.md)).
5. **`localHeapViewU8()` is the right accessor.** It returns the very object `Module.HEAPU8`, the same object on
   every call, with no allocation. Emscripten replaces it after `memory.grow`, and **a cached view becomes detached
   (`byteLength` 0)**, as this experiment demonstrated. So re-fetch the view inside every JS entry point, and never
   hold it across an `await` or a call back into .NET.[^dotnet-d-ts] Reading the whole-heap view with an offset
   costs nothing extra (89.5 vs 83.5 ns).
6. **Creating a pipeline is cheap at the call site and expensive in the GPU process.** A sync
   `createRenderPipeline` returns in about 5 µs, but the GPU process compiles serially, at about 6.5 ms per cold
   pipeline on Metal. That **stalls later queue work** (`onSubmittedWorkDone` waits for it).
   `createRenderPipelineAsync` compiles in parallel and has **about 7.6× the throughput** (85 ms vs 646 ms per 100).
   Driving it from C# through `Task<int>` costs only about 10% more than pure JS. Building the descriptor JSON in C#
   is negligible (0.2–1.4 ms per 100). Caches matter a great deal: identical descriptors are deduplicated (1 ms per
   100), and recreating released pipelines with the same shaders within a session takes 2.7 ms per 100. In an
   exploratory run, before unique shader constants were added, shader texts from an *earlier browser process with a
   fresh profile* compiled in 30–35 ms per 100. That points to a cross-process (probably OS Metal) shader cache.
   SwiftShader compiles faster (≈1 ms per pipeline sync) but rasterizes slowly (15 ms for the 1000-draw frame).
7. **Queue round trips are cheap but asynchronous.** `onSubmittedWorkDone` takes about 0.13–0.18 ms when idle and
   about 1.1 ms after a realistic frame. `mapAsync` on a small buffer takes about 0.06 ms. These calls can only be
   awaited, never blocked on, from the main thread.
8. **Headless WebGPU on macOS needs no flags** in `--headless=new` (Edge 155, CfT 151): the hardware Metal adapter
   is available. The old headless shell has no adapter by default; `--use-angle=metal` restores Metal, and
   `--enable-unsafe-webgpu` alone gives SwiftShader. Forcing SwiftShader
   (`--enable-unsafe-webgpu --use-webgpu-adapter=swiftshader --enable-unsafe-swiftshader`) works everywhere and
   passes the pixel checks, which makes it usable for CI without a GPU. In `--headless=new` rAF runs at 60 Hz; in
   headless-shell it runs at 120 Hz.
9. **All canvas readback approaches work in headless Chromium.** These are `copyTextureToBuffer` from
   `getCurrentTexture()` (this needs `usage: COPY_SRC` in `configure`), `canvas.toDataURL()` (same task *and* after
   presentation), and the CDP screenshot. All gave identical pixels. Use the GPU copy as the authoritative check,
   because it does not depend on compositor behaviour.
10. **WebGPU semantics leak through.** `queue.writeBuffer` on a buffer that is still mapped is a validation error
    ("used in submit while mapped"), so initial uploads to `mappedAtCreation` buffers must go through
    `getMappedRange` + copy + `unmap`. `GPUBufferUsage` bits differ from `VkBufferUsageFlags`, so the C# layer needs
    a translation table.[^webgpu-spec]

# Implications for the Vulkan-on-WebGPU layer design

- **Handles.** Keep every WebGPU object in a JS handle table and expose it to C# as integers. A Vulkan handle (`nint`)
  can carry the table index directly, with 0 = `VK_NULL_HANDLE`. Use reserved slots for per-frame swapchain views.
  Avoid `JSObject` on hot paths and for transient objects. If use-after-free debugging is wanted, add generation bits
  in the high bits (JS numbers are exact to 2^53, but an `int` signature caps the value at 2^31).
- **Record in C#, replay at submit.** `vkCmd*` should append records to a C#-owned arena per `VkCommandBuffer`. That
  costs about 60 ns per record pair in the interpreter, and AOT should be cheaper.
  `vkQueueSubmit` should then make **one** JSImport call that replays every submitted command buffer into fresh
  `GPUCommandEncoder`s. This keeps Vulkan's "record once, submit many" semantics (WebGPU encoders are single-use),
  and it puts the per-draw interop cost at the JS-replay level (≈ pure JS). The design in
  [vulkan-to-webgpu-command-mapping](/webgpu/vulkan-to-webgpu-command-mapping.md) and
  [performance-and-batching-design](/webgpu/performance-and-batching-design.md) is confirmed by measurement.
  A rough linear extrapolation (not measured) puts 10k draws per frame at about 2.3 ms batched versus about
  6 ms with individual calls, out of a 16.7 ms budget.
- **Memory.** Host-visible `VkDeviceMemory` lives in the wasm heap (`NativeMemory`). Flushes and submits become
  `WRITE_BUFFER` records (pointer + length) inside the same stream, so uploads cost no extra interop. JS must read
  the heap through a freshly fetched `localHeapViewU8()`. Do not use `Span`/`MemoryView` for data.
- **Descriptors.** JSON is fine for rare objects such as pipelines, layouts and render passes, because the JSON
  cost is negligible next to compilation. Fixed binary structs suit anything per frame. Enums cross the boundary
  as ints and are mapped through tables on the JS side.
- **Pipelines.** Map `vkCreateGraphicsPipelines`/`vkCreateComputePipelines` to `createRenderPipelineAsync` and return
  the handle at once, backed by a pending promise. Sync creation would stall the GPU queue at about 6.5 ms per
  pipeline. This creates a "pipeline not ready" state the layer must handle. Options: an explicit warm-up/await
  point (for example a `VkPipelineCache` pre-warm or an app-loop yield), or deferring draws until ready. Because of
  dedup and the shader caches, warm-ups are cheap from the second time on.
- **Synchronization.** `vkQueueWaitIdle`/`vkWaitForFences` cannot block. Fences should map to
  `onSubmittedWorkDone` promises (about 0.15 ms when idle, about 1 ms after a frame), and the C# main loop must yield
  to the event loop (see [synchronization](/webgpu/synchronization-and-async-shape.md)).
- **Testing.** CI can use Edge or CfT `--headless=new` (hardware) or forced SwiftShader, with the GPU-copy pixel check
  from this experiment as the assertion.

# Open questions

- **AOT.** How much do `wasm-tools` AOT builds cut the ≈150 ns JSImport cost and the ≈60 ns-per-draw C# encode?
  This was not measurable here because workload installs were out of scope.
- **Cheaper boundaries.** Would direct Emscripten/wasm imports (a relinked native runtime, `EM_JS`) beat JSImport for
  the single replay call, or for unmanaged function-pointer entry points from `Brutal.Vulkan`? See
  [function pointers](/dotnet-wasm/function-pointers-and-native-callbacks.md).
- **Threads.** With the multithreaded runtime (SharedArrayBuffer heap), do `writeBuffer` and `setBindGroup` accept
  views over shared memory, and can replay move into a worker with `OffscreenCanvas`?
- **Scaling.** Does the replay stay linear at 10k–100k records? Does the per-frame
  `getCurrentTexture().createView()` and descriptor allocation in JS cause GC jank over long runs?
- **The `c1` bimodality.** Why does the individual-call path sometimes take about 1.1–1.3 ms instead of 0.6 ms, when
  the stream path stays stable? Candidates are power state and jiterpreter trace compile timing.
- **Not-ready pipelines.** What exact policy should apply in Vulkan terms (block the app's frame loop, skip draws,
  or require a warm-up)? Is a cross-session cache (Chrome's persistent Dawn cache or the OS Metal cache)
  reliable enough to count on in production?
- **Other browsers.** How do Firefox and Safari WebGPU behave in this setup (headless availability, `toDataURL`,
  call costs)? Only Chromium-family browsers on one Apple-silicon Mac were measured.

[^project]: exp-03 project README (build, serve, test, flag table).
[^results]: exp-03 RESULTS.md: all runs, flag matrix, primary console log.
[^glue]: `webgpu-glue.js`: handle table, heap reads, pipeline JSON resolver, `replayCommandStream`.
[^cs-binding]: `WebGpu.cs`, `CommandStream.cs`, `Program.cs`: C# side of the binding and the benchmarks.
[^ms-dotnet-interop]: Microsoft Learn, JavaScript `[JSImport]`/`[JSExport]` interop in .NET WebAssembly (.NET 10): type mappings (`IntPtr`→`Number`, `Span`→`MemoryView`), MemoryView lifetime, performance considerations.
[^ms-wasm-browser-app]: Microsoft Learn, `[JSImport]`/`[JSExport]` interop with a WebAssembly Browser App project (`dotnet.create`, `setModuleImports`, `getAssemblyExports`, `runMain`).
[^dotnet-d-ts]: `dotnet.d.ts` from runtime pack 10.0.1: `RuntimeAPI.Module`, `localHeapView*()` ("short term view … don't use it after await"), `getHeap*`, `IMemoryView`.
[^webgpu-spec]: W3C WebGPU CRD 2026-09-15: `GPUQueue.writeBuffer`, `createRenderPipelineAsync` (`#dom-gpudevice-createrenderpipelineasync`), `onSubmittedWorkDone` (`#dom-gpuqueue-onsubmittedworkdone`), `setBindGroup` with `Uint32Array` dynamic offsets, `GPUBufferUsage` flags.
[^puppeteer-launcher]: puppeteer-core 25.14.0 `ChromeLauncher.js`: `headless: true` pushes `--headless=new`, `'shell'` pushes `--headless`; default args include `--force-color-profile=srgb`.
