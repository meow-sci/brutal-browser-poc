---
type: Technology Assessment
title: .NET 10 browser-wasm runtime overview (Mono interpreter, jiterpreter, AOT, SIMD, EH, threads, memory, size)
description: What actually executes C# in the browser on .NET 10 (Mono runtime compiled with Emscripten 3.1.56), its execution modes, post-MVP wasm features, threading status, memory limits, globalization/trimming defaults and measured payload sizes, with the MSBuild switches that control each.
tags: [dotnet-wasm, mono, net10, interpreter, jiterpreter, aot, simd, exceptions, threads, memory, trimming, icu, webcil]
sources:
  - id: emscripten-manifest
    resource: /usr/local/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.emscripten.current/10.0.101/WorkloadManifest.json
    title: Emscripten workload manifest 10.0.101 (aliases to Microsoft.NET.Runtime.Emscripten.3.1.56.*)
  - id: mono-toolchain-manifest
    resource: /usr/local/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.mono.toolchain.current/10.0.101/WorkloadManifest.json
    title: Mono toolchain workload manifest 10.0.101 (wasm-tools, wasm-experimental)
  - id: mono-toolchain-targets
    resource: /usr/local/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.mono.toolchain.current/10.0.101/WorkloadManifest.targets
    title: Mono toolchain WorkloadManifest.targets 10.0.101 (runtime pack version pin, multithread pack switch)
  - id: wasm-features-props
    resource: /usr/local/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.mono.toolchain.current/10.0.101/WasmFeatures.props
    title: WasmFeatures.props (feature-switch defaults for browser-wasm)
  - id: wasm-props-json
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.netcore.app.runtime.mono.browser-wasm/runtimes/browser-wasm/native/src/wasm-props.json
    title: Runtime pack 10.0.13 wasm-props.json (RuntimeEmccVersion 3.1.56, relink-triggering defaults)
  - id: emcc-link-rsp
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.netcore.app.runtime.mono.browser-wasm/runtimes/browser-wasm/native/src/emcc-link.rsp
    title: Runtime pack 10.0.13 emcc-link.rsp (INITIAL_MEMORY, STACK_SIZE, ALLOW_MEMORY_GROWTH, …)
  - id: wasm-config-h
    resource: /Users/asherwin/.nuget/packages/microsoft.netcore.app.runtime.mono.browser-wasm/10.0.1/runtimes/browser-wasm/native/include/wasm/wasm-config.h
    title: wasm-config.h (DISABLE_THREADS in the default runtime pack)
  - id: browserwasmapp-targets
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.sdk/Sdk/BrowserWasmApp.targets
    title: BrowserWasmApp.targets (Microsoft.NET.Runtime.WebAssembly.Sdk 10.0.13)
  - id: wasmapp-common-targets
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.sdk/Sdk/WasmApp.Common.targets
    title: WasmApp.Common.targets (Microsoft.NET.Runtime.WebAssembly.Sdk 10.0.13)
  - id: illink-subst
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.sdk/Sdk/ILLink.Substitutions.WasmIntrinsics.xml
    title: ILLink substitutions for SIMD on/off (Vector/Vector128/PackedSimd)
  - id: sdk-wasm-props
    resource: /usr/local/share/dotnet/sdk/10.0.100/Sdks/Microsoft.NET.Sdk.WebAssembly/Sdk/Sdk.props
    title: Microsoft.NET.Sdk.WebAssembly Sdk.props in SDK 10.0.100
  - id: wasm-sdk-pack-props
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.sdk.webassembly.pack/build/Microsoft.NET.Sdk.WebAssembly.Browser.props
    title: Microsoft.NET.Sdk.WebAssembly.Pack 10.0.13 Browser.props (TrimMode defaults)
  - id: wasm-features-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/features.md
    title: Configuring and hosting .NET WebAssembly applications (dotnet/runtime release/10.0)
  - id: wasm-threads-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/threads.md
    title: Threaded runtime notes (dotnet/runtime release/10.0)
  - id: jiterpreter-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/docs/design/mono/jiterpreter.md
    title: The Jiterpreter (dotnet/runtime design doc)
  - id: wasm-aot-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/docs/design/mono/wasm-aot.md
    title: WebAssembly AOT code generation (dotnet/runtime design doc)
  - id: webcil-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/docs/design/mono/webcil.md
    title: WebCIL assembly format (dotnet/runtime design doc)
  - id: ms-build-tools-aot
    resource: https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot?view=aspnetcore-10.0
    title: Blazor WebAssembly build tools and AOT compilation (.NET 10)
  - id: monitor-c
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/mono/metadata/monitor.c
    title: Mono monitor.c (DISABLE_THREADS infinite wait → PNSE)
  - id: thread-ref
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Threading.Thread/ref/System.Threading.Thread.cs
    title: System.Threading.Thread reference assembly source ([UnsupportedOSPlatform("browser")] on Start)
  - id: probe-build
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/proj/WasmProbe
    title: Throwaway wasmbrowser project (SDK 10.0.100, runtime pack 10.0.1, no wasm-tools) — measured outputs
  - id: nuget-versions
    resource: https://api.nuget.org/v3-flatcontainer/microsoft.netcore.app.runtime.mono.browser-wasm/index.json
    title: NuGet version index for Microsoft.NETCore.App.Runtime.Mono.browser-wasm (queried 2026-10-16; latest 10.0.13)
  - id: issue-121511
    resource: https://github.com/dotnet/runtime/issues/121511
    title: 'dotnet/runtime tracking issue ".NET WebAssembly on CoreCLR interpreter"'
  - id: maui-coreclr-blog
    resource: https://devblogs.microsoft.com/dotnet/dotnet-maui-moves-to-coreclr-in-dotnet-11/
    title: .NET Blog — .NET MAUI moves to CoreCLR in .NET 11 ("WASM continues to use Mono")
  - id: devblogs-net8p2
    resource: https://devblogs.microsoft.com/dotnet/asp-net-core-updates-in-dotnet-8-preview-2/
    title: ASP.NET Core updates in .NET 8 Preview 2 (Jiterpreter introduction)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T15:59:39Z }
