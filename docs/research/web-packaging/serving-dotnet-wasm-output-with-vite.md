---
type: Technology Assessment
title: Serving and bundling .NET 10 WebAssembly output with Vite 8
description: Exact layout of .NET 10 Microsoft.NET.Sdk.WebAssembly output, how dotnet.js finds its files, four Vite integration options tested against a real build (static middleware+copy, public/, naive bundling, bundler-friendly + unplugin), a recommended plugin design, community solutions and pitfalls.
tags: [vite, dotnet, wasm, dotnet-js, framework-folder, bundler-friendly, unplugin-dotnet-wasm, static-web-assets, plugin-design]
sources:
  - id: dotnet-features-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/features.md
    title: Configuring and hosting .NET WebAssembly applications (dotnet/runtime features.md, release/10.0)
  - id: dotnet-browser-targets
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/nuget/Microsoft.NET.Sdk.WebAssembly.Pack/build/Microsoft.NET.Sdk.WebAssembly.Browser.targets
    title: Microsoft.NET.Sdk.WebAssembly.Browser.targets (release/10.0)
  - id: dotnet-bootjson-helper
    resource: https://github.com/dotnet/runtime/blob/main/src/tasks/Microsoft.NET.Sdk.WebAssembly.Pack.Tasks/BootJsonBuilderHelper.cs
    title: BootJsonBuilderHelper.cs (bundler-friendly imports generation)
  - id: dotnet-browser-template
    resource: https://github.com/dotnet/runtime/tree/release/10.0/src/mono/wasm/templates/templates/browser
    title: wasmbrowser template (csproj, main.js, index.html, launchSettings.json)
  - id: dotnet-wasm-host
    resource: https://github.com/dotnet/runtime/tree/release/10.0/src/mono/wasm/host
    title: WasmAppHost (DevServer, DebugProxyLauncher)
  - id: learn-wasm-browser-app
    resource: https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/wasm-browser-app?view=aspnetcore-10.0
    title: JSImport/JSExport interop with a WebAssembly Browser App project
  - id: learn-bundler-support
    resource: https://learn.microsoft.com/aspnet/core/blazor/host-and-deploy/#javascript-bundler-support
    title: Blazor host and deploy - JavaScript bundler support
  - id: blazor-samples-react
    resource: https://github.com/dotnet/blazor-samples/tree/main/10.0/DotNetWebAssemblyReact
    title: DotNetWebAssemblyReact sample (Rollup + WasmBundlerFriendlyBootConfig)
  - id: runtime-issue-132789
    resource: https://github.com/dotnet/runtime/issues/132789
    title: "[API Proposal]: public MSBuild extension point to materialize WebAssembly framework assets during build (closed 2026-08-26)"
  - id: unplugin-readme
    resource: https://github.com/ArcadeMode/unplugin-dotnet-wasm/blob/main/unplugin-dotnet-wasm/README.md
    title: unplugin-dotnet-wasm README
  - id: unplugin-architecture
    resource: https://github.com/ArcadeMode/unplugin-dotnet-wasm/blob/main/docs/architecture.md
    title: unplugin-dotnet-wasm architecture notes
  - id: yamachu-readme
    resource: https://github.com/yamachu/vite-plugin-dotnet-wasm
    title: "@yamachu/vite-plugin-dotnet-wasm README"
  - id: avalonia-wasm-docs
    resource: https://docs.avaloniaui.net/docs/deployment/webassembly
    title: Avalonia WebAssembly deployment docs
  - id: vite-features
    resource: https://vite.dev/guide/features
    title: Vite features (WebAssembly)
  - id: vite-assets
    resource: https://vite.dev/guide/assets
    title: Vite static asset handling
  - id: vite-api-plugin
    resource: https://vite.dev/guide/api-plugin
    title: Vite plugin API
  - id: vite-changelog
    resource: https://github.com/vitejs/vite/blob/main/packages/vite/CHANGELOG.md
    title: Vite CHANGELOG
  - id: exp05-plugin
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/app/plugins/dotnet-framework.ts
    title: exp-05 dotnet-framework Vite plugin (Approach A)
  - id: exp05-bundled-config
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/app/vite.bundled.config.ts
    title: exp-05 vite.bundled.config.ts (Approach B)
  - id: exp05-negative-config
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/app/vite.negative.config.ts
    title: exp-05 negative controls
  - id: exp05-public-config
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/app/vite.public.config.ts
    title: exp-05 public/ option config
  - id: exp05-fixture
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/dotnet-fixture/Fixture.csproj
    title: exp-05 .NET fixture (Microsoft.NET.Sdk.WebAssembly, net10.0)
  - id: exp05-results
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/research/web-packaging/experiment-results-exp-05.md
    title: Experiment results exp-05 (raw observations and tables)
  - id: exp01-build-output
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/research/dotnet-wasm/project-types-and-build-output.md
    title: dotnet-wasm project types and build output (exp-01 findings)
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:40:00Z }
status: draft
---

