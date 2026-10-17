---
type: Experiment Report
title: "exp-07: Mono AOT (RunAOTCompilation) vs the interpreter for the HelloMesh browser build"
description: Measures a trimmed Release publish of samples/HelloMesh/HelloMesh.Browser with and without -p:RunAOTCompilation=true -p:WasmStripILAfterAOT=true on .NET 10 browser-wasm (publish time, _framework size raw and brotli, READY, FRAME 120, PERF avg/p95/max, e2e 17/17), and says when AOT is worth its cost for a game.
tags: [experiment, hellomesh, browser-wasm, mono, aot, interpreter, jiterpreter, performance, size, publish-time, wasm-tools, simd]
sources:
  - id: readme
    resource: ../../../samples/HelloMesh/README.md
    title: HelloMesh README (browser section, interpreter numbers, PERF console line)
  - id: readme-web
    resource: ../../../samples/HelloMesh.Web/README.md
    title: HelloMesh.Web README (how the Vite plugin finds the publish output, e2e test, troubleshooting)
  - id: e2e
    resource: ../../../samples/HelloMesh.Web/test/e2e.mjs
    title: Playwright end-to-end script (17 checks, READY and FRAME 120 timings, PERF line)
  - id: csproj
    resource: ../../../samples/HelloMesh/HelloMesh.Browser/HelloMesh.Browser.csproj
    title: HelloMesh.Browser.csproj (Microsoft.NET.Sdk.WebAssembly, no AOT properties set)
  - id: perf-note
    resource: ../dotnet-wasm/performance-and-aot.md
    title: Performance on .NET 10 browser-wasm (interpreter, jiterpreter, Mono AOT, size trade-offs)
  - id: props-note
    resource: ../dotnet-wasm/msbuild-property-reference.md
    title: MSBuild property and item reference for .NET 10 browser-wasm builds
  - id: sdk-browser-targets
    resource: /usr/local/share/dotnet/packs/Microsoft.NET.Runtime.WebAssembly.Sdk/10.0.13/Sdk/BrowserWasmApp.targets
    title: BrowserWasmApp.targets 10.0.13 (WasmEnableSIMD default, WasmStripILAfterAOT default, -msimd128)
  - id: sdk-common-targets
    resource: /usr/local/share/dotnet/packs/Microsoft.NET.Runtime.WebAssembly.Sdk/10.0.13/Sdk/WasmApp.Common.targets
    title: WasmApp.Common.targets 10.0.13 (mattr=simd AOT argument, IL strip task)
  - id: publish-interp
    resource: ../../../samples/HelloMesh.Web/test/out/aot/interp-publish.log
    title: Interpreter publish log (clean sample build, 11.83 s) - git-ignored, numbers inlined
  - id: publish-aot
    resource: ../../../samples/HelloMesh.Web/test/out/aot/aot-strip-publish.log
    title: AOT publish log (clean sample build, 55.67 s, 46 assemblies AOT'd) - git-ignored, numbers inlined
  - id: publish-restore
    resource: ../../../samples/HelloMesh.Web/test/out/aot/interp-restore-publish.log
    title: Interpreter re-publish after the AOT runs (12.67 s) - git-ignored, numbers inlined
  - id: sizes-interp
    resource: ../../../samples/HelloMesh.Web/test/out/aot/interp-sizes.txt
    title: _framework size report of the interpreter publish (file counts, raw/br/gz totals, largest files)
  - id: sizes-aot
    resource: ../../../samples/HelloMesh.Web/test/out/aot/aot-strip-sizes.txt
    title: _framework size report of the AOT publish
  - id: logs-dir
    resource: ../../../samples/HelloMesh.Web/test/out/aot/
    title: All e2e logs (interp-e2e-1..3, aot-e2e-1..2, restore-e2e, each with frame.png and console.log) and steady-state probe logs (interp-steady-1..3, aot-steady-1..2)
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T20:21:00Z }
status: draft
---

# Purpose

