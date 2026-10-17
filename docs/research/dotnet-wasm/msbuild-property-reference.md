---
type: Reference
title: MSBuild property and item reference for .NET 10 browser-wasm builds (extracted from SDK, pack and workload targets)
description: Table of every MSBuild property and item relevant to building .NET 10 C# for the browser with Microsoft.NET.Sdk.WebAssembly and the wasm-tools workload, with .NET 10 defaults, the file that defines or consumes each, its effect, and whether it needs the workload or triggers a native relink.
tags: [dotnet-wasm, msbuild, reference, wasm-tools, emscripten, aot, properties]
sources:
  - id: sdk-wasm-props
    resource: /usr/local/share/dotnet/sdk/10.0.100/Sdks/Microsoft.NET.Sdk.WebAssembly/Sdk/Sdk.props
    title: "[SDK] Microsoft.NET.Sdk.WebAssembly Sdk.props (SDK 10.0.100)"
  - id: sdk-wasm-targets
    resource: /usr/local/share/dotnet/sdk/10.0.100/Sdks/Microsoft.NET.Sdk.WebAssembly/Sdk/Sdk.targets
    title: "[SDK] Microsoft.NET.Sdk.WebAssembly Sdk.targets (SDK 10.0.100)"
  - id: wasm-sdk-pack-props
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.sdk.webassembly.pack/build/Microsoft.NET.Sdk.WebAssembly.Browser.props
    title: "[PACK] Microsoft.NET.Sdk.WebAssembly.Pack 10.0.13 build/Microsoft.NET.Sdk.WebAssembly.Browser.props"
  - id: wasm-sdk-pack-targets
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.sdk.webassembly.pack/build/Microsoft.NET.Sdk.WebAssembly.Browser.targets
    title: "[PACK] Microsoft.NET.Sdk.WebAssembly.Pack 10.0.13 build/Microsoft.NET.Sdk.WebAssembly.Browser.targets"
  - id: browserwasmapp-targets
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.sdk/Sdk/BrowserWasmApp.targets
    title: "[BWA] Microsoft.NET.Runtime.WebAssembly.Sdk 10.0.13 Sdk/BrowserWasmApp.targets (GitHub: src/mono/browser/build/BrowserWasmApp.targets)"
  - id: wasmapp-common-targets
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.sdk/Sdk/WasmApp.Common.targets
    title: "[WAC] Microsoft.NET.Runtime.WebAssembly.Sdk 10.0.13 Sdk/WasmApp.Common.targets (GitHub: src/mono/wasm/build/WasmApp.Common.targets)"
  - id: wasm-sdk-sdkprops
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.sdk/Sdk/Sdk.targets
    title: "[BWA] Microsoft.NET.Runtime.WebAssembly.Sdk 10.0.13 Sdk/Sdk.targets (WasmEmitSymbolMap default for non-SDK projects)"
  - id: mono-toolchain-targets
    resource: /usr/local/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.mono.toolchain.current/10.0.101/WorkloadManifest.targets
    title: "[WMT] mono toolchain WorkloadManifest.targets 10.0.101"
  - id: wasm-features-props
    resource: /usr/local/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.mono.toolchain.current/10.0.101/WasmFeatures.props
    title: "[WFP] WasmFeatures.props 10.0.101"
  - id: wasm-props-json
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.netcore.app.runtime.mono.browser-wasm/runtimes/browser-wasm/native/src/wasm-props.json
    title: "[WPJ] runtime pack 10.0.13 native/src/wasm-props.json (PropertiesThatTriggerRelinking)"
  - id: emscripten-cache-props
    resource: /usr/local/share/dotnet/packs/Microsoft.NET.Runtime.Emscripten.3.1.34.Cache.osx-arm64/8.0.22/Sdk/Sdk.targets
    title: "[EMC] Emscripten Cache pack Sdk.targets (net8 copy inspected; sets WasmCachePath)"
  - id: wasm-features-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/features.md
    title: Configuring and hosting .NET WebAssembly applications (release/10.0)
  - id: jiterpreter-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/docs/design/mono/jiterpreter.md
    title: The Jiterpreter (BlazorWebAssemblyJiterpreter / BlazorWebAssemblyRuntimeOptions)
  - id: ms-migrate-10
    resource: https://learn.microsoft.com/aspnet/core/migration/90-to-100
    title: Migrate from ASP.NET Core in .NET 9 to .NET 10 (WasmApplicationEnvironmentName, BlazorCacheBootResources removal)
  - id: ms-wasm-browser-app
    resource: https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/wasm-browser-app?view=aspnetcore-10.0
    title: WebAssembly Browser App project (.NET 10; WasmEnableHotReload)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:10:47Z }
