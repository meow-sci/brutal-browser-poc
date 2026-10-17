---
type: Experiment Report
title: exp-05 results - Vite 8 packaging of a .NET 10 wasm app and headless WebGPU test
description: Ledger of every experiment run in projects/exp-05-vite-packaging (static serving plugin, public/, naive bundling, unplugin-dotnet-wasm, COOP/COEP, dotnet watch, headless Chromium WebGPU matrix) with commands, observed outcomes, numbers and reproduction steps.
tags: [experiment, vite, dotnet-wasm, webgpu, headless, puppeteer, results, exp-05]
sources:
  - id: exp05-readme
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/README.md
    title: exp-05 README (layout and commands)
  - id: exp05-fixture-csproj
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/dotnet-fixture/Fixture.csproj
    title: exp-05 .NET fixture csproj
  - id: exp05-fixture-program
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/dotnet-fixture/Program.cs
    title: exp-05 .NET fixture Program.cs (Main, JSExport Tick, JSImport host.log)
  - id: exp05-plugin
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/app/plugins/dotnet-framework.ts
    title: exp-05 dotnet-framework Vite plugin
  - id: exp05-main
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/app/src/main.ts
    title: exp-05 main.ts
  - id: exp05-headless
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/app/test/headless.mjs
    title: exp-05 headless.mjs
  - id: exp05-matrix
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/app/test/matrix.mjs
    title: exp-05 matrix.mjs
  - id: exp05-vite-configs
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/app/vite.config.ts
    title: exp-05 Vite configs (vite.config.ts, vite.bundled.config.ts, vite.negative.config.ts, vite.public.config.ts, vite.coi.config.ts)
  - id: exp05-out-plain
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/out/publish-plain/wwwroot/_framework
    title: dotnet publish output (plain)
  - id: exp05-out-bundler
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/out/publish-bundler/wwwroot/_framework
    title: dotnet publish output (WasmBundlerFriendlyBootConfig=true)
  - id: runtime-pack
    resource: /Users/asherwin/.nuget/packages/microsoft.netcore.app.runtime.mono.browser-wasm/10.0.1/runtimes/browser-wasm/native
    title: Mono browser-wasm runtime pack 10.0.1 (dotnet.js, dotnet.d.ts, ICU data)
  - id: unplugin-readme
    resource: https://github.com/ArcadeMode/unplugin-dotnet-wasm/blob/main/unplugin-dotnet-wasm/README.md
    title: unplugin-dotnet-wasm README
  - id: runtime-issue-132789
    resource: https://github.com/dotnet/runtime/issues/132789
    title: dotnet/runtime#132789
  - id: dotnet-browser-targets
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/nuget/Microsoft.NET.Sdk.WebAssembly.Pack/build/Microsoft.NET.Sdk.WebAssembly.Browser.targets
    title: Microsoft.NET.Sdk.WebAssembly.Browser.targets (release/10.0)
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T17:45:00Z }
status: draft
---

# exp-05 results

All runs on 2026-10-16. Folder: `/Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/` (README there has the run commands).[^exp05-readme]

## Environment

| Item | Value |
|---|---|
| Machine | Apple M4 Pro, macOS 27.0.1 (Darwin 27.0.0), arm64 |
| Node / npm | 24.18.0 / 12.0.2 (npm 12 blocks install scripts by default) |
| .NET SDK | 10.0.100 (workload manifest 10.0.101); runtime pack `Microsoft.NETCore.App.Runtime.Mono.browser-wasm` 10.0.1; `wasm-tools` workload NOT installed (only `wasm-tools-net8`), so no AOT/relink |
| Vite | 8.3.4 (Rolldown 1.2.14), TypeScript 6.0.x (scaffold), `puppeteer-core` 25.14.0, `@webgpu/types` 0.1.74, `unplugin-dotnet-wasm` 1.5.2, `playwright-core` 1.64.1 (scratch install only) |
| Browsers | Chrome for Testing 151.0.7922.34 (new headless), chrome-headless-shell 151.0.7922.34, Microsoft Edge 155.0.4283.45. **Google Chrome is not installed.** No browser was downloaded. |
| Footprint | `app/node_modules` about 94 MB; no global installs |

