---
type: Research Note
title: Native linking in .NET 10 wasm builds — wasm-tools workload, Emscripten 3.1.56, NativeFileReference, emcc flags and Emscripten ports
description: What the .NET 10 wasm-tools workload installs (Emscripten 3.1.56 built on Microsoft's LLVM fork, a frozen prebuilt cache), how the relink pipeline invokes emcc, which MSBuild properties/items inject C sources, archives, JS libraries and emcc flags, and whether Emscripten ports (contrib.glfw3, USE_WEBGPU, emdawnwebgpu, OpenAL) can be used from a .NET build given the pinned toolchain.
tags: [dotnet-wasm, emscripten, wasm-tools, nativefilereference, emcc, ports, emdawnwebgpu, webgpu, glfw, openal, relink]
sources:
  - id: emscripten-manifest
    resource: /usr/local/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.emscripten.current/10.0.101/WorkloadManifest.json
    title: Emscripten workload manifest 10.0.101 (Microsoft.NET.Runtime.Emscripten.3.1.56.* aliases)
  - id: emscripten-manifest-targets
    resource: /usr/local/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.emscripten.current/10.0.101/WorkloadManifest.targets
    title: Emscripten workload manifest targets 10.0.101
  - id: mono-toolchain-manifest
    resource: /usr/local/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.mono.toolchain.current/10.0.101/WorkloadManifest.json
    title: Mono toolchain workload manifest 10.0.101 (wasm-tools packs)
  - id: wasm-props-json
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.netcore.app.runtime.mono.browser-wasm/runtimes/browser-wasm/native/src/wasm-props.json
    title: Runtime pack wasm-props.json (RuntimeEmccVersion/Raw/Hash)
  - id: emscripten-version-txt
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/browser/emscripten-version.txt
    title: dotnet/runtime release/10.0 emscripten-version.txt ("3.1.56")
  - id: emcc-link-rsp
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.netcore.app.runtime.mono.browser-wasm/runtimes/browser-wasm/native/src/emcc-link.rsp
    title: Runtime pack emcc-link.rsp (default link flags)
  - id: browserwasmapp-targets
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.sdk/Sdk/BrowserWasmApp.targets
    title: BrowserWasmApp.targets 10.0.13 (emcc invocation, flags, version check, frozen cache)
  - id: wasmapp-common-targets
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.sdk/Sdk/WasmApp.Common.targets
    title: WasmApp.Common.targets 10.0.13 (documented public properties/items)
  - id: emsdk-repo-defaults
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.sdk/Sdk/EmSdkRepo.Defaults.props
    title: EmSdkRepo.Defaults.props 10.0.13 (EMSDK_PATH-based toolchain)
  - id: net8-cache-pack
    resource: /usr/local/share/dotnet/packs/Microsoft.NET.Runtime.Emscripten.3.1.34.Cache.osx-arm64/8.0.22
    title: Installed net8 Emscripten Cache pack (layout reference; Sdk.targets sets WasmCachePath to the frozen cache)
  - id: runtime-lib-probe
    resource: /Users/asherwin/.nuget/packages/microsoft.netcore.app.runtime.mono.browser-wasm/10.0.1/runtimes/browser-wasm/native/libSystem.Native.a
    title: Runtime pack static library (26 wasm object members; "clang version 19.1.0 (…dotnet-llvm-project…)")
  - id: probe-build
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/proj/WasmProbe
    title: Throwaway project builds without wasm-tools (NETSDK1147)
  - id: ms-native-deps
    resource: https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-native-dependencies?view=aspnetcore-10.0
    title: Blazor WebAssembly native dependencies (.NET 10)
  - id: ms-build-tools-aot
    resource: https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot?view=aspnetcore-10.0
    title: Blazor WebAssembly build tools and AOT (.NET 10)
  - id: wasm-features-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/features.md
    title: Configuring and hosting .NET WebAssembly applications (release/10.0)
  - id: emscripten-changelog
    resource: https://github.com/emscripten-core/emscripten/blob/main/ChangeLog.md
    title: Emscripten ChangeLog (3.1.54 --use-port/contrib.glfw3; 4.0.10 USE_WEBGPU deprecated; 4.0.18 removed; 6.0.12 latest on 2026-10-08)
  - id: dawn-emdawn-readme
    resource: https://dawn.googlesource.com/dawn/+/refs/heads/main/src/emdawnwebgpu/pkg/README.md
    title: Dawn emdawnwebgpu package README (remote port requires Emscripten 4.0.10+)
  - id: uno-wgpu-wasm-targets
    resource: https://github.com/unoplatform/uno/blob/master/src/Uno.UI.Composition.WebGpu.Init/wgpu-wasm.targets
    title: Uno Platform wgpu-wasm.targets (emdawnwebgpu local port patched for Emscripten 3.1.56, writable cache)
  - id: uno-webgpu-targets
    resource: https://github.com/unoplatform/uno/blob/master/src/Uno.UI.Composition.WebGpu/package/Uno.WinUI.Composition.WebGpu.targets
    title: Uno Platform WebGPU package targets (--use-port + WasmCachePath + _WasmPInvokeModules "webgpu")
  - id: skiasharp-targets
    resource: https://github.com/mono/SkiaSharp/blob/main/binding/SkiaSharp.NativeAssets.WebAssembly/buildTransitive/SkiaSharp.targets
    title: SkiaSharp WebAssembly buildTransitive targets (EmccExtraLDFlags -s USE_WEBGL2=1 -s USE_WEBGPU=1)
  - id: skiasharp-blazor-props
    resource: https://github.com/mono/SkiaSharp/blob/main/source/SkiaSharp.Views/SkiaSharp.Views.Blazor/nuget/buildTransitive/SkiaSharp.Views.Blazor.props
    title: SkiaSharp.Views.Blazor props (EmccExtraLDFlags --js-library SkiaSharpInterop.js)
  - id: runtime-advanced-sample
    resource: https://github.com/dotnet/runtime/blob/main/src/mono/sample/wasm/browser-advanced/Wasm.Advanced.Sample.csproj
    title: dotnet/runtime browser-advanced sample (EmccExtraLDFlags -lGL -lSDL -lidbfs.js)
  - id: pinvoke-table-gen
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/tasks/WasmAppBuilder/PInvokeTableGenerator.cs
    title: PInvokeTableGenerator.cs (module list handling)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:04:03Z }