The HelloMesh browser build runs on the Mono interpreter (plus the jiterpreter). The research note predicts that Mono AOT ("LLVM-only + interpreter fallback") gives large CPU speed-ups at roughly double the payload and minutes of build time, and lists "AOT and relinked sizes for the PoC" and "interpreter vs jiterpreter vs AOT speed for BRUTAL-style code" as unverified.[^perf-note] This experiment measures both on the real sample: what a trimmed Release publish of `HelloMesh.Browser` costs and gains with `-p:RunAOTCompilation=true -p:WasmStripILAfterAOT=true`, and whether the 17-check e2e still passes.

Headline: the e2e passes 17/17 under AOT; the AOT publish took 55.7 s (interpreter 11.8 s); `_framework` grows 2.8x raw (6.57 to 18.38 MB) and 1.9x brotli (2.18 to 4.15 MB), almost all of it `dotnet.native.wasm` (1.52 to 13.33 MB); steady-state CPU per frame drops from about 0.5 ms to 0.2-0.3 ms and the 9-18 ms spikes of the first ~1,200 frames (about 20 s) disappear (max 1.2 ms). The game is still paced by the display (FRAME 120 at about 1.97 s after READY in both), so AOT buys headroom, not frame rate, for this sample.

# Setup

Machine and tools (all already installed; nothing was installed globally):

| Item | Value |
| --- | --- |
| Hardware / OS | Apple M4 Pro, macOS 27.0.1 (arm64) |
| .NET SDK | 10.0.100 (`global.json`, `rollForward: latestFeature`); workload `wasm-tools` manifest 10.0.113 (packs 10.0.13: Mono browser-wasm runtime pack, `Microsoft.NETCore.App.Runtime.AOT.osx-arm64.Cross.browser-wasm`, `Microsoft.NET.Runtime.MonoAOTCompiler.Task`, Emscripten 3.1.56) |
| Node / test stack | Node 24.18.0, Vite 8.3.4, playwright-core 1.64.1 |
| Browser | Playwright Chromium 156.0.8078.4 (`channel: 'chromium'`, new headless, Metal WebGPU adapter), viewport 1280x720, DPR 1 |
| Project | `samples/HelloMesh/HelloMesh.Browser` (`Microsoft.NET.Sdk.WebAssembly`, `RuntimeIdentifier=browser-wasm`, `InvariantGlobalization`, no AOT property set)[^csproj] |

Procedure. Before each of the three publishes the sample's own `bin/Release` and `obj/Release` were deleted (the referenced `src/*` projects kept their incremental state), because `dotnet publish` never deletes old fingerprinted files and the AOT and interpreter outputs go to the same folder (`bin/Release/net10.0/publish/wwwroot`), which the Vite plugin serves "newest wins".[^readme-web] The publish times are therefore "clean sample build, warm library projects". Sizes are of `publish/wwwroot/_framework` only (the three root-level JS glue files, 56,624 B raw together, are the same in both).

```sh
cd samples/HelloMesh.Web
rm -rf ../HelloMesh/HelloMesh.Browser/{bin,obj}/Release

# 1. interpreter baseline (what `npm run build:dotnet` does)
npm run build:dotnet                      # 11.83 s
node test/e2e.mjs --out test/out/aot/interp-e2e-1   # x3

# 2. AOT
rm -rf ../HelloMesh/HelloMesh.Browser/{bin,obj}/Release
dotnet publish ../HelloMesh/HelloMesh.Browser -c Release -p:RunAOTCompilation=true -p:WasmStripILAfterAOT=true   # 55.67 s
node test/e2e.mjs --out test/out/aot/aot-e2e-1      # x2

# 4. restore the default
rm -rf ../HelloMesh/HelloMesh.Browser/{bin,obj}/Release
npm run build:dotnet                      # 12.67 s
node test/e2e.mjs --out test/out/aot/restore-e2e
```

`npm run build:dotnet -- -p:RunAOTCompilation=true` is equivalent to step 2 (the script passes extra arguments through). `WasmStripILAfterAOT` already defaults to `true` in both SDK targets files, so spelling it out changes nothing.[^sdk-browser-targets][^sdk-common-targets]