## Fixture

`dotnet-fixture/` is a `Microsoft.NET.Sdk.WebAssembly` `net10.0` console app (`OutputType=Exe`, `AllowUnsafeBlocks`, `InvariantGlobalization`, `WasmEnableHotReload=false`; `-p:BundlerFriendly=true` maps to `WasmBundlerFriendlyBootConfig=true`). `Program.Main` prints `Fixture: Main() ran`; `[JSExport] Tick(double)` calls `[JSImport("host.log","main.js")]` and returns 42.[^exp05-fixture-csproj][^exp05-fixture-program] `src/main.ts` probes WebGPU, boots the runtime (`create`, `setModuleImports('main.js', ...)`, `getAssemblyExports`, `runMain`) and calls `Tick` five times from `requestAnimationFrame`.[^exp05-main]

## Experiment ledger

| ID | What | Outcome |
|---|---|---|
| E1 | Scaffold `npm create vite@latest app -- --template vanilla-ts --no-interactive` (create-vite 9.2.1) | Vite 8.3.4 installed; `npm install` ok (4 s warm). TypeScript pinned `~6.0.2`. |
| E2 | `dotnet build -c Debug` of the fixture | `bin/Debug/net10.0/wwwroot/_framework/` complete: 180 files, 24,023,184 bytes; inline config in `dotnet.js` listed 177 resource entries, all present. No `AppBundle`. |
| E3 | Same with `<RuntimeIdentifier>browser-wasm</RuntimeIdentifier>` (scratch copy) | Same layout (no `browser-wasm/AppBundle`). |
| E4 | `dotnet publish -c Release -o ../out/publish-plain` | `wwwroot/_framework/`: 8 real files (4,470,408 B) + `.br` (1,410,541 B) + `.gz` (1,736,096 B). Boot config inlined in `dotnet.js`. 6.6 s wall (first publish).[^exp05-out-plain] |
| E5 | `dotnet publish -p:BundlerFriendly=true -o ../out/publish-bundler` | `dotnet.js` (42 kB) now starts with `import * as ... from "./dotnet.runtime.<hash>.js"`, `import ... from "./Name.<hash>.wasm"` and config entries gain `moduleExports` / `resolvedUrl`.[^exp05-out-bundler] |
| E6 | `-p:WasmInlineBootConfig=false` (scratch) | Separate `dotnet.boot.js` (`export const config = ...`); `dotnet.js` references it. |
| E7 | `-p:WasmFingerprintDotnetJs=true` (scratch) | `dotnet.yy522yscju.js` instead of `dotnet.js`. |
| E8 | **Approach A dev** (`vite.config.ts`[^exp05-vite-configs]), plugin `frameworkDir` = Debug `_framework`, `vite` + `headless.mjs` | PASS in cft, edge, shell(`--use-angle=metal`/swiftshader); .NET `Tick` 42; 5 JSImport log lines. |
| E9 | Approach A dev, plugin `manifest` = `Fixture.staticwebassets.runtime.json`[^exp05-plugin] | PASS; also with Hot Reload ON (scratch fixture) it serves `/_content/Microsoft.DotNet.HotReload.WebAssembly.Browser/...lib.module.js` (200). |
| E10 | Approach A dev, `frameworkDir` with Hot Reload ON | App still booted (`Tick` 42) but console warning `MONO_WASM: Failed to import library initializer` and HTTP 404 for the `_content/...lib.module.js` URL. |
| E11 | Approach A `vite build` (copy publish `_framework`) + `vite preview` | PASS. Chrome sent `Accept-Encoding: gzip, deflate, br, zstd`; plugin answered `Content-Encoding: br` for all 8 files (wasm 976,573 B on the wire). dist 8.6 MB (includes `.br` and `.gz`). Vite JS chunk 5.23 kB. |
| E12 | E11 with `--base /games/brutal/` | PASS (`BASE_URL` + middleware under base). |
| E13 | Reload with `--net --reload` | First load: 200 x 8; reload: 304 x 8 (wasm requests carry `Cache-Control: max-age=0`). |
| E14 | C# edit -> `dotnet build -c Debug` while `vite` runs | Build 0.9 to 1.1 s; plugin logged `dotnet.js.gz changed -> full reload` once; new `hostLog` text appeared. Old fingerprinted `Fixture.<hash>.wasm` replaced by the new name. |
| E15 | `dotnet watch build --non-interactive -c Debug` | Rebuilt on each of 2 edits plus initial (0.84 to 0.90 s each). |
| E16 | **Approach B dev**: `dotnet build -p:BundlerFriendly=true` + `vite -c vite.bundled.config.ts` (`unplugin-dotnet-wasm` discovery mode) | PASS; 361 HTTP responses (runtime JS served via `/@id/__x00__dotnet-wasm:_framework/*.js`, each wasm via a small stub module). |
| E17 | Approach B `vite build` from Debug build output, then preview | PASS. `dist-bundled` 23 MB, 179 asset files, entry JS 366.25 kB (113.08 kB gzip), wasm files hashed `Name.<dotnethash>-<vitehash>.wasm`. Warning `Module "module" has been externalized for browser compatibility`. |
| E18 | Approach B `vite build` from Release publish (`dotnetOutputDir` = publish root), then preview | PASS. `dist-bundled` 4.2 MB, entry JS 325.88 to 326.03 kB (97.93 to 97.99 kB gzip), 5 wasm assets, no `.br`. Pointing `dotnetOutputDir` at `.../wwwroot/_framework` instead fails: `DiscoveryError: Endpoints manifest not found`. |
| E19 | Approach B with `--base /games/brutal/` | PASS. |
| E20 | Negative: statically import plain `dotnet.js` into the Vite graph (`NEGATIVE=plain`) | Build OK (warnings: `process`/`module` externalized; `new URL("dotnet.native.wasm", import.meta.url) doesn't exist at build time`). Runtime FAIL: 404 for `/assets/dotnet.runtime.0j6ezsi0n0.js` and `/assets/dotnet.native.cs8mcre4gh.js`; `/assets/*.wasm` returned `200 text/html` (SPA fallback); `Failed to fetch dynamically imported module`. |
| E21 | Negative: import bundler-friendly `dotnet.js` with no plugin (`NEGATIVE=bundlerfriendly`) | Build FAIL: Rolldown "Missing export" on every `import X from "./Name.<hash>.wasm"` (Vite >= 8.1 treats `.wasm` imports as ESM-integration modules). |
| E22 | **Option (a)**: no plugin, `publicDir` = `out/publish-plain/wwwroot` | Dev FAIL: `dotnet.js?import` -> HTTP 500 "This file is in ../out/publish-plain/wwwroot and will be copied as-is ... should not be imported from source code. It can only be referenced via HTML tags." (plain `GET /_framework/dotnet.js` is 200). Build + preview PASS but the wasm was served raw (3,001,957 B, no `Content-Encoding`), JS gzip only. |
| E23 | COOP/COEP via `server.headers` (`vite.coi.config.ts`) | PASS; `crossOriginIsolated` false -> true; .NET + WebGPU unaffected. |
| E24 | Headless matrix (below) | See table. |
| E25 | Playwright-core 1.64.1 with `executablePath` (scratch install) | Same adapter outcomes as puppeteer-core for cft/shell/edge with `[]` and `['--enable-unsafe-webgpu']`. |