status: draft
---

# The authoritative Emscripten version for .NET 10: 3.1.56

- The workload manifest `microsoft.net.workload.emscripten.current` 10.0.101 maps `Microsoft.NET.Runtime.Emscripten.{Node,Python,Cache,Sdk}.net10` (pack version 10.0.1) to **`Microsoft.NET.Runtime.Emscripten.3.1.56.<Kind>.<rid>`** for win/linux/linux-musl/osx × x64/arm64. Python is bundled only on Windows and macOS.[^emscripten-manifest]
- The runtime pack records the toolchain it was built with: `RuntimeEmccVersion` = `3.1.56`, `RuntimeEmccVersionRaw` = `emcc (Emscripten gcc/clang-like replacement + linker emulating GNU ld) 3.1.56 (57b21b8fdcbe3ebb523178b79465254668eab408)`.[^wasm-props-json] `release/10.0` `src/mono/browser/emscripten-version.txt` says `3.1.56`.[^emscripten-version-txt]
- The runtime's static libraries are **wasm object files** (no LLVM bitcode) built by `clang version 19.1.0 (…/dotnet-llvm-project …)`, i.e. Microsoft's LLVM fork.[^runtime-lib-probe]
- NuGet publishes the 3.1.56 packs up to 10.0.13 and `11.0.0-preview.5`. (The net8 workload uses 3.1.34, which is what is installed on this Mac.) For comparison, upstream Emscripten is at **6.0.12 (2026-10-08)**: .NET 10's toolchain is about 2.5 years older than upstream.[^emscripten-changelog]