Metrics. READY and FRAME 120 are the e2e's own timings (READY relative to `page.goto`, FRAME 120 relative to the READY line), served by the Vite dev server (loopback, uncompressed). The e2e prints only the first `PERF` line (frames 1-300, which includes start-up and the scripted drag and key presses), so a second, input-free probe was added as a scratch script outside the repo: it serves the same publish output, loads the page, clicks start and records the first eight `PERF` windows (2,400 frames) of the host's own `PERF avg/p95/max/interval/alloc/gc` line.[^readme] "Steady state" below means windows 2-8 of that probe.

```js
// scratch (not in the repo): steady.mjs <label> [windows]
const { chromium } = await import(web + '/node_modules/playwright-core/index.mjs');
const { createServer } = await import(web + '/node_modules/vite/dist/node/index.js');
const server = await createServer({ root: web, logLevel: 'warn', server: { port: 5197 } }); await server.listen();
const browser = await chromium.launch({ channel: 'chromium', headless: true });
const page = await (await browser.newContext({ viewport: { width: 1280, height: 720 }, deviceScaleFactor: 1 })).newPage();
page.on('console', (m) => lines.push({ t: Date.now() - t0, text: m.text() }));
await page.goto(server.resolvedUrls.local[0]); /* wait for READY */ await page.click('#start');
/* wait until `windows` lines starting with "PERF " were seen, print them, then window 2..N mean/median/worst */
```

# Results

All numbers are from this machine and session; the raw logs are git-ignored under `samples/HelloMesh.Web/test/out/aot/` and the key values are inlined here.[^logs-dir]

| | Interpreter (default) | Mono AOT (`RunAOTCompilation` + `WasmStripILAfterAOT`) | Ratio / delta |
| --- | --- | --- | --- |
| Publish wall-clock (clean sample build) | 11.83 s (re-publish later: 12.67 s)[^publish-interp][^publish-restore] | 55.67 s (`AOT'ing 46 assemblies`, slowest bitcode compile `aot-instances` 13.6 s, `System.Private.CoreLib` 7.6 s)[^publish-aot] | 4.7x |
| `_framework` files | 147 (49 files x raw, `.br`, `.gz`) | 147 | same |
| `_framework` raw total | 6,567,239 B (6.57 MB)[^sizes-interp] | 18,382,203 B (18.38 MB)[^sizes-aot] | 2.80x, +11.8 MB |
| `_framework` brotli total | 2,184,219 B (2.18 MB) | 4,152,763 B (4.15 MB) | 1.90x, +1.97 MB |
| `_framework` gzip total | 2,676,834 B (2.68 MB) | 5,910,859 B (5.91 MB) | 2.21x |
| `dotnet.native.wasm` raw / br | 1,523,737 / 476,742 B | 13,326,110 / 2,713,522 B | 8.7x / 5.7x |
| 45 assembly `.wasm` files raw / br | 4,575,409 / 1,602,867 B | 4,575,409 / 1,333,717 B | raw identical; br -17% |
| `_framework/*.js` (`dotnet.js`, `dotnet.native.js`, `dotnet.runtime.js`) raw / br | 468,093 / 104,610 B | 480,684 / 105,524 B | about +3% raw |
| READY (after navigation), e2e | 525, 527, 525 ms (restore run 536) | 502, 452 ms | about -4 to -14% |
| FRAME 120 (after READY), e2e | 1974, 1962, 1970 ms (restore run 1988) | 1982, 1980 ms | no change (display-paced: 120 x 16.67 ms) |
| PERF, e2e window 1 (frames 1-300, with input): avg / p95 / max | 0.75-0.80 / 1.5-1.9 / 11.9-14.5 ms (4 runs) | 0.43-0.44 / 0.9-1.0 / 3.5-3.8 ms (2 runs) | avg -45%, max -70% |
| PERF, probe window 1 (frames 1-300, no input): avg / p95 / max | 0.81-0.84 / 1.4-1.5 / 13.8-14.7 ms (3 runs) | 0.35-0.37 / 0.6 / 3.5-4.3 ms (2 runs) | avg about -57% |
| PERF, probe steady state (windows 2-8): avg / p95 | 0.51, 0.54, 0.52 / 0.77, 0.79, 0.74 ms (per run: mean of 7 windows) | 0.33, 0.23 / 0.50, 0.43 ms | avg 1.6-2.3x lower |
| PERF, probe steady state: max (median / worst of 7 windows) | 4.1, 4.5, 3.1 / 17.7, 17.2, 17.1 ms | 1.0, 1.0 / 1.2, 1.2 ms | spikes gone |
| `interval` / `gc` / `alloc` (steady) | 16.67 ms / 0 / 1-50 B per frame | 16.67 ms / 0 / 1 B per frame | same |
| e2e result | 17/17 (4 runs: 3 + after restore) | 17/17 (2 runs) | no regression |