# Serving and bundling .NET 10 WebAssembly output with Vite 8

Scope: non-Blazor `Microsoft.NET.Sdk.WebAssembly` projects (what `dotnet new wasmbrowser` creates), Mono interpreter, `net10.0`, runtime pack 10.0.1, Vite 8.3.4. Every "verified" statement below was observed in `projects/exp-05-vite-packaging` against a real build in headless Chrome for Testing 151 (details: [/web-packaging/experiment-results-exp-05.md](/web-packaging/experiment-results-exp-05.md)).[^exp05-results]

## Recommendation (short)

1. **Do not bundle `dotnet.js` by default. Serve the `_framework/` folder as static files and `import()` `dotnet.js` at runtime from `main.ts` (Approach A).** Dev: a tiny Vite plugin serves the .NET output with correct MIME types and full-reloads on rebuild. Build: the plugin copies the `dotnet publish` `_framework/` (with its `.br`/`.gz` files) into `dist/`. Verified in dev, `vite build` + `vite preview`, and under a sub-path `base`.[^exp05-plugin]
2. Resolve files from the SDK's **static web assets manifest** (`*.staticwebassets.runtime.json`) in dev rather than from a hard-coded `bin/.../wwwroot/_framework` path. The .NET team recommends this and .NET 11 stops copying framework assets into `bin/wwwroot` on a plain build.[^runtime-issue-132789]
3. Set `<WasmEnableHotReload>false</WasmEnableHotReload>` in the C# project; the .NET 10 docs say this produces bundler-compatible output, and we observed the Hot Reload library initializer 404 otherwise (unless the manifest mode also serves `_content/`).[^learn-wasm-browser-app][^exp05-plugin]
4. Keep **Approach B** (bundler-friendly output + `unplugin-dotnet-wasm`) as a fallback if `dotnet.js` must be in the module graph (typed `import { dotnet } from '_framework/dotnet'`, Rolldown-hashed filenames). It works with Vite 8.3.4 in dev and build, but folds ~326 kB of runtime JS into the entry chunk, drops `.br` files, and depends on a young community plugin.[^unplugin-readme][^exp05-bundled-config]

## What the .NET 10 output looks like (verified)

Project type: `<Project Sdk="Microsoft.NET.Sdk.WebAssembly">`, `net10.0`, `OutputType=Exe`, `AllowUnsafeBlocks=true`, `WasmEnableHotReload=false`, `InvariantGlobalization=true`.[^exp05-fixture]