## Headless WebGPU matrix (final script version; `npm run test:matrix`)

Columns: adapter (`vendor/architecture`), buffer-readback OK, canvas screenshot centre pixel, verdict from the script (which also asserts `Tick` == 42). "PASS (no GPU)" rows use `--allow-no-gpu`.[^exp05-headless][^exp05-matrix]

| Browser | Preset (flags after defaults) | Adapter | Readback | Screenshot | Script |
|---|---|---|---|---|---|
| cft 151 | none | apple/metal-3 | ok | 255,128,0 | PASS |
| cft 151 | `--enable-unsafe-webgpu` | apple/metal-3 | ok | 255,128,0 | PASS |
| cft 151 | `+ --use-angle=metal` | apple/metal-3 | ok | 255,128,0 | PASS |
| cft 151 | `+ --use-webgpu-adapter=swiftshader` | google/swiftshader (fallback) | ok | 255,128,0 | PASS |
| cft 151 | `+ --use-angle=swiftshader --use-webgpu-adapter=swiftshader` | google/swiftshader (fallback) | ok | 255,128,0 | PASS |
| cft 151 | `+ --enable-webgpu-developer-features` | apple/metal-3 `Apple M4 Pro` | ok | 255,128,0 | PASS |
| shell 151 | none | null | - | white | PASS (no GPU) |
| shell 151 | `--enable-unsafe-webgpu` | google/swiftshader (fallback) | zeros | white | FAIL |
| shell 151 | `+ --use-angle=metal` | apple/metal-3 | ok | 255,128,0 | PASS |
| shell 151 | `+ --use-webgpu-adapter=swiftshader` | google/swiftshader (fallback) | zeros | white | FAIL |
| shell 151 | `+ --use-angle=swiftshader --use-webgpu-adapter=swiftshader` | google/swiftshader (fallback) | ok | 255,128,0 | PASS |
| shell 151 | `+ --enable-webgpu-developer-features` | google/swiftshader `SwiftShader Device (LLVM 10.0.0)` | zeros | white | FAIL |
| edge 155 | none | apple/metal-3 | ok | 255,128,0 | PASS |
| edge 155 | `--enable-unsafe-webgpu` | apple/metal-3 | ok | 255,128,0 | PASS |
| edge 155 | `+ --use-angle=metal` | apple/metal-3 | ok | 255,128,0 | PASS |
| edge 155 | `+ --use-webgpu-adapter=swiftshader` | google/swiftshader (fallback) | ok in 5 of 7 runs, device lost in 2 | white | FAIL (flaky) |
| edge 155 | `+ --use-angle=swiftshader --use-webgpu-adapter=swiftshader` | google/swiftshader (fallback) | ok | 255,128,0 | PASS |
| edge 155 | `+ --enable-webgpu-developer-features` | apple/metal-3 `Apple M4 Pro` | ok | 255,128,0 | PASS |