Largest `_framework` files (raw / brotli, bytes; names without fingerprint):

| File | Interpreter | AOT |
| --- | --- | --- |
| `dotnet.native.wasm` | 1,523,737 / 476,742 | 13,326,110 / 2,713,522 |
| `System.Private.CoreLib.wasm` | 1,514,261 / 474,380 | 1,514,261 / 348,098 |
| `Brutal.Vulkan.wasm` (browser flavor) | 635,669 / 152,802 | 635,669 / 141,735 |
| `HelloMesh.Browser.wasm` | 542,485 / 362,533 | 542,485 / 355,062 |
| `Brutal.Vulkan.WebGpu.wasm` | 435,989 / 108,941 | 435,989 / 80,226 |
| `System.Text.Json.wasm` | 361,749 / 120,587 | 361,749 / 88,172 |
| `Brutal.Numerics.wasm` | 152,853 / 32,777 | 152,853 / 26,214 |

Per-window `PERF` lines of the probe (avg / p95 / max, ms): interpreter run 1: 0.81/1.4/14.5, 0.63/0.9/10.7, 0.59/0.9/9.8, 0.57/0.8/17.7, 0.44/0.7/1.3, 0.41/0.7/1.1, 0.44/0.7/4.1, 0.46/0.7/1.1. AOT run 1: 0.37/0.6/4.3, 0.35/0.5/1.2, 0.35/0.5/1.1, 0.34/0.5/1.0, 0.34/0.5/1.1, 0.32/0.5/1.0, 0.32/0.5/1.0, 0.31/0.5/1.0. AOT run 2: 0.35/0.6/3.5, 0.15/0.2/0.4, 0.14/0.4/1.0, 0.18/0.4/1.1, 0.29/0.5/0.9, 0.28/0.5/1.2, 0.28/0.5/1.0, 0.30/0.5/1.0.

Optional step 3 (`-p:WasmEnableSIMD=true`) was not run: it is the default already. In `BrowserWasmApp.targets`, `WasmEnableSIMD` defaults to `$(WasmEnableExceptionHandling)`, which defaults to `true` (so `-msimd128`, the SIMD runtime library and, with AOT, the `mattr=simd` AOT argument are on without any property).[^sdk-browser-targets][^sdk-common-targets][^props-note]

# Observations