- **There is no `AppBundle` folder.** Output is `bin/<cfg>/net10.0/wwwroot/_framework/` for `dotnet build` and `bin/<cfg>/net10.0/publish/wwwroot/_framework/` for `dotnet publish`. Adding an explicit `<RuntimeIdentifier>browser-wasm</RuntimeIdentifier>` did not change that (observed). `AppBundle` belongs to the older workload/`WasmApp.targets` path, which `features.md` still documents for `net8.0`.[^exp01-build-output][^dotnet-features-md]
- **Debug build (`dotnet build -c Debug`)**: `_framework/` held 180 files, 24,023,184 bytes raw (nothing trimmed: the inline config listed 177 resource entries, namely 171 assemblies, 2 core assemblies, 1 pdb and the runtime JS/wasm, and all were present; 0 missing), plus `.gz` copies (9,177,054 bytes). No `.br` files in a build. Time for an incremental rebuild after a one-line C# change: about 1.1 s wall clock (tiny project).
- **Publish (`dotnet publish -c Release`)**: `publish/wwwroot/_framework/` held 24 files: 8 real files totalling 4,470,408 bytes plus `.br` (1,410,541 bytes) and `.gz` (1,736,096 bytes) siblings. Trimming ran even without the `wasm-tools` workload (the SDK printed "Publishing without optimizations... we strongly recommend using wasm-tools workload").
- **File names are fingerprinted**: `dotnet.native.muve7a13r4.wasm`, `dotnet.runtime.0j6ezsi0n0.js`, `System.Private.CoreLib.qumlskvajz.wasm` (WebCIL-wrapped assemblies use the `.wasm` extension). The entry `dotnet.js` is **not** fingerprinted by default. `-p:WasmFingerprintDotnetJs=true` produced `dotnet.yy522yscju.js` (verified); the template pairs that with `OverrideHtmlAssetPlaceholders` and an import map in `index.html`.[^dotnet-browser-targets][^dotnet-browser-template]
- **Boot config is inlined into `dotnet.js`** for `net10.0`: the file ends with `withConfig(/*json-start*/{ "mainAssemblyName", "resources": {...}, "debugLevel", "globalizationMode", "runtimeConfig": {...} }/*json-end*/)`. A separate `dotnet.boot.js` (an ES module `export const config = ...`) appears only with `-p:WasmInlineBootConfig=false` (verified). `blazor.boot.json` was the pre-.NET 10 name.[^dotnet-browser-targets]
- **A stray `dotnet.js` also sits at the publish root** (`publish/dotnet.js`, 37,868 bytes, the same size as the runtime-pack file, i.e. the copy without a merged boot config); only `wwwroot/_framework/dotnet.js` is the deployable one.
- **Not in the output**: `dotnet.d.ts` (it lives in the runtime pack under `~/.nuget/packages/microsoft.netcore.app.runtime.mono.browser-wasm/10.0.1/runtimes/browser-wasm/native/`) and no `@microsoft/dotnet-runtime` npm package exists (`npm view` returned 404). Copy the d.ts into `src/vendor/`.[^exp05-results]

### How `dotnet.js` locates its files (verified by reading and by a negative control)

- `dotnet.js` is an ES module that computes its own directory from `import.meta.url`. Resource names from the inline config (`dotnet.native.<hash>.wasm`, ...) are resolved relative to that directory, `dotnet.runtime.<hash>.js` and `dotnet.native.<hash>.js` are loaded with dynamic `import()` (the file carries `/*! webpackIgnore: true */` hints on its `import.meta.url` use), and assemblies/`.wasm` are fetched with an `integrity` hash from the config.[^exp05-negative-config]
- The `.wasm` fetch uses `cache: "no-cache"` semantics (the browser sent `Cache-Control: max-age=0` and received `304` on reload) and requires `Content-Type: application/wasm` for `WebAssembly.compileStreaming`; otherwise it logs "does not have the expected content type... falling back to slower ArrayBuffer instantiation".[^dotnet-features-md][^exp05-results]
- Consequence: **if the bundler moves `dotnet.js` into a hashed chunk, the siblings are looked up next to that chunk**. Negative control: statically importing the plain `dotnet.js` made Rolldown build successfully (with "externalized for browser compatibility" warnings for `process` and `module`) but at runtime the browser requested `/assets/dotnet.runtime.0j6ezsi0n0.js` and `/assets/dotnet.native.cs8mcre4gh.js` (404) and `/assets/*.wasm` (answered by the SPA fallback as `200 text/html`). Boot failed with "Failed to fetch dynamically imported module".[^exp05-negative-config]

### The "bundler-friendly" variant (`WasmBundlerFriendlyBootConfig=true`)