status: draft
---

# How to read this table

- **File codes**: `[SDK]` = in-box `Microsoft.NET.Sdk.WebAssembly`; `[PACK]` = `Microsoft.NET.Sdk.WebAssembly.Pack` (NuGet, always used by the WebAssembly SDK); `[BWA]`/`[WAC]` = `Microsoft.NET.Runtime.WebAssembly.Sdk` targets, **imported only when the `wasm-tools` workload is used** (`UsingBrowserRuntimeWorkload=true`); `[WMT]` = workload manifest targets (always evaluated for `browser-wasm`); `[WFP]` = feature-switch props; `[WPJ]` = runtime-pack `wasm-props.json`.[^sdk-wasm-props][^wasm-sdk-pack-targets][^browserwasmapp-targets][^wasmapp-common-targets][^mono-toolchain-targets][^wasm-features-props][^wasm-props-json]
- **WL** = setting it (to a non-default value where noted) makes the build require `wasm-tools`. Without the workload the build fails with `NETSDK1147` (see [/dotnet-wasm/function-pointers-and-native-callbacks.md](/dotnet-wasm/function-pointers-and-native-callbacks.md)). **RL** = it triggers a native relink of `dotnet.native.wasm`.
- `[BWA]`/`[WAC]` properties have **no effect** on a WebAssembly-SDK build without the workload, because those targets are not imported.

# Project / SDK level

| Property | .NET 10 default | File | Meaning | WL/RL |
|---|---|---|---|---|
| `RuntimeIdentifier` | `browser-wasm` | [SDK] | target RID | — |
| `UseMonoRuntime` / `SelfContained` | `true` / `true` | [SDK] | Mono runtime pack, self-contained | — |
| `PublishTrimmed` | `true` | [SDK] | ILLink on publish | — |
| `TrimMode` | `full` (non-Blazor), `partial` (Blazor) | [PACK] props | trimming aggressiveness | — |
| `SuppressTrimAnalysisWarnings` | `true` unless `TrimmerDefaultAction=link` | [PACK] | hides trim warnings | — |
| `AppendRuntimeIdentifierToOutputPath` | `false` | [PACK] props | output in `bin/<cfg>/net10.0/` | — |
| `StaticWebAssetBasePath` | `/` | [PACK] props | URL base of the app | — |
| `OverrideHtmlAssetPlaceholders` | template: `true` | [PACK] | rewrite `index.html` preload/importmap and fingerprint `dotnet.js` | — |
| `WasmEnableHotReload` | `true` in Debug for net10+, else `false` | [SDK] targets | implicit `Microsoft.DotNet.HotReload.WebAssembly.Browser` reference. Set `false` for bundler-friendly output | — |
| `AllowUnsafeBlocks` | template: `true` | csproj | required by the JSImport/JSExport generator | — |
| `UsingBrowserRuntimeWorkload` | computed | [WMT] | `true` when any WL property is set, or the workload is installed and available | — |
| `WasmNativeWorkload` / `WasmNativeWorkloadAvailable` | `net10.0` / `true` when the workload is installed | [WMT], AutoImport.props | workload presence | — |

[^sdk-wasm-props][^sdk-wasm-targets][^wasm-sdk-pack-props][^wasm-sdk-pack-targets][^mono-toolchain-targets][^ms-wasm-browser-app]

# Runtime features