# What `wasm-tools` installs (net10)

`wasm-tools` = packs `Microsoft.NET.Runtime.WebAssembly.Sdk.net10`, `Microsoft.NET.Sdk.WebAssembly.Pack.net10`, `Microsoft.NETCore.App.Runtime.Mono.net10.browser-wasm`, `Microsoft.NETCore.App.Runtime.AOT.Cross.net10.browser-wasm` (the Mono AOT cross compiler). It extends `microsoft-net-runtime-mono-tooling` (`MonoAOTCompiler.Task`, `MonoTargets.Sdk`) and `microsoft-net-sdk-emscripten` (the four Emscripten packs).[^mono-toolchain-manifest] `wasm-experimental` adds the templates and `Microsoft.NETCore.App.Runtime.Mono.multithread.net10.browser-wasm`.[^mono-toolchain-manifest] The runtime team: "The `wasm-tools` workload contains all of the tools and libraries necessary to perform native rebuild or AOT compilation … Although it's optional for Blazor, **we strongly recommend using it!**"[^wasm-features-md] Workloads are per SDK band. With the .NET 10 SDK, targeting net10 needs `wasm-tools`; `wasm-tools-net8`/`-net9` cover older TFMs. On Linux, Python must be on `PATH`.[^ms-build-tools-aot] **This Mac has only `wasm-tools-net8`**, so every net10 relink or AOT build fails with `NETSDK1147 … wasm-tools`.[^probe-build]

The Emscripten SDK is imported only when `UsingBrowserRuntimeWorkload` is true, which happens with `RunAOTCompilation`, a non-WebAssembly-SDK project, or `WasmNativeWorkload`.[^emscripten-manifest-targets]

# The relink pipeline (what runs when `WasmBuildNative=true`)

From `WasmApp.Common.targets` and `BrowserWasmApp.targets`:[^wasmapp-common-targets][^browserwasmapp-targets]

1. `_SetupEmscripten` checks the toolchain paths, then builds `EmscriptenEnvVars`: `PATH`, `DOTNET_EMSCRIPTEN_LLVM_ROOT`, `…BINARYEN_ROOT`, `…NODE_JS`, `PYTHONUTF8=1`, `EM_CACHE`, `EM_FROZEN_CACHE`, `WASM_ENABLE_SIMD/EH`, `RUN_AOT_COMPILATION`, …
2. `_WasmAotCompileApp`, only when AOT is on: Mono AOT cross-compiler → `.bc` → `emcc` → `.o`.
3. `_GenerateManagedToNative`: `pinvoke-table.h`, `wasm_m2n_invoke.g.h`, `icall-table.h` (see [/dotnet-wasm/function-pointers-and-native-callbacks.md](/dotnet-wasm/function-pointers-and-native-callbacks.md)).
4. `_WasmCompileNativeSourceFiles`: `emcc` compiles the runtime-pack sources `pinvoke.c`, `driver.c`, `runtime.c`, `corebindings.c` with `$(EmccCompileOptimizationFlag)` plus `-DGEN_PINVOKE=1 -emit-llvm`, `-msimd128`/`-fwasm-exceptions`, and optional defines (`-DINVARIANT_GLOBALIZATION=1`, `-DLINK_ICALLS=1`, …) plus `$(EmccExtraCFlags)`.
5. `_BrowserWasmLinkDotNet`: `emcc "@emcc-default.rsp" … "@emcc-link.rsp" "@obj/…/emcc-link.rsp"` → `dotnet.native.js` + `dotnet.native.wasm`. The log prints "Linking with emcc with -O2. This may take a while …". Undefined symbols fail the link (`-s ERROR_ON_UNDEFINED_SYMBOLS=1`); the hint "Use '-p:WasmAllowUndefinedSymbols=true' to allow undefined symbols" is printed.
6. `_RunWasmOptPostLink` (when `WasmNativeStrip`): `wasm-opt --enable-simd --enable-exception-handling --enable-bulk-memory … --strip-dwarf`.

