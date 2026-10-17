---
type: Reference
title: .NET 10 browser project types, build output layout and the dotnet.js host API
description: Blazor WebAssembly vs the non-Blazor "WebAssembly Browser App" (Microsoft.NET.Sdk.WebAssembly), the exact files the wasmbrowser template generates, what `dotnet run`/`build`/`publish` produce (including the .NET 10 inlining of the boot config into dotnet.js), the full dotnet.js JavaScript API from dotnet.d.ts, and a brief note on headless testing.
tags: [dotnet-wasm, wasmbrowser, blazor, msbuild, dotnet-js, boot-config, webcil, static-web-assets, testing]
sources:
  - id: template-nupkg
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.templates.net10/content/templates/browser
    title: Microsoft.NET.Runtime.WebAssembly.Templates.net10 10.0.13 — browser template content (extracted)
  - id: sdk-wasm-props
    resource: /usr/local/share/dotnet/sdk/10.0.100/Sdks/Microsoft.NET.Sdk.WebAssembly/Sdk/Sdk.props
    title: Microsoft.NET.Sdk.WebAssembly Sdk.props (SDK 10.0.100)
  - id: sdk-wasm-targets
    resource: /usr/local/share/dotnet/sdk/10.0.100/Sdks/Microsoft.NET.Sdk.WebAssembly/Sdk/Sdk.targets
    title: Microsoft.NET.Sdk.WebAssembly Sdk.targets (SDK 10.0.100; implicit HotReload package)
  - id: bundled-versions
    resource: /usr/local/share/dotnet/sdk/10.0.100/Microsoft.NETCoreSdk.BundledVersions.props
    title: SDK 10.0.100 BundledVersions.props (KnownWebAssemblySdkPack Microsoft.NET.Sdk.WebAssembly.Pack)
  - id: wasm-sdk-pack-targets
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.sdk.webassembly.pack/build/Microsoft.NET.Sdk.WebAssembly.Browser.targets
    title: Microsoft.NET.Sdk.WebAssembly.Pack 10.0.13 Browser.targets (boot config, fingerprinting, webcil, run command)
  - id: wasm-sdk-pack-props
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.sdk.webassembly.pack/build/Microsoft.NET.Sdk.WebAssembly.Browser.props
    title: Microsoft.NET.Sdk.WebAssembly.Pack 10.0.13 Browser.props
  - id: wasmapp-common-targets
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.sdk/Sdk/WasmApp.Common.targets
    title: WasmApp.Common.targets 10.0.13 (AppBundle path, RunCommand for workload builds)
  - id: browserwasmapp-targets
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.sdk/Sdk/BrowserWasmApp.targets
    title: BrowserWasmApp.targets 10.0.13 (WasmAppBuilder ConfigFileName dotnet.boot.js)
  - id: mono-toolchain-targets
    resource: /usr/local/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.mono.toolchain.current/10.0.101/WorkloadManifest.targets
    title: Mono toolchain WorkloadManifest.targets 10.0.101
  - id: dotnet-d-ts
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.netcore.app.runtime.mono.browser-wasm/runtimes/browser-wasm/native/dotnet.d.ts
    title: dotnet.d.ts from runtime pack 10.0.13 ("not considered public API with backward compatibility guarantees")
  - id: dotnet-js
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.netcore.app.runtime.mono.browser-wasm/runtimes/browser-wasm/native/dotnet.js
    title: dotnet.js loader 10.0.13 (builder methods not in dotnet.d.ts)
  - id: runtime-package-json
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.netcore.app.runtime.mono.browser-wasm/runtimes/browser-wasm/native/package.json
    title: 'package.json in runtime pack (name @microsoft/dotnet-runtime, types dotnet.d.ts)'
  - id: npm-lookup
    resource: https://registry.npmjs.org/@microsoft%2fdotnet-runtime
    title: npm registry lookup for @microsoft/dotnet-runtime (returned "Not found" on 2026-10-16)
  - id: probe-build
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/proj/WasmProbe
    title: Throwaway project from the 10.0.13 template, built/published with SDK 10.0.100 (no wasm-tools)
  - id: ms-wasm-browser-app
    resource: https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/wasm-browser-app?view=aspnetcore-10.0
    title: JSImport/JSExport interop with a WebAssembly Browser App project (.NET 10)
  - id: ms-migrate-10
    resource: https://learn.microsoft.com/aspnet/core/migration/90-to-100
    title: Migrate from ASP.NET Core in .NET 9 to .NET 10 (boot configuration inlined into dotnet.js)
  - id: wasm-features-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/features.md
    title: Configuring and hosting .NET WebAssembly applications (release/10.0)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:01:15Z }