- **The AOT publish worked first time**, with only the known `IL2104` Brutal.Gltf trim warning that the interpreter publish also prints. No fallback to `WasmStripILAfterAOT=false` was needed. It took under a minute (14 cores), not "minutes": 46 assemblies (the trimmed app, BRUTAL, and the BCL pieces it keeps) are compiled to LLVM bitcode, then to `.o` files in parallel (the single slowest unit is the generics image `aot-instances`, 13.6 s), then one `emcc` link and `wasm-opt`. Larger games with more IL will scale roughly with IL size.[^publish-aot]
- **All 17 e2e checks pass under AOT**, twice, including the pixel checks (GPU readback centre `[2,2,2,255]` vs clear colour `[26,51,77,255]`), the 200 px drag turning the camera exactly 80 degrees, ArrowUp zoom, `AUDIO play` and the running AudioContext. No `ERROR` line, no page error. Behaviour of the C# ICD, the `[JSImport]`/`[JSExport]` surface and `Brutal.Gltf`'s reflection-based JSON is unchanged by AOT.[^e2e]
- **Frame rate does not change; CPU headroom does.** `interval` stays at 16.6-16.9 ms and FRAME 120 arrives about 1.97 s after READY in both modes because the game is display-paced (120 frames x 16.67 ms). The measurable gains are the CPU time of the frame delegate: first window 0.81 to 0.36 ms, steady state about 0.52 to 0.23-0.33 ms. HelloMesh uses 2-5 percent of a 16.7 ms frame in either mode.
- **The interpreter's spikes are warm-up, and AOT removes them.** In the interpreter the first four 300-frame windows (about 20 s) contain frames of 9-18 ms and the average only settles to about 0.45 ms from window 5; with AOT the worst frame after window 1 is 1.2 ms in 4,200 frames (7 windows x 300 frames x 2 runs). This is consistent with the README's guess that the isolated slow frames are jiterpreter trace compilation, not GC (`gc=0` in all windows).[^readme]
- **The remaining 0.2-0.3 ms is probably not C#.** The `PERF` timer includes the submit's JS replay call, which AOT cannot speed up (UNVERIFIED: not profiled). AOT run 2 shows two regimes (0.14-0.18 ms in windows 2-4, then 0.28-0.30 ms) with no code change, so run-to-run machine state (GPU process, power) matters at this scale; treat the AOT steady-state avg as 0.2-0.35 ms.
- **READY is not slower despite a 13 MB native module.** READY came 4-14 percent earlier (452-502 ms vs 525-536 ms), plausibly because start-up code no longer runs in the interpreter (UNVERIFIED). This was measured on loopback from the uncompressed dev server, so the extra download of the AOT build is not included.
- **Size is almost entirely `dotnet.native.wasm`.** The AOT code of 46 assemblies, the interpreter and the runtime are linked into one module: 1.52 to 13.33 MB raw (8.7x) and 0.48 to 2.71 MB brotli (5.7x). Brotli turns a 2.8x raw growth into 1.9x, close to the research note's "roughly twice the size".[^perf-note]
- **`WasmStripILAfterAOT` shrinks only the compressed size, not the raw size.** The 45 assembly `.wasm` (Webcil) files have byte-identical raw sizes in both builds (4,575,409 B total) but the AOT'd bodies are zero-filled in place (zero bytes in `System.Private.CoreLib`: 376,638 to 659,022; `Brutal.Vulkan.WebGpu`: 120,217 to 195,931; `System.Text.Json`: 75,303 to 161,758), so brotli of the assemblies falls 1.60 to 1.33 MB (-17 percent; `System.Text.Json` -27 percent, `Brutal.Vulkan.WebGpu` -26 percent). `HelloMesh.Browser.wasm` barely compresses either way (355-363 KB br of 542 KB): it embeds the GLB and OGG resources.
- **AOT and interpreter outputs collide in one folder.** Both go to `bin/Release/net10.0/publish/wwwroot`, so whichever is published last is what the Vite dev server and `vite build` use; old fingerprinted files stay (the first interpreter publish after earlier work had up to three fingerprints per assembly). The restore step therefore cleaned `bin/Release` and `obj/Release`; the re-published interpreter build is byte-identical in raw size (6,567,239 B, 49 raw files) and passes 17/17 again. Whether a non-clean switch back from AOT is safe was not tested.

# Recommendation

