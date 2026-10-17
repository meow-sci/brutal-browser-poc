---
type: Research Note
title: Performance on .NET 10 browser-wasm — interpreter, jiterpreter, Mono AOT, size trade-offs and profiling
description: Published numbers and mechanisms behind interpreter vs jiterpreter vs Mono AOT performance on browser-wasm, how AOT mixes with the interpreter, size costs (WasmStripILAfterAOT, relink, ICU), optimisation flags, profiling tools, and concrete recommendations for a C# Vulkan-on-WebGPU frame loop.
tags: [dotnet-wasm, performance, aot, jiterpreter, interpreter, size, profiling, emcc, game-loop]
sources:
  - id: ms-build-tools-aot
    resource: https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot?view=aspnetcore-10.0
    title: Blazor WebAssembly build tools and AOT compilation (.NET 10)
  - id: ms-runtime-perf
    resource: https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/webassembly-runtime-performance?view=aspnetcore-10.0
    title: Blazor WebAssembly runtime performance (.NET 10)
  - id: ms-dotnet-interop
    resource: https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/?view=aspnetcore-10.0
    title: JSImport/JSExport interop in .NET WebAssembly (performance section)
  - id: devblogs-net8p2
    resource: https://devblogs.microsoft.com/dotnet/asp-net-core-updates-in-dotnet-8-preview-2/
    title: .NET Blog — ASP.NET Core updates in .NET 8 Preview 2 (Jiterpreter numbers)
  - id: devexpress-aot
    resource: https://community.devexpress.com/blogs/aspnet/archive/2023/03/16/blazor-webassembly-aot-compilation-and-link-trimming.aspx
    title: DevExpress — Blazor WebAssembly AOT Compilation and Link Trimming (.NET 7 measurements)
  - id: jiterpreter-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/docs/design/mono/jiterpreter.md
    title: The Jiterpreter (design doc)
  - id: wasm-aot-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/docs/design/mono/wasm-aot.md
    title: WebAssembly AOT code generation (design doc)
  - id: profiled-aot-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/docs/design/mono/profiled-aot.md
    title: Profiled Ahead-Of-Time Compilation on Mono (design doc)
  - id: wasm-features-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/features.md
    title: Configuring and hosting .NET WebAssembly applications (release/10.0)
  - id: wasmapp-common-targets
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.sdk/Sdk/WasmApp.Common.targets
    title: WasmApp.Common.targets 10.0.13 (AOT task arguments, profilers, errors)
  - id: browserwasmapp-targets
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.sdk/Sdk/BrowserWasmApp.targets
    title: BrowserWasmApp.targets 10.0.13 (optimisation flag defaults, WasmStripILAfterAOT)
  - id: probe-build
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/proj/WasmProbe
    title: Throwaway wasmbrowser publish (sizes; debugLevel in Debug vs Release)
  - id: sig-classification
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/sig-classification.txt
    title: Brutal.Vulkan call-path classification
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:09:23Z }
status: draft
---

# Execution tiers and what they cost

| Tier | When | Speed (published evidence) | Size / build cost |
|---|---|---|---|
| Interpreter | always available; Debug builds run only this (debugging disables the jiterpreter) | baseline; "apps typically run slower than they would on a server-side .NET JIT runtime"[^ms-build-tools-aot] | no native toolchain |
| Interpreter + jiterpreter | published (non-debug) single-threaded builds | .NET 8 Preview 2: `Span<byte>.Reverse()` +46.7%, `String.Normalize()` +86.9%, JSON serialization +40.8%[^devblogs-net8p2] | small runtime memory overhead; traces capped at about 4 KB modules[^jiterpreter-md] |
| Mono AOT (`RunAOTCompilation`) + interpreter fallback | `dotnet publish -c Release` with `wasm-tools` | "dramatic improvements for apps that execute CPU-intensive tasks"[^ms-build-tools-aot]. DevExpress (.NET 7, DxGrid): filter 100k rows 220→117 ms, select row 320→85 ms, paging 900→570 ms, sort 100k rows 1000→950 ms[^devexpress-aot] | "most AOT-compiled apps are about twice the size". DevExpress transfer size 16.2→34 MB (trimmed). Minutes of build time[^ms-build-tools-aot][^devexpress-aot] |