The link step receives, in order: `$(WasmLinkOptimizationFlag)`, `$(EmccFlags)`, `-s EXPORT_ES6=1 -lexports.js`, `-s INITIAL_MEMORY`, `-s STACK_SIZE`, `-s WASM_BIGINT=1`, optional `-s ENVIRONMENT=…`/`-s ASSERTIONS=1`, `--js-library/--pre-js/--post-js` runtime JS, **`@(NativeFileReference)`**, all runtime `*.a`, `-s EXPORTED_RUNTIME_METHODS=[…]`, `-s EXPORTED_FUNCTIONS=…`, then **`$(EmccExtraLDFlags)`** and **`@(LinkerArg)`** last.[^browserwasmapp-targets] The runtime pack's own defaults (`emcc-link.rsp`) are `INITIAL_MEMORY=33554432`, `STACK_SIZE=5MB`, `ALLOW_MEMORY_GROWTH=1`, `ALLOW_TABLE_GROWTH=1`, `NO_EXIT_RUNTIME=0`, `FORCE_FILESYSTEM=1`, `MODULARIZE=1`, `EXPORT_NAME='createDotnetRuntime'`, `ENVIRONMENT="web,webview,worker,node,shell"` and `--profiling-funcs`.[^emcc-link-rsp]

Optimisation defaults:[^browserwasmapp-targets]

| Situation | Compile (`EmccCompileOptimizationFlag`) | Link (`EmccLinkOptimizationFlag`) |
|---|---|---|
| `dotnet build -c Debug` with relink (`_WasmDevel=true`) | `-O0` | `-O0` (follows compile flag) |
| Debug, `_WasmDevel` forced off | `-O1` | `-O1` |
| Publish in a non-Release configuration | `-Oz` | `-Oz` (follows compile flag) |
| `Release` | `-Oz` | **`-O2`** |
| AOT bitcode (`WasmBitcodeCompileOptimizationFlag`) | `-O2` in Release, else the compile flag | — |

# Adding native code

## `NativeFileReference`

- `<NativeFileReference Include="foo.c" />` (also `.cpp`, `.o`, `.a`, `.bc`, `.wasm` per the docs). Each item is passed to the link `emcc` command, so Emscripten compiles sources there.[^ms-native-deps][^browserwasmapp-targets] Prebuilt objects "typically must be built using the same version of Emscripten used to build the .NET WebAssembly runtime", i.e. 3.1.56.[^ms-native-deps]
- **The file name without extension becomes the P/Invoke module name**: `[DllImport("foo")]` binds to `foo.c`/`foo.a`. Opt out of scanning with `ScanForPInvokes="false"` metadata.[^wasmapp-common-targets][^pinvoke-table-gen]
- `@(NativeLibrary)` items that exist are converted to `NativeFileReference`.[^wasmapp-common-targets]
- Any `NativeFileReference` turns `WasmBuildNative` on automatically (build and publish), which requires the workload. Without it there is only a warning, and the code is not linked.[^wasmapp-common-targets]
- NuGet packages carry `browser-wasm` native assets, which consumers must reference manually or through a `.props` file. They are not picked up automatically.[^ms-native-deps]

## Flags, exports and JS libraries