status: draft
---

# Summary

| Aspect | .NET 10 fact | Evidence |
|---|---|---|
| Runtime | **Mono** (`Microsoft.NETCore.App.Runtime.Mono.browser-wasm`), compiled to `dotnet.native.wasm` with Emscripten | [^mono-toolchain-manifest] |
| Emscripten pinned by .NET 10 | **3.1.56** (`emcc … 3.1.56 (57b21b8fdcbe3ebb523178b79465254668eab408)`); workload packs `Microsoft.NET.Runtime.Emscripten.3.1.56.{Sdk,Node,Python,Cache}.<rid>` | [^wasm-props-json][^emscripten-manifest] |
| Default execution | IL **interpreter** plus the **jiterpreter** (interpreter traces compiled to small wasm modules at run time) | [^jiterpreter-md][^ms-build-tools-aot] |
| AOT | Mono LLVM AOT, `RunAOTCompilation=true`, **publish only**, requires `wasm-tools`; mode `LLVMOnlyInterp` (AOT plus interpreter fallback) | [^wasmapp-common-targets] |
| SIMD / EH | Both **on** by default (`-msimd128`, `-fwasm-exceptions`); turning either off requires a relink | [^browserwasmapp-targets][^wasm-props-json] |
| Threads | Single-threaded by default (`DISABLE_THREADS`). `WasmEnableThreads=true` selects a separate **experimental** multithread runtime pack | [^wasm-config-h][^mono-toolchain-targets][^wasm-features-md] |
| Pointer size | wasm32: `IntPtr.Size == 4`, 4 GB address-space ceiling; the default max heap is **2 GB** | [^wasm-props-json] |
| Runtime pack version actually used here | **10.0.1** (pinned by workload manifest 10.0.101 via `_RuntimePackInWorkloadVersionCurrent`), although NuGet has 10.0.13 | [^mono-toolchain-targets][^probe-build][^nuget-versions] |

# The runtime pack and how its version is chosen