| Property | .NET 10 default | File | Meaning | WL/RL |
|---|---|---|---|---|
| `WasmEnableSIMD` | `true` (= `WasmEnableExceptionHandling`) | [BWA], [WPJ] | wasm SIMD (`-msimd128`) and the ILLink intrinsic substitutions | WL+RL when `false` |
| `WasmEnableExceptionHandling` | `true` | [BWA], [WPJ] | wasm EH (`-fwasm-exceptions`) vs JS EH | WL+RL when `false` |
| `WasmEnableThreads` | `false` | [WMT], [PACK], [BWA] | multithread runtime pack (experimental); adds `dotnet.native.worker.mjs`; `dotnet run` adds `--apply-cop-headers` | builds **without** WL (observed) |
| `WasmEnableJsInteropByValue` | `true` iff threads | [BWA] | JS interop without managed pointers | RL |
| `InvariantGlobalization` | `false` | [WFP], [WPJ] | no ICU | RL (relinks if workload) |
| `InvariantTimezone` | `false` (`true` if `BlazorEnableTimeZoneSupport=false`) | [WFP], [PACK], [WPJ] | drop TZ database | WL+RL when `true` |
| `WasmIncludeFullIcuData` / `WasmIcuDataFileName` | `false` / empty | [BWA] | full `icudt.dat` / custom ICU file (must start with `icudt`) | — |
| `BlazorWebAssemblyLoadAllGlobalizationData` / `BlazorIcuDataFileName` | `false` / empty | [PACK] | the same knobs on the SDK path | — |
| `EventSourceSupport`, `MetricsSupport` | `false`, `false` | [WFP] | feature switches | — |
| `UseSystemResourceKeys` | `true` | [WFP] | exception messages are resource keys | — |
| `UseSizeOptimizedLinq` | `true` | [WFP] | smaller LINQ | — |
| `Http3Support`, `HttpActivityPropagationSupport`, `EnableUnsafeUTF7Encoding` | `false` | [WFP] | feature switches | — |
| `DebuggerSupport` | `false` outside Debug | [WFP] | — | — |
| `WasmEnableStreamingResponse` | `true` | [WFP], [BWA] | streamed `HttpClient` responses (.NET 10 change) | — |
| `WasmEnableWebcil` | `true` | [PACK], [BWA] | assemblies as `.wasm` Webcil | — |
| `BlazorWebAssemblyJiterpreter` | unset (on) | [PACK] | enable/disable jiterpreter (works for the WebAssembly SDK too) | — |
| `BlazorWebAssemblyRuntimeOptions` | empty | [PACK] | runtime options string (e.g. `--no-jiterpreter-traces-enabled`) | — |
| `WasmDebugLevel` | Debug build → boot config `-1`; publish → `0` | [PACK], [WAC] | >0 debug+log, 0 no debug (enables interpreter optimisations), <0 debug without log | — |

[^browserwasmapp-targets][^wasm-props-json][^wasm-features-props][^wasm-sdk-pack-targets][^mono-toolchain-targets][^jiterpreter-md][^wasm-features-md]

# Output, boot config, hosting