| Knob | Kind | Effect |
|---|---|---|
| `EmccFlags` | property | added to **both** compile and link |
| `EmccExtraCFlags` | property | extra compile flags for native sources |
| `EmccExtraBitcodeCompilationFlags` | property | extra flags when compiling AOT bitcode |
| `EmccExtraLDFlags` | property | appended to the link command (e.g. `--js-library x.js`, `-lGL`, `-sUSE_WEBGPU=1`, `--use-port=…`) |
| `LinkerArg` | item | appended after `EmccExtraLDFlags` |
| `EmccExportedRuntimeMethod` | **item** (singular) | adds to `-s EXPORTED_RUNTIME_METHODS` (e.g. `<EmccExportedRuntimeMethod Include="HEAPU8" />`) |
| `EmccExportedFunction` | **item** | adds to `-s EXPORTED_FUNCTIONS` (`_myfn`) |
| `EmccExportedLibraryFunction` | **item** | adds to `-s DEFAULT_LIBRARY_FUNCS_TO_INCLUDE` |
| `Content` items with extension `.js` and `Kind` metadata | item | passed as `--<Kind> file` (`js-library`, `pre-js`, `post-js`, `extern-pre-js`, `extern-post-js`). UNVERIFIED in practice |
| `WasmAllowUndefinedSymbols` | property | `-Wl,--allow-undefined -s ERROR_ON_UNDEFINED_SYMBOLS=0` |
| `EmccEnvironment`, `EmccEnableAssertions`, `EmccVerbose` | property | `-s ENVIRONMENT`, `-s ASSERTIONS=1`, `-v` |

[^browserwasmapp-targets][^wasmapp-common-targets]

Note: `EmccExportedRuntimeMethods`/`EmccExportedFunctions` are **not** property names; the targets use the singular item names above.[^browserwasmapp-targets]

Real-world use of `EmccExtraLDFlags` from packages: SkiaSharp adds `-s USE_WEBGL2=1 -s USE_WEBGPU=1` (or `USE_WEBGL2` only when using emdawnwebgpu)[^skiasharp-targets] and `--js-library="…/SkiaSharpInterop.js"`.[^skiasharp-blazor-props] dotnet/runtime's own advanced sample uses `-s USE_CLOSURE_COMPILER=1 -s LEGACY_GL_EMULATION=1 -lGL -lSDL -lidbfs.js`.[^runtime-advanced-sample]

# The frozen Emscripten cache and ports

- The workload ships a **prebuilt Emscripten cache** (sysroot libs: libc, libc++, dlmalloc, `libGL-*` variants, `libal.a` (OpenAL), `libembind`, …). The installed net8 3.1.34 cache pack shows that layout; the net10 3.1.56 cache is assumed analogous (UNVERIFIED). Its `Sdk.targets` sets `WasmCachePath` to that directory, and `BrowserWasmApp.targets` then exports `EM_FROZEN_CACHE=1`.[^net8-cache-pack][^browserwasmapp-targets]
- **Anything that must be *built* into the cache fails with the default frozen cache.** That covers most ports (`--use-port=contrib.glfw3`, SDL2, emdawnwebgpu) and uncached system-lib variants. UNVERIFIED: the exact emcc error text. Pointing `WasmCachePath` to a writable directory gives `EM_FROZEN_CACHE=0`.[^browserwasmapp-targets] Uno Platform does exactly that. It copies the SDK cache into `obj/…/wgpu-wasm-emcache` once ("Seed the writable cache from the SDK's once, so only the port and its extra stdlibs get built"). It also warns that on Windows the cache must sit on the toolchain's drive, and that emscripten "rejects a relative EM_CACHE outright".[^uno-wgpu-wasm-targets][^uno-webgpu-targets]

## Port and library availability with Emscripten 3.1.56