- The browser runtime is a NuGet *runtime pack*. `WorkloadManifest.targets` from the installed **mono toolchain manifest** sets its version (`LatestRuntimeFrameworkVersion`) for `net10.0`, **even when the `wasm-tools` workload is not installed**. On this Mac, SDK 10.0.100 with manifest 10.0.101 resolves `Microsoft.NETCore.App.Runtime.Mono.browser-wasm` **10.0.1** and `Microsoft.NET.Sdk.WebAssembly.Pack` **10.0.1**. The NuGet feed has 10.0.13 (template package `Microsoft.NET.Runtime.WebAssembly.Templates.net10` is also at 10.0.13).[^mono-toolchain-targets][^probe-build][^nuget-versions] To get a newer runtime, update the SDK or workload manifests (`dotnet workload update`), not a package reference.
- The same manifest switches the runtime pack name to `Microsoft.NETCore.App.Runtime.Mono.multithread.**RID**` when `WasmEnableThreads=true`.[^mono-toolchain-targets]

# Execution modes

## Interpreter (default)

All IL runs in Mono's interpreter (`libmono-ee-interp.a`), which is compiled into the prebuilt `dotnet.native.wasm` (about 3.0 MB uncompressed in 10.0.x).[^probe-build] No native toolchain is needed. `System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported` is `true` in the generated runtimeconfig, so `Reflection.Emit` output is interpreted.[^probe-build] Interpreter↔native transitions need build-time trampolines; see [/dotnet-wasm/function-pointers-and-native-callbacks.md](/dotnet-wasm/function-pointers-and-native-callbacks.md).

## Jiterpreter

- "A just-in-time compiler for the Mono Interpreter component." It has three parts: a trace compiler (hot interpreter opcode sequences → wasm functions), interp_entry thunks and jit_call thunks. The two thunk kinds are relevant only to AOT.[^jiterpreter-md]
- Traces are "On by default for single-threaded builds; disabled if threading is active." Browsers cap synchronously compiled modules at about 4 KB, so traces are small.[^jiterpreter-md]
- "At present, the Jiterpreter only functions in Blazor applications that have been published. When running with debugging enabled, it will be inactive."[^jiterpreter-md] A Debug build of the template writes `"debugLevel": -1` into the boot config (debugging on), while publish writes `0`.[^probe-build] **Always measure performance on `dotnet publish -c Release` output.**
- Configure it with `BlazorWebAssemblyJiterpreter` (MSBuild, honoured by `Microsoft.NET.Sdk.WebAssembly`), `BlazorWebAssemblyRuntimeOptions`, or JS `dotnet.withRuntimeOptions(["--no-jiterpreter-traces-enabled"])`.[^jiterpreter-md]
- Microsoft's .NET 8 Preview 2 micro-benchmarks: `Span<byte>.Reverse()` 46.7% faster, `String.Normalize()` 86.9% faster, JSON serialization 40.8% faster.[^devblogs-net8p2]

## AOT (Mono LLVM)

- `RunAOTCompilation=true` compiles each assembly to LLVM bitcode, then wasm. Defaults: `AOTMode=LLVMOnlyInterp`, `WasmDedup=true` (generic instances deduplicated into `aot-instances.dll`), `WasmStripILAfterAOT=true` (IL bodies removed after AOT).[^wasmapp-common-targets][^browserwasmapp-targets]
- Hard requirements enforced by targets: `AOT is not supported without IL trimming (PublishTrimmed=true required).` and `AOT is not supported in debug configuration (Configuration=Release required).`[^wasmapp-common-targets] AOT runs only on publish unless `RunAOTCompilationAfterBuild=true`.
- Design limits from the runtime team: wasm cannot walk the stack, so **exceptions are implemented with C++/wasm exceptions and caught code "deopts" into the interpreter**. Every AOT method gets a GC pin area. Null checks are explicit. Vtable slots are lazily initialised. Every AOT method's address is taken, which limits LLVM's dead-code elimination.[^wasm-aot-md]
- Size: Microsoft says "most AOT-compiled apps are about twice the size of their IL-compiled versions."[^ms-build-tools-aot] More in [/dotnet-wasm/performance-and-aot.md](/dotnet-wasm/performance-and-aot.md).

# Post-MVP WebAssembly features