| Property | .NET 10 default | File | Meaning |
|---|---|---|---|
| `WasmInlineBootConfig` | `true` for net10 when `WasmBootConfigFileName` is empty | [PACK] | boot config embedded in `dotnet.js` (replaces `blazor.boot.json`) |
| `WasmBootConfigFileName` | empty → `dotnet.js` (inline) / `dotnet.boot.js` | [PACK], [BWA] | boot config file name |
| `WasmFingerprintAssets` | `true` (net9+) | [PACK] | hashed file names for `.wasm/.dll/.dat/.pdb` |
| `WasmFingerprintDotnetJs` | `$(OverrideHtmlAssetPlaceholders)` else `false` | [PACK] | hard fingerprint `dotnet.js` |
| `WasmFingerprintBootConfig` | follows `WasmFingerprintDotnetJs` when inline | [PACK] | — |
| `WasmPreloadAssets` | `true` | [PACK] | `<link rel=preload>` generation |
| `WasmBundlerFriendlyBootConfig` | `false` | [PACK] | JS output for webpack/rollup (not directly runnable) |
| `WasmApplicationEnvironmentName` | `Development` (build), `Production` (publish) | [PACK] | app environment (replaces the `Blazor-Environment` header) |
| `WasmEmitSourceMap` | build: `true` | [PACK], [BWA] | ship `dotnet.js.map`/`dotnet.runtime.js.map` |
| `CompressionIncludePatterns` / `DisableBuildCompression` | includes `_framework\**` | [PACK] | `.gz` (build) / `.br`+`.gz` (publish) |
| `BlazorCacheBootResources` | removed (warning if set) | [PACK] | — |
| `WasmHostArguments` | empty | [PACK], [WAC] | extra args to `WasmAppHost` for `dotnet run` |
| `WasmRuntimeAssetsLocation` | `_framework` | [BWA], [WAC] | AppBundle path only (`./` flattens) |
| `WasmAppDir` | `$(OutputPath)AppBundle` | [WAC] | AppBundle path only |
| `WasmGenerateAppBundle` | `true` for exe on non-SDK path; `false` with Blazor/WebAssembly SDK + workload | [WAC], [WMT] | WL when `true` on the SDK path |
| `WasmMainJSPath` / `WasmMainHTMLPath` / `WasmMainAssemblyFileName` | empty / empty / `$(TargetFileName)` | [BWA], [WAC] | AppBundle path only |
| `WasmGenerateRunV8Script` | empty | [BWA] | writes `run-v8.sh` |
| `WasmSingleFileBundle` | `false` | [WAC], [WPJ] | bundle assemblies into the wasm (RL) |

[^wasm-sdk-pack-targets][^browserwasmapp-targets][^wasmapp-common-targets][^ms-migrate-10]

# Native build / Emscripten (workload only)

| Property | .NET 10 default | File | Meaning |
|---|---|---|---|
| `WasmBuildNative` | auto: `true` with `NativeFileReference`, a changed relink property, AOT, or Release publish of a trimmed app | [WAC] | relink `dotnet.native.wasm` (WL) |
| `WasmBuildOnlyAfterPublish` | `true` when publishing | [WAC] | relink only on publish |
| `WasmNativeStrip` | `true` (but `false` + `WasmNativeDebugSymbols=true` for Debug relinks) | [WAC], [WPJ] | strip symbols; enables `wasm-opt` post-link (`WasmRunWasmOpt`) (WL+RL when `false`) |
| `WasmNativeDebugSymbols` | `true` (compile `-g`) | [WAC], [BWA] | DWARF in the native build (WL when `true`, per [WMT]) |
| `WasmEmitSymbolMap` | `false` (SDK path); `true` for non-SDK non-AOT | [BWA], [BWA Sdk.targets] | write `dotnet.native.js.symbols` |
| `WasmLinkIcalls` | `$(WasmBuildNative)` | [BWA] | link only used icalls (`-DLINK_ICALLS=1`) |
| `EmccFlags` | empty | [BWA] | flags for compile **and** link |
| `EmccExtraCFlags` | empty | [BWA] | extra compile flags |
| `EmccExtraLDFlags` | empty | [BWA] | extra link flags (JS libraries, `-l…`, `-s…`, `--use-port`) |
| `EmccExtraBitcodeCompilationFlags` | empty | [BWA] | extra flags for AOT `.bc` → `.o` |
| `EmccCompileOptimizationFlag` | `-O0` (Debug relink), `-O1`, else `-Oz` | [BWA] | native compile optimisation |
| `EmccLinkOptimizationFlag` | `-O2` in Release, else = compile flag | [BWA] | link optimisation |
| `WasmBitcodeCompileOptimizationFlag` | `-O2` in Release | [BWA] | AOT bitcode compile |
| `EmccInitialHeapSize` (aliases `WasmInitialHeapSize`, `EmccTotalMemory`) | computed, ≥ 33554432 | [BWA], [WAC], [WPJ] | `-s INITIAL_MEMORY` (multiple of 16384) (WL+RL) |
| `EmccMaximumHeapSize` | 2147483648 (runtime pack) | [BWA], [WPJ] | `-s MAXIMUM_MEMORY` (WL+RL) |
| `EmccStackSize` | `5MB` | [BWA] | `-s STACK_SIZE` |
| `EmccEnvironment` | runtime pack: `web,webview,worker,node,shell` | [BWA] | `-s ENVIRONMENT` |
| `EmccEnableAssertions` | `false` | [BWA] | `-s ASSERTIONS=1` |
| `EmccVerbose` | not `false` → `-v` | [BWA] | verbose emcc |
| `WasmAllowUndefinedSymbols` | `false` | [BWA] | allow unresolved native symbols |
| `WasmCachePath` | the workload's frozen cache dir | [EMC], [BWA] | `EM_CACHE`. Any other path gives `EM_FROZEN_CACHE=0` (needed for ports) |
| `WasmUseEMSDK_PATH` / `EMSDK_PATH` | unset | [BWA] | use an external emsdk; enforces an exact version match with 3.1.56 |
| `DisableParallelAot` / `DisableParallelEmccCompile` | `false` | [BWA] | serialise compilation |