| Feature | Since | In 3.1.56? | From a .NET build | Notes |
|---|---|---|---|---|
| `--use-port=<name>` | 3.1.54 | yes | `EmccExtraLDFlags` (and `EmccExtraCFlags` for headers) | needs a writable cache to build the port |
| `contrib.glfw3` port | 3.1.54 | yes | as above | GLFW-on-HTML5 in C++; BRUTAL's GLFW binding would P/Invoke it via a module name (see [/brutal/](/brutal/)) |
| `-sUSE_GLFW=3` (JS `library_glfw.js`) | old | yes | `EmccExtraLDFlags` | JS library, no cache build |
| `-sUSE_WEBGPU=1` (Emscripten's own `library_webgpu.js`, old `webgpu.h`) | old | **yes** (deprecated in 4.0.10, removed in 4.0.18) | `EmccExtraLDFlags` | used by SkiaSharp. Older, non-standardised `webgpu.h` API |
| `--use-port=emdawnwebgpu` (built-in) | ≥ 4.0.10 | **no** | — | "Requires Emscripten 4.0.10+" for the remote port |
| emdawnwebgpu **local port** (`emdawnwebgpu_pkg/emdawnwebgpu.port.py`) | Dawn releases | works **with patches** | `EmccExtraLDFlags=--use-port=…/emdawnwebgpu.port.py -sDEFAULT_TO_CXX=1` + writable `WasmCachePath` | Dawn: "Emdawnwebgpu may not work with older Emscripten releases", and before 4.0.7 needs `--closure-args=--externs=…/webgpu-externs.js`. Uno ships Dawn `v20260624.223603` patched: "emsdk 3.1.56 exposes stackSave/stackRestore only as bare names, not the $-aliases the modern port's __deps declare" |
| `-lopenal` (JS `library_openal.js`, `libal.a` in cache) | old | yes | `EmccExtraLDFlags` | Web Audio-backed OpenAL 1.1 subset |
| `-lGL`, `-sMIN_WEBGL_VERSION=2` | old | yes | `EmccExtraLDFlags` | |

[^emscripten-changelog][^dawn-emdawn-readme][^uno-wgpu-wasm-targets][^uno-webgpu-targets][^skiasharp-targets]

Two notes from Uno's emdawnwebgpu integration are directly relevant to a WebGPU backend driven from C#:
1. "the in-WASM wgpuInstanceProcessEvents pump hangs when driven from a managed call stack on the browser". Uno therefore creates the `GPUDevice` in JS (`navigator.gpu`) and imports it into emdawnwebgpu's handle table.
2. "the in-WASM DevicePoll busy-spin can't yield for the map to complete". Buffer mapping must go through JS promises.[^uno-wgpu-wasm-targets]

Both point back to: **do async WebGPU work in JS and await it from C# as `Task`s**. See [/dotnet-wasm/main-loop-threading-and-pitfalls.md](/dotnet-wasm/main-loop-threading-and-pitfalls.md) and [/webgpu/](/webgpu/).

Uno also registers `webgpu` as a static P/Invoke module with `<_WasmPInvokeModules Include="webgpu" />` in a target that runs `BeforeTargets="_GenerateManagedToNative"`. That lets `[DllImport("webgpu")]` bind to the port's `wgpu*` symbols without a `NativeFileReference` named `webgpu`. It also adds a C stub file (`ScanForPInvokes="false"`) for functions Dawn lacks.[^uno-webgpu-targets] `_WasmPInvokeModules` is an internal item, so the technique may break between versions.

# Using a newer Emscripten than 3.1.56

- Supported route: **none**. The toolchain comes from the workload packs.
- In-repo route: `WasmUseEMSDK_PATH=true` + `EMSDK_PATH=<emsdk>` imports `EmSdkRepo.Defaults.props`, which builds the tool paths from `EMSDK_PATH`.[^emsdk-repo-defaults][^browserwasmapp-targets] That also sets `_WasmStrictVersionMatch=true`, so `_CheckEmccIsExpectedVersion` runs `emcc --version` and emits **both a warning and an error** on any difference:[^wasmapp-common-targets][^browserwasmapp-targets]

  ```text
  Emscripten version mismatch. The runtime pack in <dir> expects 'emcc (…) 3.1.56 (57b21b8…)', but emcc being used has version '<actual>'. This might cause build failures.
  ```

- Hack route (UNVERIFIED): override `EmscriptenSdkToolsPath`/`EmscriptenUpstreamBinPath`/`EmscriptenNodeToolsPath` in a target that runs before `_SetupEmscripten`, without setting `WasmUseEMSDK_PATH`, which skips the strict check. Risks: the prebuilt runtime objects (`libmonosgen-2.0.a`, …) were compiled against 3.1.56's sysroot headers and musl ABI; `dotnet.native.js` is generated with Emscripten JS-library internals that change between versions (Uno's `stackSave` patch shows the drift); and `src/es6/*.js` runtime libraries assume 3.1.56 APIs.
- Practical conclusion: **use ports that work on 3.1.56** (patched local emdawnwebgpu, `USE_WEBGPU`, JS libraries), or keep WebGPU entirely in JS behind `[JSImport]` (the current plan), which needs no native linking at all.