| Feature | Property | Default (.NET 10) | Notes |
|---|---|---|---|
| SIMD (128-bit) | `WasmEnableSIMD` | `true` (defaults to the value of `WasmEnableExceptionHandling`) | ILLink substitutions hard-wire `Vector.IsHardwareAccelerated`, `Vector128.IsHardwareAccelerated` and `PackedSimd.IsSupported` to `true` (or `false` when SIMD is off). Changing it relinks.[^browserwasmapp-targets][^illink-subst] |
| Exception handling | `WasmEnableExceptionHandling` | `true` (`-fwasm-exceptions`; `libmono-wasm-eh-wasm.a`) | `false` falls back to JS-based EH (`-s DISABLE_EXCEPTION_CATCHING=0`, `libmono-wasm-eh-js.a`) and relinks.[^browserwasmapp-targets] |
| BigInt | always (`-s WASM_BIGINT=1`) | — | needed for `long` interop.[^emcc-link-rsp][^wasm-features-md] |
| Threads | `WasmEnableThreads` | `false` | experimental; see below |

`Vector256`/`Vector512` acceleration: wasm SIMD is 128-bit only. UNVERIFIED: whether `Vector256.IsHardwareAccelerated` reports `false` on browser-wasm (expected; no substitution file covers it).

# Threading status in .NET 10

- The default runtime pack is built with `DISABLE_THREADS` (`wasm-config.h`: "Support for threads is disabled").[^wasm-config-h] Everything runs on the browser's main thread, including the thread pool, timers and finalizers, which are scheduled as "background jobs".[^wasm-threads-md]
- The single-threaded reference assemblies mark `Thread.Start` `[UnsupportedOSPlatform("browser")]`.[^thread-ref] An infinite `Monitor.Wait` (which is what `Task.Wait()`/`.Result` on an incomplete task reach) throws `PlatformNotSupportedException: Cannot wait on monitors on this runtime.`[^monitor-c]
- `WasmEnableThreads=true` is "the multi-threading experiment … currently disabled by default", and "JavaScript interop … via [JSExport]/[JSImport] is currently limited to the main thread even if multi-threading support is enabled".[^wasm-features-md] It needs `Cross-Origin-Embedder-Policy: require-corp` + `Cross-Origin-Opener-Policy: same-origin` (`dotnet run` passes `--apply-cop-headers` to the dev host).[^wasm-features-md][^wasmapp-common-targets]
- The `wasm-experimental` workload ships `Microsoft.NETCore.App.Runtime.Mono.multithread.net10.browser-wasm`.[^mono-toolchain-manifest] **Observed:** with `Microsoft.NET.Sdk.WebAssembly` and **no** workload, `dotnet build -p:WasmEnableThreads=true` succeeded. It pulled `microsoft.netcore.app.runtime.mono.multithread.browser-wasm` from NuGet and emitted `dotnet.native.worker.<hash>.mjs` (`dotnet.native.wasm` 3,090,662 bytes vs 3,001,957 single-threaded).[^probe-build] UNVERIFIED: runtime behaviour of that build in a browser.
- With threads, `WasmEnableJsInteropByValue` defaults to `true` and jiterpreter traces are off.[^browserwasmapp-targets][^jiterpreter-md]
- **Recommendation for the PoC: single-threaded.** See [/dotnet-wasm/main-loop-threading-and-pitfalls.md](/dotnet-wasm/main-loop-threading-and-pitfalls.md).

# Memory

| Setting | Default | Source / property | Needs relink? |
|---|---|---|---|
| Initial linear memory | 32 MiB (`-s INITIAL_MEMORY=33554432`); a relink computes `max(32 MiB, assemblies + AOT data)` | `EmccInitialHeapSize` (alias `WasmInitialHeapSize`, legacy `EmccTotalMemory`) | yes |
| Maximum memory | 2 GiB (`EmccMaximumHeapSize` default 2147483648 in `wasm-props.json`) | `EmccMaximumHeapSize` → `-s MAXIMUM_MEMORY` | yes |
| Growth | `-s ALLOW_MEMORY_GROWTH=1`, `-s ALLOW_TABLE_GROWTH=1` | runtime-pack `emcc-link.rsp` | — |
| C stack | 5 MB (`-s STACK_SIZE=5MB`) | `EmccStackSize` | yes |