Extra single runs: cft `--disable-gpu` -> no adapter; cft `--disable-gpu --enable-unsafe-swiftshader --enable-unsafe-webgpu` -> swiftshader adapter, zeros, white; cft/edge `--enable-unsafe-webgpu --enable-dawn-backend-validation --enable-webgpu-developer-features` -> PASS; all three browsers with `--enable-unsafe-webgpu --use-angle=swiftshader` (with or without `--enable-unsafe-swiftshader`) -> PASS on the software adapter. Details and guidance: [/web-packaging/headless-webgpu-testing.md](/web-packaging/headless-webgpu-testing.md).

Whole-run timing: `npm run test:headless` about 0.9 s; the 18-row matrix about 15 to 25 s.

## Corrections to assumptions made before the experiment

1. **No `AppBundle`.** .NET 10 `Microsoft.NET.Sdk.WebAssembly` writes `bin/<cfg>/net10.0/wwwroot/_framework` (build) and `bin/<cfg>/net10.0/publish/wwwroot/_framework` (publish).
2. **No `dotnet.boot.js` by default.** The boot config is inlined in `dotnet.js` for `net10.0`; `dotnet.boot.js` exists only with `WasmInlineBootConfig=false` (E6).[^dotnet-browser-targets]
3. **No `@microsoft/dotnet-runtime` on npm** (404). `dotnet.d.ts` (26,994 bytes) lives in the runtime pack folder.[^runtime-pack]
4. **`.br` files come from `dotnet publish`, not from `dotnet build`** (build gives `.gz` only).
5. **Chrome sends `br` to `http://localhost`.**
6. **Vite 8.1+ gives `.wasm` imports ESM-integration semantics**, which breaks the .NET bundler-friendly `dotnet.js` without a helper plugin.
7. **WebGPU on macOS needs no flags in new headless** (Chrome for Testing 151, Edge 155).
8. A **community plugin already exists** (`unplugin-dotnet-wasm` 1.5.2, plus `@yamachu/vite-plugin-dotnet-wasm`), and the .NET team's guidance is to read the static web assets manifest.[^unplugin-readme][^runtime-issue-132789]