[^wasmapp-common-targets][^browserwasmapp-targets][^wasm-props-json][^emscripten-cache-props][^wasm-sdk-sdkprops]

# AOT

| Property | .NET 10 default | File | Meaning |
|---|---|---|---|
| `RunAOTCompilation` | `false` | [WAC], [WMT] | Mono LLVM AOT on publish (WL+RL; Release + trimmed only) |
| `RunAOTCompilationAfterBuild` | `false` | [WAC] | also AOT on build |
| `AOTMode` | `LLVMOnlyInterp` | [WAC] | AOT plus interpreter fallback |
| `WasmStripILAfterAOT` | `true` | [BWA], [WAC] | remove IL of AOT'd methods |
| `WasmDedup` | `true` | [WAC] | dedup generic instances (`aot-instances.dll`) |
| `AOTProfilePath` / `WasmAotProfilePath` | empty | [WAC] | profile-guided (partial) AOT |
| `WasmAOTCompilerVerbose` | empty | [WAC] | `-v` to the AOT compiler |
| `WasmStripAOTAssemblies` | forced `false` | [WAC] | legacy `mono-cil-strip` |

[^wasmapp-common-targets][^browserwasmapp-targets]

# Diagnostics

| Property | Default | File | Meaning |
|---|---|---|---|
| `EnableDiagnostics` | `false` | [BWA], [WMT], [WPJ] | EventPipe diagnostics server, `dotnet.diagnostics.js` (WL+RL) |
| `WasmPerformanceInstrumentation` | empty | [WAC] | sampling instrumentation (`all`, `none`, `N:Ns`); requires `EnableDiagnostics=true` |
| `DiagnosticPorts` | empty | [BWA] | sets `DOTNET_DiagnosticPorts` |
| `WasmProfilers` | empty | [BWA], [WAC], [WPJ] | `browser`, `log`, `aot` Mono profilers (WL+RL). `browser` is incompatible with AOT and `EnableDiagnostics` |

[^browserwasmapp-targets][^wasmapp-common-targets][^wasm-features-md]

# Items