No published .NET 10-specific interpreter-vs-AOT benchmark was found for game-like workloads (tight math loops, struct-heavy code, many small calls). UNVERIFIED: expected AOT speed-ups for such code are much larger than the UI numbers above. Measure in [/experiments/](/experiments/).

## Measure correctly

- `dotnet build` (Debug) writes `"debugLevel": -1` into the boot config, i.e. debugging enabled, which turns the jiterpreter off.[^probe-build][^jiterpreter-md] `dotnet publish -c Release` writes `"debugLevel": 0`. **Benchmark only published Release output**, or force `WasmDebugLevel=0`.
- Hot-reload support is injected into Debug builds (`Microsoft.DotNet.HotReload.WebAssembly.Browser`); see [/dotnet-wasm/project-types-and-build-output.md](/dotnet-wasm/project-types-and-build-output.md).

# How Mono AOT works on wasm (and where it falls back)

- `AOTMode=LLVMOnlyInterp`. Assemblies are compiled with Mono's LLVM backend to bitcode, then by `emcc` into `dotnet.native.wasm`. Default AOT arguments: `no-opt;static;direct-icalls;deterministic;compile-in-child` (+`mattr=simd` with SIMD; `--wasm-exceptions` with EH; `--wasm-gc-safepoints` with threads).[^wasmapp-common-targets]
- **The interpreter stays in the binary.** Calls AOT→interpreter go through per-signature wrappers. Methods the AOT compiler cannot handle, and EH paths, run interpreted.[^wasm-aot-md]
- Known code-quality limits, from the runtime team: every AOT method's address is taken (no dead-code elimination across the image); explicit null checks; lazily initialised vtable slots; GC references pinned in a per-method area; **exceptions are thrown as C++/wasm exceptions and the catching method "deopts" into the interpreter**.[^wasm-aot-md] Keep exceptions out of hot paths. BRUTAL wrappers throw `VkResultError` only on error codes, which is fine.
- `WasmStripILAfterAOT=true` by default: IL bodies of AOT-compiled methods are removed after compilation. Turn it off if the app breaks.[^ms-runtime-perf][^browserwasmapp-targets]
- `WasmDedup=true` deduplicates generic instances into one `aot-instances` image.[^wasmapp-common-targets]
- Profiled AOT: a `.mibc` profile (`AOTProfilePath`/`WasmAotProfilePath`) restricts AOT to profiled methods. That is a size/speed middle ground ("leverages profiles to select which CIL code to AOT rather than an all-or-nothing approach").[^profiled-aot-md][^wasmapp-common-targets] UNVERIFIED: how practical generating a browser profile is in .NET 10 (`WasmProfilers=aot` links `libmono-profiler-aot.a`).
- Errors you will see if prerequisites are missing: `AOT is not supported without IL trimming (PublishTrimmed=true required).`, `AOT is not supported in debug configuration (Configuration=Release required).`, and, without the workload, `NETSDK1147 … wasm-tools`.[^wasmapp-common-targets][^probe-build]

# Size levers

| Lever | Effect | Needs relink |
|---|---|---|
| Trimming (`PublishTrimmed=true`, `TrimMode=full`, default for the WebAssembly SDK) | the minimal app shrinks to 4 assemblies (CoreLib 1.15 MB raw / 366 KB br)[^probe-build] | no |
| Relink (Release publish with `wasm-tools`) | drops unused runtime code and icalls (`WasmLinkIcalls`) from `dotnet.native.wasm` (3.0 MB raw / 977 KB br prebuilt)[^probe-build][^ms-runtime-perf] | — |
| `InvariantGlobalization=true` | removes ICU (`icudt_*.dat`, 144–249 KB br per shard) and ICU code on relink | partially |
| `InvariantTimezone=true` | removes the bundled TZ database | yes |
| `WasmNativeStrip=true` (default) | strips names/DWARF (`wasm-opt --strip-dwarf`) | — |
| `EmccLinkOptimizationFlag` | default `-O2` in Release; `-Oz` trades speed for size | yes |
| `WasmStripILAfterAOT` | smaller assemblies with AOT | AOT |
| Compression | publish emits `.br`/`.gz`. Serve brotli (about 3.5× smaller for the measured app: 4.60 MB → 1.46 MB without ICU) | no |

[^probe-build][^browserwasmapp-targets][^ms-runtime-perf]

# Profiling and diagnostics