# Relevance to the PoC

- The current plan (C# Vulkan-on-WebGPU translation calling WebGPU via `[JSImport]`) needs **no Emscripten port**. `wasm-tools` is still needed if the design keeps `delegate* unmanaged` + `[UnmanagedCallersOnly]` (trampolines) or uses AOT.
- An alternative backend worth benchmarking later is P/Invoke into emdawnwebgpu's `webgpu.h` (`[DllImport("webgpu")]`, as Uno does). Calls then go wasm → Emscripten JS library directly, without the managed `[JSImport]` marshalling layer. That path requires `wasm-tools`, a writable cache and the 3.1.56 patches above.

## Open questions

- UNVERIFIED: the exact error emcc prints when a port is requested with the frozen cache.
- UNVERIFIED: whether the net10 Emscripten 3.1.56 Cache pack contains the same prebuilt variants as the net8 3.1.34 pack inspected here.
- UNVERIFIED: whether `Content` items with `Kind` metadata are a supported way to add `--js-library` files (the targets support it; no docs).
- UNVERIFIED: overriding the Emscripten tool paths without the strict version check, and the ABI consequences.

[^emscripten-manifest]: microsoft.net.workload.emscripten.current 10.0.101 WorkloadManifest.json
[^emscripten-manifest-targets]: microsoft.net.workload.emscripten.current 10.0.101 WorkloadManifest.targets
[^mono-toolchain-manifest]: microsoft.net.workload.mono.toolchain.current 10.0.101 WorkloadManifest.json
[^wasm-props-json]: runtime pack wasm-props.json (10.0.13)
[^emscripten-version-txt]: dotnet/runtime release/10.0 emscripten-version.txt
[^emcc-link-rsp]: runtime pack emcc-link.rsp (10.0.13)
[^browserwasmapp-targets]: BrowserWasmApp.targets 10.0.13
[^wasmapp-common-targets]: WasmApp.Common.targets 10.0.13
[^emsdk-repo-defaults]: EmSdkRepo.Defaults.props 10.0.13
[^net8-cache-pack]: Microsoft.NET.Runtime.Emscripten.3.1.34.Cache.osx-arm64 8.0.22 (installed)
[^runtime-lib-probe]: libSystem.Native.a inspection (runtime pack 10.0.1)
[^probe-build]: WasmProbe builds, 2026-10-16
[^ms-native-deps]: Microsoft Learn — Blazor WebAssembly native dependencies (.NET 10)
[^ms-build-tools-aot]: Microsoft Learn — Blazor WebAssembly build tools and AOT (.NET 10)
[^wasm-features-md]: dotnet/runtime src/mono/wasm/features.md (release/10.0)
[^emscripten-changelog]: Emscripten ChangeLog.md
[^dawn-emdawn-readme]: Dawn emdawnwebgpu pkg README
[^uno-wgpu-wasm-targets]: Uno wgpu-wasm.targets
[^uno-webgpu-targets]: Uno.WinUI.Composition.WebGpu.targets
[^skiasharp-targets]: SkiaSharp.NativeAssets.WebAssembly SkiaSharp.targets
[^skiasharp-blazor-props]: SkiaSharp.Views.Blazor.props
[^runtime-advanced-sample]: dotnet/runtime Wasm.Advanced.Sample.csproj
[^pinvoke-table-gen]: PInvokeTableGenerator.cs (release/10.0)