## Reproduce

```sh
cd /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/app
npm install
npm run dotnet:build && npm run dotnet:publish && npm run dotnet:publish:bundler
npm run test:headless                                   # Approach A, dev, cft 151
node test/headless.mjs --serve --browser edge --preset none --expect-dotnet --summary
npm run build && npm run preview &                      # Approach A production; then: node test/headless.mjs --url http://localhost:4173/ --expect-dotnet --net --reload
npm run build:bundled                                   # Approach B production (dist-bundled/)
NEGATIVE=plain npx vite build -c vite.negative.config.ts
npm run test:matrix
```

Caveat for Approach B dev (`npm run dev:bundled`): rebuild the fixture with `dotnet build -p:BundlerFriendly=true` first, and rebuild without the property afterwards before running Approach A, because both write the same `dotnet.js`.

## Limitations

- One small fixture (4 assemblies after trimming); no BRUTAL code, no AOT, no threads, no ICU, no large assets.
- Real GPU adapter was Apple Metal on an M4 Pro only. Linux/Windows flags are documentation-derived.
- Only Chromium-family browsers; no Firefox or Safari.
- Timing numbers are single-machine, warm-cache.

## Open questions

- UNVERIFIED: behaviour on a BRUTAL-sized project (assembly count, trimming warnings, build time, start-up time).
- UNVERIFIED: C# debugger attached through Vite; .NET Hot Reload with Vite.
- UNVERIFIED: production host Brotli negotiation (Cloudflare Pages, Railway).
- UNVERIFIED: why SwiftShader without `--use-angle=swiftshader` yields zeros/white in the headless shell.
- UNVERIFIED: stock Google Chrome (not installed).

[^exp05-readme]: exp-05 README (layout and commands)
[^exp05-fixture-csproj]: exp-05 .NET fixture csproj
[^exp05-fixture-program]: exp-05 .NET fixture Program.cs (Main, JSExport Tick, JSImport host.log)
[^exp05-main]: exp-05 main.ts
[^exp05-headless]: exp-05 headless.mjs
[^exp05-matrix]: exp-05 matrix.mjs
[^runtime-pack]: Mono browser-wasm runtime pack 10.0.1 (dotnet.js, dotnet.d.ts, ICU data)
[^unplugin-readme]: unplugin-dotnet-wasm README
[^runtime-issue-132789]: dotnet/runtime#132789
[^dotnet-browser-targets]: Microsoft.NET.Sdk.WebAssembly.Browser.targets (release/10.0)
[^exp05-out-plain]: dotnet publish output (plain)
[^exp05-out-bundler]: dotnet publish output (WasmBundlerFriendlyBootConfig=true)
[^exp05-vite-configs]: exp-05 Vite configs (vite.config.ts, vite.bundled.config.ts, vite.negative.config.ts, vite.public.config.ts, vite.coi.config.ts)
[^exp05-plugin]: exp-05 dotnet-framework Vite plugin