status: draft
---

# Project types

| | Blazor WebAssembly | WebAssembly Browser App ("wasmbrowser") | Legacy `Microsoft.NET.Sdk` + `RuntimeIdentifier=browser-wasm` |
|---|---|---|---|
| SDK | `Microsoft.NET.Sdk.BlazorWebAssembly` | **`Microsoft.NET.Sdk.WebAssembly`** (in the SDK: `sdk/10.0.100/Sdks/Microsoft.NET.Sdk.WebAssembly`) | `Microsoft.NET.Sdk` (+ Mono WasmApp targets) |
| UI model | Razor components, `IJSRuntime` | none: you own `index.html`/`main.js`, interop through `[JSImport]/[JSExport]` | none |
| Needs `wasm-tools`? | optional (relink/AOT only) | optional (relink/AOT only) | **required** (`_UsingBlazorOrWasmSdk != true` is in the `_WasmNativeWorkloadNeeded` list)[^mono-toolchain-targets] |
| Output | `wwwroot/_framework` via static web assets | `wwwroot/_framework` via static web assets | `bin/<cfg>/<tfm>/browser-wasm/AppBundle` (`WasmGenerateAppBundle`)[^wasmapp-common-targets] |
| Fit for a game | heavy (renderer, DI); `IJSRuntime` adds overhead | **best: minimal host, full control of the page and loop** | superseded |