[^emcc-link-rsp][^wasm-props-json][^browserwasmapp-targets][^wasmapp-common-targets]

Guidance from the runtime team: desktop apps should stay between 256 and 512 MB; "Using more than 2GB is experimental". Lower `EmccMaximumHeapSize` (e.g. 268435456) for iOS Safari.[^wasm-features-md] Memory growth replaces the `ArrayBuffer`, so JS typed-array views over wasm memory must be re-created after any call that may allocate. See [/dotnet-wasm/js-interop-jsimport-jsexport.md](/dotnet-wasm/js-interop-jsimport-jsexport.md).

# Globalization, time zones, trimming

- ICU is on by default and **sharded**: `icudt_EFIGS.dat`, `icudt_CJK.dat`, `icudt_no_CJK.dat` ship, and only the shard matching the user's locale downloads (`"globalizationMode": "sharded"`).[^probe-build][^wasm-features-md] `InvariantGlobalization=true` drops them. `InvariantTimezone=true` drops the bundled time-zone database (`wasm-bundled-timezones.a`) and needs a relink.[^browserwasmapp-targets]
- Trimming: `Microsoft.NET.Sdk.WebAssembly` sets `PublishTrimmed=true`. `TrimMode` is `full` for non-Blazor projects (`partial` for Blazor). `SuppressTrimAnalysisWarnings=true` unless `TrimmerDefaultAction=link`, so trim breakage is **silent by default**.[^sdk-wasm-props][^wasm-sdk-pack-props]
- Feature-switch defaults for browser-wasm (`WasmFeatures.props`): `EventSourceSupport=false`, `MetricsSupport=false`, `UseSystemResourceKeys=true` (exception messages become resource keys), `UseSizeOptimizedLinq=true`, `Http3Support=false`, `DebuggerSupport=false` outside Debug, `WasmEnableStreamingResponse=true`.[^wasm-features-props]

# Payload: what a minimal app downloads (measured)

`dotnet publish -c Release` of the `wasmbrowser` template (no workload, so no relink; trimming on; 4 assemblies remain), from `bin/Release/net10.0/publish/wwwroot/_framework`:[^probe-build]

| File | Raw bytes | Brotli bytes |
|---|---|---|
| `dotnet.native.<fp>.wasm` | 3,001,957 | 976,573 |
| `System.Private.CoreLib.<fp>.wasm` (Webcil) | 1,152,277 | 366,255 |
| `dotnet.runtime.<fp>.js` | 198,479 | 47,846 |
| `dotnet.native.<fp>.js` | 145,050 | 30,468 |
| `dotnet.<fp>.js` (loader + inlined boot config) | 41,947 | 11,661 |
| `System.Runtime.InteropServices.JavaScript` / `System.Console` / app | 40,213 / 14,613 / 8,981 | 15,702 / 6,153 / 3,719 |
| ICU shards (`EFIGS` / `no_CJK` / `CJK`; one is fetched) | 550,832 / 1,107,168 / 956,416 | 143,983 / 222,250 / 248,840 |
| **Total excluding ICU** | **4,603,517** | **1,458,377** |

So a hello-world downloads about 1.5 MB brotli plus one ICU shard (0.14–0.25 MB). A relink with `InvariantGlobalization` would shrink it further (not measured: no workload). Publish emits `.br` and `.gz` next to every file; build output gets `.gz` only.[^probe-build]

# Webcil

Assemblies ship as `.wasm` files in the **Webcil** format (`WasmEnableWebcil=true` by default), because "firewalls and anti-virus software may prevent browsers from downloading or caching assemblies with a .DLL extension and PE contents."[^webcil-md][^browserwasmapp-targets]

# Debugging and diagnostics (summary)