- **Keep the interpreter as the default** for iteration, CI and for HelloMesh itself: publish is 12 s, `_framework` is 2.2 MB brotli, and the frame costs 0.4-0.9 ms of a 16.7 ms budget. Nothing in this sample needs AOT.
- **Use AOT for a game build when the C# per-frame cost matters**: a CPU-bound frame (many draws or objects through the Vulkan-on-WebGPU ICD, physics, animation, procedural geometry), low-end or mobile CPUs where the interpreter is several times slower than on an M4 Pro, or when the first ~20 s of 9-18 ms stutter from jiterpreter warm-up is unacceptable. For the tail latency alone, AOT's evidence here is strong (max 1.2 ms vs 17 ms); for average throughput it is 1.6-2.3x on this light workload and is expected to be larger for heavy math (UNVERIFIED, not measured).
- **The cost**: +11.8 MB raw / +1.97 MB brotli (+90 percent) on every cold load, so a hosted game needs `.br`/`.gz` negotiation (a `vite preview` feature of this repo's plugin; the e2e's dev server serves the raw files) and at about 10 Mbit/s the brotli delta alone is about 1.6 s of extra download (arithmetic: 1.97 MB x 8 / 10 Mbit/s); a 4.7x longer publish here (more on slower CI hosts and bigger games); the `wasm-tools` workload and a Release, trimmed, relinked build are mandatory; `WasmProfilers` DevTools markers do not work with AOT;[^perf-note] and the interpreter stays in the binary, so AOT'd code still falls back for exception-heavy paths.
- **How to ship it**: add it as an opt-in publish (`npm run build:dotnet -- -p:RunAOTCompilation=true`) for release builds, keep `WasmStripILAfterAOT` at its default (`true`; it costs nothing raw and saves 17 percent brotli on the assemblies), clean `bin/Release` and `obj/Release` when switching between modes, and keep the Debug/interpreter path for inner-loop work.

# Open questions

- How much of the remaining 0.2-0.3 ms per frame is C# vs the JS replay of the recorded command stream (needs a browser profile or a `performance.now()` split inside `BrowserHost.Tick`)?
- Does a heavier workload (many meshes, a physics step) show the larger AOT speed-ups the research note expects, and where does the interpreter exceed 16 ms per frame?
- Can profiled/partial AOT (`.mibc` via `AOTProfilePath`/`WasmAotProfilePath`) keep most of the benefit while cutting `dotnet.native.wasm` from 13 MB? (Open since the research note.)[^perf-note]
- What do `WasmEnableSIMD=false`, `EmccLinkOptimizationFlag=-Oz` and `WasmStripILAfterAOT=false` do to AOT size and speed? Not run: SIMD is already the default, and only the default combination was measured.
- Real network start-up: READY under throttled bandwidth with brotli (`npm run build && npm run preview`), and with a cold HTTP cache and a cold V8 code cache for the 13 MB module.
- Memory: wasm heap and resident size with the larger module were not measured.
- Chrome (`--chrome`), Edge and non-Apple-silicon machines were not tried; only Chromium 156 on an M4 Pro.
- Incremental AOT publish time after a one-line C# change (the numbers here are clean sample builds).

[^readme]: HelloMesh README (browser section, interpreter numbers, PERF console line)
[^readme-web]: HelloMesh.Web README (how the Vite plugin finds the publish output, e2e test, troubleshooting)
[^e2e]: Playwright end-to-end script (17 checks, READY and FRAME 120 timings, PERF line)
[^csproj]: HelloMesh.Browser.csproj (Microsoft.NET.Sdk.WebAssembly, no AOT properties set)
[^perf-note]: Performance on .NET 10 browser-wasm (interpreter, jiterpreter, Mono AOT, size trade-offs)
[^props-note]: MSBuild property and item reference for .NET 10 browser-wasm builds
[^sdk-browser-targets]: BrowserWasmApp.targets 10.0.13 (WasmEnableSIMD default, WasmStripILAfterAOT default, -msimd128)
[^sdk-common-targets]: WasmApp.Common.targets 10.0.13 (mattr=simd AOT argument, IL strip task)
[^sizes-interp]: _framework size report of the interpreter publish (file counts, raw/br/gz totals, largest files)
[^sizes-aot]: _framework size report of the AOT publish
[^publish-interp]: Interpreter publish log (clean sample build, 11.83 s)
[^publish-aot]: AOT publish log (clean sample build, 55.67 s, 46 assemblies AOT'd)
[^publish-restore]: Interpreter re-publish after the AOT runs (12.67 s)
[^logs-dir]: All e2e logs and steady-state probe logs under samples/HelloMesh.Web/test/out/aot/ (git-ignored)