`Microsoft.NET.Sdk.WebAssembly` props set `RuntimeIdentifier=browser-wasm`, `UseMonoRuntime=true`, `SelfContained=true` and `PublishTrimmed=true` (unless set). They import the StaticWebAssets and Publish SDKs.[^sdk-wasm-props] The real build logic lives in the NuGet package **`Microsoft.NET.Sdk.WebAssembly.Pack`** (the SDK's `KnownWebAssemblySdkPack`), downloaded on restore. Its version is pinned to the workload manifest's runtime version (10.0.1 here).[^bundled-versions][^mono-toolchain-targets][^probe-build] The pack's props also set `AppendRuntimeIdentifierToOutputPath=false` (hence `bin/<cfg>/net10.0/` without `browser-wasm/`), `OutputType=exe`, `TrimMode=full` for non-Blazor projects and `StaticWebAssetBasePath=/`.[^wasm-sdk-pack-props] In .NET 10, Debug builds also get an implicit `PackageReference` to `Microsoft.DotNet.HotReload.WebAssembly.Browser` unless `WasmEnableHotReload=false`. Set that to `false` for bundler-friendly output (Vite etc.).[^sdk-wasm-targets][^ms-wasm-browser-app]

# The `wasmbrowser` template (Microsoft.NET.Runtime.WebAssembly.Templates.net10 10.0.13)

Install with `dotnet new install Microsoft.NET.Runtime.WebAssembly.Templates` (or the `wasm-experimental` workload), then run `dotnet new wasmbrowser`. Template identity is `WebAssembly.Browser.10.0`, name "WebAssembly Browser App", with a single framework choice, `net10.0`.[^template-nupkg][^ms-wasm-browser-app] The template is **not installed on this Mac**; the content below comes from the extracted nupkg.

`browser.0.csproj`:[^template-nupkg]

```xml
<Project Sdk="Microsoft.NET.Sdk.WebAssembly">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>            <!-- required by the JSImport/JSExport source generator -->
    <OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>  <!-- rewrites index.html: preload link + importmap -->
  </PropertyGroup>
  <ItemGroup>
    <StaticWebAssetFingerprintPattern Include="JS" Pattern="*.js" Expression="#[.{fingerprint}]!" />
  </ItemGroup>
</Project>
```

Other files:[^template-nupkg]
- `Program.cs`: top-level statements. Prints "Hello, Browser!", then `while (true) { StopwatchSample.Render(); await Task.Delay(1000); }`. A `partial class StopwatchSample` has `[JSImport("dom.setInnerText", "main.js")]` and `[JSExport]` methods `Toggle`, `Reset`, `IsRunning`.
- `Properties/AssemblyInfo.cs`: `[assembly:System.Runtime.Versioning.SupportedOSPlatform("browser")]`.
- `Properties/launchSettings.json`: `applicationUrl` `https://localhost:5001;http://localhost:5000` (template randomises 7000–7300 / 5000–5300); `inspectUri` `{wsProtocol}://{url.hostname}:{url.port}/_framework/debug/ws-proxy?browser={browserInspectUri}`.
- `wwwroot/index.html`: `<link rel="preload" id="webassembly" />`, an empty `<script type="importmap"></script>` and `<script type='module' src="main#[.{fingerprint}].js">`. The build fills these placeholders.
- `wwwroot/main.js`:

```js
import { dotnet } from './_framework/dotnet.js'
const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet
    .withApplicationArguments("start")
    .create();
setModuleImports('main.js', { dom: { setInnerText: (selector, time) => document.querySelector(selector).innerText = time } });
const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);
// … exports.StopwatchSample.Reset() / Toggle() from button handlers …
await runMain();   // runs C# Main and keeps the runtime alive
```

# Build output (measured on SDK 10.0.100, no workload)

`dotnet build -c Debug`:[^probe-build]
- `bin/Debug/net10.0/` holds raw copies (`dotnet.js`, `dotnet.native.wasm`, `*.dll`, `icudt_*.dat`, `WasmProbe.runtimeconfig.json`, `*.staticwebassets.*.json`; 190 entries).
- `bin/Debug/net10.0/wwwroot/_framework/` is what gets served: 184 fingerprinted files (e.g. `dotnet.native.muve7a13r4.wasm`, `System.Private.CoreLib.<fp>.wasm`), each with a `.gz` sibling, about 36 MB because Debug does not trim.

`dotnet publish -c Release` → `bin/Release/net10.0/publish/wwwroot/` holds `index.html`, `main.<fp>.js`, and `_framework/` with 11 files after trimming, each with `.br` and `.gz`. The log printed: `Publishing without optimizations. Although it's optional for Blazor, we strongly recommend using 'wasm-tools' workload!`[^probe-build] Sizes are in [/dotnet-wasm/runtime-overview-net10.md](/dotnet-wasm/runtime-overview-net10.md#payload-what-a-minimal-app-downloads-measured).

The published `index.html` gets a `<link href="_framework/dotnet.<fp>.js" rel="preload" as="script" fetchpriority="high" crossorigin="anonymous" integrity="sha256-…">`. It also gets an import map that maps `./_framework/dotnet.js`, `dotnet.native.js`, `dotnet.runtime.js` and `./main.js` to their fingerprinted names, with an `integrity` section.[^probe-build]

## `_framework` file roles

| File | Role |
|---|---|
| `dotnet.js` | Loader ES module and public JS API (`dotnet` builder). **In .NET 10 it also contains the boot config** (see below) |
| `dotnet.runtime.js` | .NET↔browser integration (interop, marshalling, timers, HTTP) |
| `dotnet.native.js` | Emscripten-generated glue (`MODULARIZE`, `EXPORT_ES6`, `EXPORT_NAME='createDotnetRuntime'`) |
| `dotnet.native.wasm` | Mono runtime + interpreter (+ AOT code when AOT'd) |
| `dotnet.native.worker.mjs` | pthread worker script, multithreaded runtime only |
| `dotnet.diagnostics.js` | only with `EnableDiagnostics=true` |
| `*.wasm` (other) | managed assemblies in **Webcil** wrapping (`*.dll` when `WasmEnableWebcil=false`) |
| `icudt_*.dat` | ICU data shards |
| `*.js.map`, `dotnet.native.js.symbols` | source map / wasm symbol map (debug aids) |

[^wasm-features-md][^browserwasmapp-targets][^probe-build]

## Boot config: `blazor.boot.json` → inlined into `dotnet.js` (or `dotnet.boot.js`)

- Microsoft's .NET 10 migration guide: "Blazor's boot configuration, which prior to the release of .NET 10 existed in a file named `blazor.boot.json`, has been inlined into the `dotnet.js` script."[^ms-migrate-10]
- The pack targets agree. For `net10.0`, `_WasmInlineBootConfig` defaults to `true` when `WasmBootConfigFileName` is empty, and the boot config file name then *is* `dotnet.js`. With `WasmInlineBootConfig=false` it is `dotnet.boot.js`; before .NET 10 it was `blazor.boot.json`.[^wasm-sdk-pack-targets] The published `dotnet.<fp>.js` ends with `/*json-start*/{ … "mainAssemblyName", "resources", "debugLevel": 0, "linkerEnabled": true, "globalizationMode": "sharded", "runtimeConfig": {…} }/*json-end*/`.[^probe-build]
- **Confirmed by build:** `-p:WasmInlineBootConfig=false` produced `_framework/dotnet.boot.<fp>.js`, an ES module beginning `export const config = /*json-start*/{ "mainAssemblyName": "WasmProbe", "applicationEnvironment": "Development", "resources": {…`.[^probe-build]
- Workload/AppBundle builds (`WasmAppBuilder`) default `ConfigFileName` to `dotnet.boot.js`.[^browserwasmapp-targets]
- The custom Blazor resource cache was removed (`BlazorCacheBootResources` → warning "Custom Blazor cache has been removed …"). Everything is fingerprinted and left to the HTTP cache.[^wasm-sdk-pack-targets][^ms-migrate-10]
- `WasmApplicationEnvironmentName` sets the environment (`Development` for build, `Production` for publish).[^ms-migrate-10][^wasm-sdk-pack-targets]

## Lazy loading

`BlazorWebAssemblyLazyLoad` items are honoured by `Microsoft.NET.Sdk.WebAssembly` (passed as `LazyLoadedAssemblies` to `GenerateWasmBootJson`). The runtime contains a `LoadLazyAssembly` path that errors with "… must be marked with 'BlazorWebAssemblyLazyLoad' item group …".[^wasm-sdk-pack-targets][^dotnet-js] UNVERIFIED: the public, non-Blazor API for triggering a lazy load (Blazor uses `LazyAssemblyLoader`).

# What `dotnet run` does

For `Microsoft.NET.Sdk.WebAssembly` the pack sets `RunCommand=dotnet` and `RunArguments=exec "<nuget>/microsoft.net.sdk.webassembly.pack/10.0.1/WasmAppHost/WasmAppHost.dll" --use-staticwebassets --runtime-config "<bin>/WasmProbe.runtimeconfig.json"`. It adds `--apply-cop-headers` when `WasmEnableThreads=true`, plus `$(WasmHostArguments)`.[^wasm-sdk-pack-targets][^probe-build] `WasmAppHost` is a small ASP.NET Core static server. It serves the static web assets at the `launchSettings.json` URL and hosts the debugging proxy (`BrowserDebugHost`) used by `inspectUri`. For non-SDK workload builds it serves `AppBundle` instead.[^wasmapp-common-targets]

# The dotnet.js JavaScript API

Types live in **`dotnet.d.ts` inside the runtime pack** (`runtimes/browser-wasm/native/dotnet.d.ts`). It is not copied to `_framework`, and its header says "This is not considered public API with backward compatibility guarantees."[^dotnet-d-ts] The pack's `package.json` names it `@microsoft/dotnet-runtime` with `"types": "dotnet.d.ts"`, but **that npm package does not exist** (registry returned "Not found", 2026-10-16). Copy `dotnet.d.ts` from the NuGet cache to get typings.[^runtime-package-json][^npm-lookup]

Exports of `dotnet.js`: `dotnet` (a `DotnetHostBuilder`), `exit(code, reason?)`, default `createDotnetRuntime(moduleFactory)`. There is also the global `globalThis.getDotnetRuntime(runtimeId)`.[^dotnet-d-ts]

## `DotnetHostBuilder`

Declared in `dotnet.d.ts`: `withConfig(MonoConfig)`, `withConfigSrc(url)`, `withApplicationArguments(...args)`, `withEnvironmentVariable(name, value)`, `withEnvironmentVariables(obj)`, `withVirtualWorkingDirectory(path)`, `withDiagnosticTracing(bool)`, `withDebugging(level)`, `withMainAssembly(name)`, `withApplicationArgumentsFromQuery()`, `withApplicationEnvironment(env)`, `withApplicationCulture(culture)`, `withResourceLoader(cb)`, `download()`, `create(): Promise<RuntimeAPI>`, `run(): Promise<number>` (runs Main and **exits** the runtime).[^dotnet-d-ts]

Present in `dotnet.js` but not typed: `withRuntimeOptions(string[])` (e.g. `--no-jiterpreter-traces-enabled`), `withModuleConfig(obj)` (merged into the Emscripten module config, e.g. `instantiateWasm`, `onAbort`), `withInterpreterPgo(enabled, saveDelay)`, `withExitOnUnhandledError()`, `withExitCodeLogging()`, `withElementOnExit()`, `withAsyncFlushOnExit()`, `withConsoleForwarding()`, `withWaitingForDebugger(ms)`, `withInteropCleanupOnExit()`, `withDumpThreadsOnNonZeroExit()`, `withOnConfigLoaded(cb)`.[^dotnet-js] UNVERIFIED: stability of the untyped methods across servicing.

`MonoConfig` fields include `mainAssemblyName`, `globalizationMode` (`sharded|all|invariant|custom`), `debugLevel`, `environmentVariables`, `runtimeConfig.runtimeOptions.configProperties`, `maxParallelDownloads` (default 16), `enableDownloadRetry`, `remoteSources`, `pthreadPoolInitialSize/UnusedSize`, `interpreterPgo`, `applicationArguments`, `virtualWorkingDirectory` and `resources` (`assembly`, `lazyAssembly`, `wasmNative`, `jsModuleNative/Runtime/Worker`, `icu`, `vfs`, `satelliteResources`, `modulesAfterConfigLoaded/RuntimeReady`). Every asset may carry `buffer` / `pendingDownload` / `moduleExports` so that a host or bundler can supply bytes itself.[^dotnet-d-ts]

## `RuntimeAPI` (returned by `create()`)

| Group | Members |
|---|---|
| Run | `runMain(mainAssemblyName?, args?)` (**keeps the runtime alive**), `runMainAndExit(...)`, `exit(code, reason?)`, `setEnvironmentVariable`, `getConfig()`, `invokeLibraryInitializers(name, args)` |
| Interop | `setModuleImports(moduleName, obj)` (the name is the 2nd argument of `[JSImport]`), `getAssemblyExports(assemblyName): Promise<any>` (namespaced object tree of `[JSExport]` methods) |
| Memory | `getHeapU8/I8/U16/I16/U32/I32/I52/U52/I64Big/F32/F64(offset)`, the matching `setHeap*`, and `localHeapViewI8/I16/I32/I64Big/U8/U16/U32/F32/F64()`, documented as "a short term view of the WASM linear memory. Don't store the reference, don't use it after await." |
| Diagnostics | `collectCpuSamples`, `collectMetrics`, `collectGcDump`, `connectDSRouter(url)` (need `EnableDiagnostics`) |
| Misc | `Module` (Emscripten module: `_malloc`, `_free`, `ccall`, `cwrap`, `UTF8ToString`, `addFunction(fn, sig)`, `stackSave/Restore/Alloc`, FS helpers …), `runtimeId`, `runtimeBuildInfo { productVersion, gitHash, buildConfiguration, wasmEnableThreads, wasmEnableSIMD, wasmEnableExceptionHandling }`, `INTERNAL` |

[^dotnet-d-ts]

`Module.HEAPU8` is **not in the typings**. At run time `dotnet.native.js` (Emscripten 3.1.56) still assigns `Module["HEAPU8"]=HEAPU8=new Uint8Array(b)` (and `HEAP8/16/32/F32/F64`) inside `updateMemoryViews`, which runs after every memory growth. Either `localHeapViewU8()` or `api.Module.HEAPU8` therefore works. The former is the supported one.[^dotnet-d-ts] Exported Emscripten runtime methods are fixed by `EXPORTED_RUNTIME_METHODS` (`FS, out, err, ccall, cwrap, setValue, getValue, UTF8ToString, …, addFunction, …, wasmExports`); more can be added with `EmccExportedRuntimeMethod` items, but only after a relink. See [/dotnet-wasm/msbuild-property-reference.md](/dotnet-wasm/msbuild-property-reference.md).

# Automated / headless testing (brief)

- `dotnet run` starts `WasmAppHost` on the `launchSettings.json` URL. Any static server works for `publish/wwwroot` as long as `.wasm` is served as `application/wasm` (and with COOP/COEP headers for threads).[^wasm-features-md]
- For Playwright/Puppeteer: load the page, collect `console` messages (C# `Console.WriteLine` → `console.log`), and wait for a marker. Built-in helpers: `dotnet.withElementOnExit()` appends `<label id="tests_done">{exitCode}</label>` to `<body>` when the runtime exits (red background when non-zero), `withExitCodeLogging()` logs the exit code, and `runMainAndExit()` resolves with Main's exit code.[^dotnet-js]
- `wasmconsole` (same template package) runs a .NET wasm app under Node (`node main.mjs`), which is handy for pure-logic tests without a GPU.[^ms-wasm-browser-app] Vite integration is covered in [/webgpu/](/webgpu/) / [/experiments/](/experiments/) by other agents.

## Open questions

- UNVERIFIED: non-Blazor public API for lazy assembly loading.
- UNVERIFIED: stability of the builder methods missing from `dotnet.d.ts` (`withRuntimeOptions`, `withModuleConfig`, …).
- UNVERIFIED: whether a future servicing release publishes `@microsoft/dotnet-runtime` to npm.

[^template-nupkg]: Microsoft.NET.Runtime.WebAssembly.Templates.net10 10.0.13 browser template
[^sdk-wasm-props]: Microsoft.NET.Sdk.WebAssembly Sdk.props (SDK 10.0.100)
[^sdk-wasm-targets]: Microsoft.NET.Sdk.WebAssembly Sdk.targets (SDK 10.0.100)
[^bundled-versions]: Microsoft.NETCoreSdk.BundledVersions.props (SDK 10.0.100)
[^wasm-sdk-pack-targets]: Microsoft.NET.Sdk.WebAssembly.Browser.targets 10.0.13
[^wasm-sdk-pack-props]: Microsoft.NET.Sdk.WebAssembly.Browser.props 10.0.13
[^wasmapp-common-targets]: WasmApp.Common.targets 10.0.13
[^browserwasmapp-targets]: BrowserWasmApp.targets 10.0.13
[^mono-toolchain-targets]: mono toolchain WorkloadManifest.targets 10.0.101
[^dotnet-d-ts]: dotnet.d.ts (runtime pack 10.0.13)
[^dotnet-js]: dotnet.js (runtime pack 10.0.13)
[^runtime-package-json]: runtime pack package.json
[^npm-lookup]: npm registry lookup, 2026-10-16
[^probe-build]: WasmProbe build/publish outputs, 2026-10-16
[^ms-wasm-browser-app]: Microsoft Learn — WebAssembly Browser App project (.NET 10)
[^ms-migrate-10]: Microsoft Learn — Migrate from .NET 9 to .NET 10
[^wasm-features-md]: dotnet/runtime src/mono/wasm/features.md (release/10.0)