- Browser DevTools performance panel: wasm frames are named when symbols are kept. The runtime pack ships `dotnet.native.js.symbols`; `WasmEmitSymbolMap=true` regenerates it on relink. `WasmNativeDebugSymbols=true` + `WasmNativeStrip=false` keep DWARF.[^wasm-features-md]
- `WasmProfilers=browser:callspec=N:MyNamespace` (or `all`) emits .NET method markers into the DevTools timeline. **Not compatible with AOT** (`$(WasmProfilers)=… is not supported together with $(RunAOTCompilation)`) and not with `EnableDiagnostics`.[^wasm-features-md][^wasmapp-common-targets]
- `EnableDiagnostics=true` (+ optional `WasmPerformanceInstrumentation=all|N:Ns`) adds EventPipe. In the console, `getDotnetRuntime(0).collectCpuSamples({durationSeconds: 60})`, `collectGcDump()` and `collectMetrics()` produce `.nettrace` for PerfView, Visual Studio or `dotnet-trace report`.[^wasm-features-md]
- Jiterpreter stats: runtime option `--jiterpreter-stats-enabled`; dump with `INTERNAL.jiterpreter_dump_stats()`.[^jiterpreter-md]
- `Timing-Allow-Origin` gives more precise timings.[^wasm-features-md]

# Recommendations for the Vulkan-on-WebGPU frame loop

1. **Minimise boundary crossings per frame.** A `[JSImport]` call costs roughly 10× a managed call (~254 ns in Microsoft's JSObject benchmark).[^ms-dotnet-interop] Batch Vulkan command recording into a byte stream in wasm memory and flush it with one call per submit. JS decodes the stream into WebGPU calls.
2. **Avoid unmanaged round trips inside the C# layer.** In the interpreter, every `delegate* unmanaged` → `[UnmanagedCallersOnly]` call crosses interpreter → native wrapper → wasm table → C thunk → interpreter. A source build that uses managed `delegate*` stays inside the interpreter (see [/dotnet-wasm/function-pointers-and-native-callbacks.md](/dotnet-wasm/function-pointers-and-native-callbacks.md)). UNVERIFIED: the magnitude; measure both.[^sig-classification]
3. **Avoid per-call allocations crossing interop**: no `JSObject`, `Task`, `ArraySegment`, `string` or array marshalling on hot paths. Pass integers and `(ptr, len)`.
4. **Use AOT for performance milestones**, the interpreter (with the jiterpreter, published) for iteration. Expect AOT builds to take minutes and roughly double the payload.
5. Prefer `Vector128`/`System.Numerics` (accelerated via wasm SIMD); avoid 256-bit vector paths.
6. Keep exception-throwing out of per-frame code (AOT deopt cost; interpreter EH is also slow).

## Open questions

- UNVERIFIED: interpreter vs jiterpreter vs AOT speed for BRUTAL-style math, struct copies and function-pointer calls on .NET 10.
- UNVERIFIED: AOT and relinked sizes for the PoC (requires `wasm-tools`).
- UNVERIFIED: whether profiled AOT (`.mibc`) can be generated from a browser session in .NET 10.

[^ms-build-tools-aot]: Microsoft Learn — Blazor WebAssembly build tools and AOT (.NET 10)
[^ms-runtime-perf]: Microsoft Learn — Blazor WebAssembly runtime performance (.NET 10)
[^ms-dotnet-interop]: Microsoft Learn — JSImport/JSExport interop performance section
[^devblogs-net8p2]: .NET Blog — ASP.NET Core updates in .NET 8 Preview 2
[^devexpress-aot]: DevExpress blog — Blazor WebAssembly AOT and link trimming (2023-03-16)
[^jiterpreter-md]: dotnet/runtime docs/design/mono/jiterpreter.md
[^wasm-aot-md]: dotnet/runtime docs/design/mono/wasm-aot.md
[^profiled-aot-md]: dotnet/runtime docs/design/mono/profiled-aot.md
[^wasm-features-md]: dotnet/runtime src/mono/wasm/features.md (release/10.0)
[^wasmapp-common-targets]: WasmApp.Common.targets 10.0.13
[^browserwasmapp-targets]: BrowserWasmApp.targets 10.0.13
[^probe-build]: WasmProbe build/publish measurements, 2026-10-16
[^sig-classification]: Brutal.Vulkan call-path classification (scratchpad r1)