- Documented in `features.md` and the .NET 10 Learn pages: it generates JavaScript "that won't run directly in browsers but can be consumed by bundlers".[^dotnet-features-md][^learn-bundler-support]
- Mechanism (read from the task source and observed): the runtime's `/*! bundlerFriendlyImports */` placeholder is replaced by `import * as X from "./dotnet.runtime.<hash>.js"` (runtime JS, used as `moduleExports`) and `import X from "./Name.<hash>.wasm"` (used as `resolvedUrl`) for every asset. A bundler must therefore turn each `.wasm` default import into a **URL string**.[^dotnet-bootjson-helper]
- Microsoft's docs say this output is produced for **publish** and that Microsoft ships **no bundler plugin**; the official sample uses Rollup with `rollup-plugin-import-file` for `.wasm|.dat`, and sets `WasmFingerprintAssets=false` and `CompressionEnabled=false`.[^learn-bundler-support][^blazor-samples-react] In our build, `dotnet build -p:WasmBundlerFriendlyBootConfig=true` also rewrote `dotnet.js` with one `import` per asset (the Vite build then transformed 186 modules), and `unplugin-dotnet-wasm` explicitly supports build output.[^exp05-results][^unplugin-readme]
- Naive import of the bundler-friendly `dotnet.js` into Vite 8.3.4 fails at build time: Rolldown reports "Missing export" for each `import X from "./Name.hash.wasm"`, because since Vite 8.1.0 a direct `.wasm` import means ESM integration (named exports, no default URL).[^exp05-negative-config][^vite-changelog][^vite-features]

## Options compared