| Item | Metadata | File | Meaning |
|---|---|---|---|
| `NativeFileReference` | `ScanForPInvokes` (default scan) | [WAC], [BWA] | C/C++/`.o`/`.a`/`.bc` linked into the runtime; the file name becomes a P/Invoke module (WL+RL) |
| `NativeLibrary` | — | [WAC] | existing files become `NativeFileReference` |
| `EmccExportedRuntimeMethod` | — | [BWA] | `-s EXPORTED_RUNTIME_METHODS` additions (defaults read from `wasm-props.json`) |
| `EmccExportedFunction` | — | [BWA] | `-s EXPORTED_FUNCTIONS` additions (`_name`) |
| `EmccExportedLibraryFunction` | — | [BWA] | `-s DEFAULT_LIBRARY_FUNCS_TO_INCLUDE` |
| `LinkerArg` | — | [BWA] | raw args appended to the link |
| `Content` (`*.js`) | `Kind` = `js-library`/`pre-js`/`post-js`/`extern-pre-js`/`extern-post-js` | [BWA] | passed to emcc as `--<Kind>` (UNVERIFIED usage) |
| `WasmExtraFilesToDeploy` | `TargetPath` | [WAC] | extra files into AppBundle |
| `WasmFilesToIncludeInFileSystem` | — | [WAC] | preload into the Emscripten VFS |
| `WasmEnvironmentVariable` | `Value` | [WAC], [PACK] | env vars in the boot config |
| `WasmExtraConfig` | `Value` (JSON) | [WAC] | extra boot-config entries |
| `WasmBootConfigExtension` | — | [PACK] | `config.extensions` |
| `BlazorWebAssemblyLazyLoad` | — | [PACK] | lazily loaded assemblies |
| `_WasmPInvokeModules` (internal) | — | [WAC] | static P/Invoke module names. Third parties inject it before `_GenerateManagedToNative` (fragile) |

[^wasmapp-common-targets][^browserwasmapp-targets][^wasm-sdk-pack-targets]

# Relink-triggering set (authoritative list from the runtime pack)

From `wasm-props.json` `PropertiesThatTriggerRelinking` (property = runtime-pack default): `InvariantTimezone=false`, `InvariantGlobalization=false`, `WasmNativeStrip=true`, `WasmSingleFileBundle=false`, `WasmEnableSIMD=true`, `WasmEnableExceptionHandling=true`, `EnableDiagnostics=false`, `WasmProfilers=""`, `EmccMaximumHeapSize=2147483648`, `EmccInitialHeapSize=33554432`, `RunAOTCompilation=""`. Setting any of them to a different value forces `WasmBuildNative=true`. Explicitly setting `WasmBuildNative=false` at the same time is an error: `WasmBuildNative is required because <Prop>=<Value>, but WasmBuildNative is already set to 'false'.`[^wasm-props-json][^wasmapp-common-targets]

## Open questions

- UNVERIFIED: whether `Content` items with `Kind` metadata are an intended public way to add Emscripten JS libraries.
- UNVERIFIED: `WasmApplicationEnvironmentName` publish default as implemented in the pack (the docs say `Production`).

[^sdk-wasm-props]: [SDK] Microsoft.NET.Sdk.WebAssembly Sdk.props
[^sdk-wasm-targets]: [SDK] Microsoft.NET.Sdk.WebAssembly Sdk.targets
[^wasm-sdk-pack-props]: [PACK] Microsoft.NET.Sdk.WebAssembly.Browser.props 10.0.13
[^wasm-sdk-pack-targets]: [PACK] Microsoft.NET.Sdk.WebAssembly.Browser.targets 10.0.13
[^browserwasmapp-targets]: [BWA] BrowserWasmApp.targets 10.0.13
[^wasmapp-common-targets]: [WAC] WasmApp.Common.targets 10.0.13
[^wasm-sdk-sdkprops]: [BWA] Microsoft.NET.Runtime.WebAssembly.Sdk Sdk.targets 10.0.13
[^mono-toolchain-targets]: [WMT] WorkloadManifest.targets 10.0.101
[^wasm-features-props]: [WFP] WasmFeatures.props 10.0.101
[^wasm-props-json]: [WPJ] runtime pack wasm-props.json 10.0.13
[^emscripten-cache-props]: [EMC] Emscripten Cache pack Sdk.targets
[^wasm-features-md]: dotnet/runtime src/mono/wasm/features.md (release/10.0)
[^jiterpreter-md]: dotnet/runtime docs/design/mono/jiterpreter.md
[^ms-migrate-10]: Microsoft Learn — Migrate from .NET 9 to .NET 10
[^ms-wasm-browser-app]: Microsoft Learn — WebAssembly Browser App project (.NET 10)