- `WasmDebugLevel`: >0 enables debugging with a log level; 0 disables debugging and enables interpreter optimisations; <0 enables debugging without logging.[^wasmapp-common-targets]
- Native symbols: `WasmNativeDebugSymbols=true` + `WasmNativeStrip=false` (DWARF in the wasm, Chrome DWARF extension); `WasmEmitSymbolMap=true` → `dotnet.native.js.symbols` (wasm function index → C name). The runtime pack already ships `dotnet.native.js.symbols` for the prebuilt binary.[^wasm-features-md][^probe-build]
- `EnableDiagnostics=true` (relink) adds the EventPipe-based diagnostics server. `getDotnetRuntime(0).collectCpuSamples()/collectGcDump()/collectMetrics()` produce `.nettrace` files. `WasmProfilers=browser:callspec=…` shows .NET methods in the DevTools performance timeline.[^wasm-features-md]
- C# step-debugging in Chromium works through the debug proxy configured by `inspectUri` in the template's `launchSettings.json` (see [/dotnet-wasm/project-types-and-build-output.md](/dotnet-wasm/project-types-and-build-output.md)).

# Outlook beyond .NET 10

Microsoft is "migrating .NET WebAssembly from the Mono runtime to CoreCLR, targeting an early preview in the .NET 11 timeframe … initial phase … CoreCLR interpreter … In the .NET 12 timeframe we will focus on stability, performance, and a seamless transition." The tracking issue lists "Relink runtime during application build" and a "Helper calls generator" as done.[^issue-121511] Microsoft's .NET 11 MAUI post is explicit for the shipping product: "WASM continues to use Mono, and that is not changing in .NET 11."[^maui-coreclr-blog] The NuGet feed still carries `Microsoft.NET.Runtime.Emscripten.3.1.56.*` packs through `11.0.0-preview.5`. UNVERIFIED: which Emscripten version .NET 11 GA uses. For this PoC, **.NET 10 means Mono**.

## Open questions

- UNVERIFIED: `Vector256.IsHardwareAccelerated` value on browser-wasm.
- UNVERIFIED: runtime behaviour of a `WasmEnableThreads=true` build produced without the workload.
- UNVERIFIED: size of a relinked, `InvariantGlobalization=true` hello-world, and of an AOT build (no workload installed here).
- UNVERIFIED: Emscripten version used by .NET 11 GA / CoreCLR-wasm.

[^emscripten-manifest]: microsoft.net.workload.emscripten.current 10.0.101 WorkloadManifest.json
[^mono-toolchain-manifest]: microsoft.net.workload.mono.toolchain.current 10.0.101 WorkloadManifest.json
[^mono-toolchain-targets]: microsoft.net.workload.mono.toolchain.current 10.0.101 WorkloadManifest.targets
[^wasm-features-props]: WasmFeatures.props 10.0.101
[^wasm-props-json]: Runtime pack wasm-props.json (10.0.13)
[^emcc-link-rsp]: Runtime pack emcc-link.rsp (10.0.13)
[^wasm-config-h]: Runtime pack wasm-config.h
[^browserwasmapp-targets]: BrowserWasmApp.targets 10.0.13
[^wasmapp-common-targets]: WasmApp.Common.targets 10.0.13
[^illink-subst]: ILLink.Substitutions.WasmIntrinsics.xml 10.0.13
[^sdk-wasm-props]: Microsoft.NET.Sdk.WebAssembly Sdk.props (SDK 10.0.100)
[^wasm-sdk-pack-props]: Microsoft.NET.Sdk.WebAssembly.Browser.props 10.0.13
[^wasm-features-md]: dotnet/runtime src/mono/wasm/features.md (release/10.0)
[^wasm-threads-md]: dotnet/runtime src/mono/wasm/threads.md (release/10.0)
[^jiterpreter-md]: dotnet/runtime docs/design/mono/jiterpreter.md
[^wasm-aot-md]: dotnet/runtime docs/design/mono/wasm-aot.md
[^webcil-md]: dotnet/runtime docs/design/mono/webcil.md
[^ms-build-tools-aot]: Microsoft Learn — Blazor WebAssembly build tools and AOT (.NET 10)
[^monitor-c]: Mono monitor.c (release/10.0)
[^thread-ref]: System.Threading.Thread ref source (release/10.0)
[^probe-build]: WasmProbe measured build/publish outputs (2026-10-16)
[^nuget-versions]: NuGet flat-container version index (2026-10-16)
[^issue-121511]: dotnet/runtime#121511
[^devblogs-net8p2]: .NET Blog — ASP.NET Core updates in .NET 8 Preview 2
[^maui-coreclr-blog]: .NET Blog — .NET MAUI moves to CoreCLR in .NET 11