| Option | Mechanism | Verified result | Strengths | Weaknesses |
|---|---|---|---|---|
| (a) Copy into `public/` (or `publicDir` = `wwwroot`) | Vite copies `public/` as-is, serves it at `/` in dev | **Build and preview work. Dev fails**: `import('/_framework/dotnet.js')` is requested as `dotnet.js?import` and Vite answers 500 "This file is in ../out/publish-plain/wwwroot and will be copied as-is ... therefore should not be imported from source code. It can only be referenced via HTML tags." Plain `GET` (no `?import`) returns 200. Preview served `.wasm` without `.br` and gzipped the JS only. | Zero plugin code; Vite docs: public files keep exact names, no hashing.[^vite-assets] | Cannot `import()` JS from `public/` in dev; no `.br` negotiation in `vite preview`; copying is a manual step. Needs a workaround (HTML-tag loading or a non-rewritten dynamic import) UNVERIFIED. |
| (b) Plugin: serve + copy `_framework` unbundled (**Approach A**) | `configureServer` middleware (runs before Vite's own, so `?import` is harmless), `closeBundle` copy, `configurePreviewServer` for `.br`/`.gz` | **Works** in dev (Debug build dir and manifest mode), `vite build` + `vite preview` (Release publish dir, `.br` served: `Content-Encoding: br`), and `--base /games/brutal/`. Rebuild triggers full reload. | Keeps .NET's own fingerprints, `.br`/`.gz`, integrity hashes and `no-cache` revalidation; independent of bundler internals; ~150 lines. | You own the plugin; `dotnet.js` is not in the module graph so there are no TS types from the import itself (use a vendored `dotnet.d.ts`). |
| (c) Bundle `dotnet.js` naively (plain output) | `import { dotnet } from '.../_framework/dotnet.js'` | **Fails at runtime** (404 for sibling files, see above). | - | Sibling lookup is relative to the bundle. |
| (c2) Bundle bundler-friendly output, no plugin | same, with `WasmBundlerFriendlyBootConfig=true` | **Fails at build** ("Missing export"). | - | Vite 8.1+ `.wasm` semantics. |
| (c3) Bundler-friendly + `unplugin-dotnet-wasm` (**Approach B**) | `DotnetWasm({...})` from `unplugin-dotnet-wasm/vite` plus `import { dotnet } from '_framework/dotnet'` | **Works** in dev (`dotnet build` Debug output, about 360 requests) and in `vite build` + `preview` from a Debug build and from a Release publish; also under `--base /games/brutal/`. | One module graph, Rolldown content-hashes the `.wasm` files, typed import possible. | Runtime JS (`dotnet.js`+`dotnet.runtime.js`+`dotnet.native.js`) is merged into the entry chunk (326.03 kB, 97.99 kB gzip from the publish output; 366 kB from Debug); no `.br`; `unplugin` requires Node >= 24; plugin is new (npm created 2026-07-11) and small. |
| (d) `?url` imports of individual files | `import u from '.../x.wasm?url'` | Not tried as a standalone solution. By construction it does not help the plain output: `dotnet.js` still resolves names from its own location, not from `u`. It is effectively what (c3) automates for the bundler-friendly output. | - | Per-file manual wiring (about 177 files in Debug). |

## Approach A: recommended plugin design

Implemented in `app/plugins/dotnet-framework.ts` (about 160 lines of TypeScript, no dependencies beyond Vite types).[^exp05-plugin]

- **Inputs**: `frameworkDir` (a directory that already is `_framework`, from `dotnet publish`) or `manifest` (path to `<Project>.staticwebassets.runtime.json` in `bin/<cfg>/net10.0/`, or `obj/<cfg>/net10.0/staticwebassets.development.json`). Also readable from env `DOTNET_FRAMEWORK_DIR` / `DOTNET_MANIFEST`.
- **Manifest mode**: the manifest is a tree (`Root.Children._framework.Children["dotnet.js"].Asset = { ContentRootIndex, SubPath }`) with `ContentRoots[]` (here `bin/.../wwwroot/`, `obj/.../compressed/`, and, with Hot Reload on, the NuGet package `staticwebassets/` folder). The plugin walks it on every request (it is rewritten by every `dotnet build`), skips the build-time `.gz` entries (their `SubPath` contains `{0}`), and serves every top-level route (`_framework/`, `_content/`). Verified with Hot Reload both off and on.
- **Prior art**: `dotnet run`'s WasmAppHost does the same job for the template: it serves the static web assets through ASP.NET Core `MapStaticAssets` with `ServeUnknownFileTypes = true`, driven by the endpoints manifest.[^dotnet-wasm-host]
- **Dev middleware**: `server.middlewares.use(base, handler)` registered *before* Vite's internal middleware so Vite's `?import` query injection and SPA fallback never see these URLs. Sets `Content-Type` (`.wasm` -> `application/wasm`, `.js` -> `text/javascript`, `.dat`/`.pdb`/`.dll` -> `application/octet-stream`, `.map` -> `application/json`), `ETag` (size+mtime), `Cache-Control: no-cache`, honours `If-None-Match` (304).
- **Reload**: `server.watcher.add(<manifest dir or framework dir>)`, listen to `add`/`change`, debounce 300 ms, `server.ws.send({ type: 'full-reload', path: '*' })`. A C# edit followed by `dotnet build` produced exactly one reload and the new code ran (see [/web-packaging/dev-loop-and-debugging.md](/web-packaging/dev-loop-and-debugging.md)).[^vite-api-plugin]
- **Build**: `closeBundle` removes the mount folders under `outDir` and copies every file (including `.br`/`.gz`); `excludeSuffixes: ['.pdb', '.map']` is available for release builds. The files are copied byte for byte, which matters because the runtime verifies SRI hashes.
- **Preview**: `configurePreviewServer` mounts the same middleware over `dist/` with compression negotiation (`br` first, then `gzip`, `Vary: Accept-Encoding`). Plain `vite preview` does not look for `.br` siblings (observed: it served the raw 3 MB wasm and gzip-encoded only the JS).
- **Loader in app code**: `const url = `${import.meta.env.BASE_URL}_framework/dotnet.js`; const { dotnet } = await import(/* @vite-ignore */ url);`. The `@vite-ignore` comment silences the dynamic-import analysis warning. Types come from a vendored `src/vendor/dotnet.d.ts` copied from the runtime pack.
- **Not implemented (ideas, UNVERIFIED)**: `transformIndexHtml` to add `<link rel="modulepreload" href="_framework/dotnet.js">` plus the `rel=preload` hints suggested by `features.md`; spawning `dotnet watch build` from the plugin (what `@yamachu/vite-plugin-dotnet-wasm` does); serving `Content-Encoding` headers from the SDK's `*.staticwebassets.endpoints.json` instead of re-deriving them.

## Approach B: configuration that worked

```ts
// vite.bundled.config.ts
import DotnetWasm from 'unplugin-dotnet-wasm/vite';
export default defineConfig({
  plugins: [DotnetWasm(
    process.env.DOTNET_OUTPUT_DIR
      ? { projectName: 'Fixture', dotnetOutputDir: process.env.DOTNET_OUTPUT_DIR }   // a publish ROOT (contains *.staticwebassets.endpoints.json)
      : { projectName: 'Fixture', projectRoot: '../dotnet-fixture', configuration: 'Debug', targetFramework: 'net10.0', isPublish: false })],
  build: { target: 'es2023' },
});
// src: import { dotnet } from '_framework/dotnet';   (types: // @ts-expect-error or a generated shim)
```

Notes from running it: pointing `dotnetOutputDir` at `.../wwwroot/_framework` fails with `DiscoveryError: Endpoints manifest not found`; it wants the publish root. The build logs `No definition file was generated for ".../dotnet.js"; skipping` at info level and Rolldown prints `Module "module" has been externalized for browser compatibility` (the Node branch of `dotnet.native.js`); both were harmless. The C# project must be built with `WasmBundlerFriendlyBootConfig=true`.[^exp05-bundled-config]

## Community and vendor solutions found

| Solution | What it is | Facts |
|---|---|---|
| `unplugin-dotnet-wasm` (ArcadeMode) | unplugin for Vite, Rolldown, Rollup, webpack, rspack, rsbuild, esbuild, Farm, Bun | npm 1.5.2 (created 2026-07-11, modified 2026-08-18), MIT, 4 GitHub stars at time of writing; peer deps include `vite >=5`, `rolldown >=1`; requires Node >= 24 and .NET SDK >= 10; works with `dotnet build` and `dotnet publish` output by reading `endpoints.json` and (for builds) `runtime.json`; serves assets in dev with the manifest's headers; reloads on manifest change.[^unplugin-readme][^unplugin-architecture] |
| `@yamachu/vite-plugin-dotnet-wasm` | Vite-only plugin that runs `dotnet build/publish/watch`, stages assets from the static web assets manifest into `<cacheDir>/dotnet-wasm/...` and resolves `./_framework/*` imports | npm 0.7.0 (modified 2026-08-27), MIT, 3 stars. NOT tested here. UNVERIFIED whether it expects bundler-friendly output.[^yamachu-readme] |
| Microsoft sample `DotNetWebAssemblyReact` | Rollup + `rollup-plugin-import-file`, `WasmBundlerFriendlyBootConfig`, `WasmFingerprintAssets=false`, `CompressionEnabled=false` | Publish only; README: "Microsoft doesn't provide any such plugin at the moment."[^blazor-samples-react] |
| .NET team guidance | Issue `dotnet/runtime#132789` (closed 2026-08-26): maintainer says to read the static web assets manifest to find all assets; build output "never contained all assets" (app `wwwroot`, NuGet static assets); .NET 11 stopped copying generated/runtime pack assets to `bin/wwwroot`; the internal `_WasmFrameworkCopyToOutputDirectory` exists only for runtime-repo tests.[^runtime-issue-132789] |
| Avalonia browser | Docs describe `dotnet publish` -> static `wwwroot` served with `dotnet-serve`; no Vite guidance found.[^avalonia-wasm-docs] |
| Uno Platform | Ships its own bootstrapper (`Uno.Wasm.Bootstrap`), not Vite based; only seen in search results, not read. UNVERIFIED. |

## Pitfalls checklist

- `dotnet build` is not `dotnet publish`: build output is untrimmed (24 MB for the fixture), has `.gz` only, and in .NET 11 will not hold the framework assets under `bin/wwwroot`. Use the manifest, or publish for production.[^runtime-issue-132789][^exp05-results]
- Hot Reload (default on for Debug) adds a library initializer under `/_content/Microsoft.DotNet.HotReload.WebAssembly.Browser/...lib.module.js`. Serving only `_framework/` yields `404` and a runtime warning `MONO_WASM: Failed to import library initializer` (non-fatal: `Tick` still returned 42). Fix: `WasmEnableHotReload=false`, or serve `_content/` via the manifest.[^exp05-plugin]
- Vite's SPA fallback turns missing assets into `200 text/html`; make sure every `_framework` URL is served by middleware before the fallback (our plugin does); `appType: 'custom'` is a possible alternative (UNVERIFIED).[^exp05-negative-config]
- `public/` files cannot be dynamically imported in dev (500 with `?import`).[^exp05-public-config]
- Never let a plugin rewrite `.wasm`, `.js` in `_framework`, or `dotnet.js` bytes: the runtime verifies `integrity` hashes taken from the boot config. Approach A copies bytes verbatim.[^dotnet-features-md]
- Vite inlines assets below `build.assetsInlineLimit` as base64 (the page fetched does not restate the default; 4096 bytes is Vite's long-standing value, UNVERIFIED here).[^vite-assets] The bundler-friendly facade assemblies are about 4.88 kB so they stayed files in our build, but a tiny assembly could be inlined. UNVERIFIED for approach B; the unplugin emits assets itself.
- `.wasm` MIME must be `application/wasm`, or the runtime falls back to slower ArrayBuffer instantiation.[^exp05-results]
- Use `import.meta.env.BASE_URL` (Approach A) when `base` is not `/`; with a sub-path both approaches booted under `/games/brutal/`.[^exp05-results]
- Keep `.NET` fingerprinting on (`WasmFingerprintAssets` default true for .NET 9+) for Approach A; turn it off only if a bundler pipeline wants stable names (the Microsoft sample does).[^dotnet-browser-targets][^blazor-samples-react]

## Open questions

- UNVERIFIED: how `@yamachu/vite-plugin-dotnet-wasm` compares with `unplugin-dotnet-wasm` in dev start-up and HMR behaviour; neither was benchmarked against a large (BRUTAL-sized) project.
- UNVERIFIED: whether a ProjectReference to class libraries with their own static web assets or JS initializers adds `_content/<pkg>/` routes that Approach A handles (manifest mode serves all top-level routes, but only the Hot Reload package was exercised).
- UNVERIFIED: behaviour of both approaches when the app is AOT-compiled (needs `wasm-tools` workload; none was installed) and when `WasmEnableThreads=true` (extra `dotnet.native.worker.<hash>.mjs`, COOP/COEP).
- UNVERIFIED: a clean workaround for importing `dotnet.js` from `public/` in dev without a plugin.
- UNVERIFIED: how .NET 11 previews change the manifest layout (second-hand from the issue thread only).

[^dotnet-features-md]: Configuring and hosting .NET WebAssembly applications (dotnet/runtime features.md, release/10.0)
[^dotnet-browser-targets]: Microsoft.NET.Sdk.WebAssembly.Browser.targets (release/10.0)
[^dotnet-bootjson-helper]: BootJsonBuilderHelper.cs (bundler-friendly imports generation)
[^dotnet-browser-template]: wasmbrowser template (csproj, main.js, index.html, launchSettings.json)
[^learn-wasm-browser-app]: JSImport/JSExport interop with a WebAssembly Browser App project
[^learn-bundler-support]: Blazor host and deploy - JavaScript bundler support
[^blazor-samples-react]: DotNetWebAssemblyReact sample (Rollup + WasmBundlerFriendlyBootConfig)
[^runtime-issue-132789]: "[API Proposal]: public MSBuild extension point to materialize WebAssembly framework assets during build (closed 2026-08-26)"
[^unplugin-readme]: unplugin-dotnet-wasm README
[^unplugin-architecture]: unplugin-dotnet-wasm architecture notes
[^yamachu-readme]: "@yamachu/vite-plugin-dotnet-wasm README"
[^avalonia-wasm-docs]: Avalonia WebAssembly deployment docs
[^vite-features]: Vite features (WebAssembly)
[^vite-assets]: Vite static asset handling
[^vite-api-plugin]: Vite plugin API
[^vite-changelog]: Vite CHANGELOG
[^exp05-plugin]: exp-05 dotnet-framework Vite plugin (Approach A)
[^exp05-bundled-config]: exp-05 vite.bundled.config.ts (Approach B)
[^exp05-negative-config]: exp-05 negative controls
[^exp05-public-config]: exp-05 public/ option config
[^exp05-fixture]: exp-05 .NET fixture (Microsoft.NET.Sdk.WebAssembly, net10.0)
[^exp05-results]: Experiment results exp-05 (raw observations and tables)
[^exp01-build-output]: dotnet-wasm project types and build output (exp-01 findings)
[^dotnet-wasm-host]: WasmAppHost (DevServer, DebugProxyLauncher)
