---
type: Decision Record
title: Architecture options for BRUTAL in the browser, and the recommended path
description: Compares the viable ways to run BRUTAL-based C# game code in a browser (managed Vulkan-on-WebGPU ICD with managed or unmanaged function pointers, a native Emscripten ICD, a WebGPU-native RHI, software Vulkan) against the evidence gathered in the research notes and experiments, and records the recommended layered architecture, its contracts, risks and prerequisites.
tags: [decision, architecture, vulkan-on-webgpu, icd, dotnet-wasm, brutal, recommendation]
sources:
  - id: brief
    resource: /overview/project-brief-and-constraints.md
    title: Project brief and constraints
  - id: fnptr
    resource: /dotnet-wasm/function-pointers-and-native-callbacks.md
    title: Function pointers, UnmanagedCallersOnly and native resolution on .NET 10 browser-wasm
  - id: native-linking
    resource: /dotnet-wasm/native-linking-and-emscripten.md
    title: Native linking and Emscripten in .NET 10 wasm builds
  - id: nativeaot
    resource: /dotnet-wasm/nativeaot-llvm-and-alternatives.md
    title: NativeAOT-LLVM and alternatives
  - id: perf-aot
    resource: /dotnet-wasm/performance-and-aot.md
    title: Performance and AOT on .NET wasm
  - id: exp01
    resource: /experiments/exp-01-dotnet-wasm-smoke.md
    title: exp-01 .NET 10 browser-wasm smoke test for BRUTAL
  - id: exp03
    resource: /experiments/exp-03-webgpu-jsimport.md
    title: exp-03 C# → WebGPU through JSImport
  - id: exp04
    resource: /experiments/exp-04-brutal-vulkan-source-build.md
    title: exp-04 Brutal.Vulkan rebuilt from source with a pluggable loader
  - id: exp06
    resource: /experiments/exp-06-managed-fnptr-flavor.md
    title: exp-06 managed function-pointer flavor of Brutal.Vulkan
  - id: prior-art
    resource: /webgpu/prior-art-and-related-layers.md
    title: Prior art and related layers
  - id: profile
    resource: /webgpu/web-vulkan-profile-proposal.md
    title: Web Vulkan profile proposal
  - id: icd-surface
    resource: /webgpu/brutal-abstractions-icd-surface.md
    title: BRUTAL Abstractions ICD surface
  - id: sync
    resource: /webgpu/synchronization-and-async-shape.md
    title: Synchronization and the asynchronous shape of WebGPU
  - id: memory
    resource: /webgpu/memory-model-and-mapping-emulation.md
    title: Memory model and mapping emulation
  - id: gaps
    resource: /webgpu/semantic-gaps-and-emulation.md
    title: Semantic gaps and emulation strategies
  - id: batching
    resource: /webgpu/performance-and-batching-design.md
    title: Performance and batching design
  - id: webgpu-status
    resource: /webgpu/webgpu-status-and-browser-support-2026.md
    title: WebGPU status and browser support 2026
  - id: portability
    resource: /brutal/library-map-and-portability-matrix.md
    title: BRUTAL library map and portability matrix
  - id: loader-contract
    resource: /brutal/vulkan-loader-and-proc-addr-contract.md
    title: Vulkan loader and proc-addr contract
  - id: build-from-source
    resource: /brutal/building-from-source.md
    title: Building BRUTAL from source
  - id: hello-mesh
    resource: /brutal/desktop-hello-mesh-recipe.md
    title: Desktop hello-mesh recipe
  - id: io-rec
    resource: /io-audio/recommendation-io-audio.md
    title: Windowing, input, main loop and audio recommendation
  - id: vite
    resource: /web-packaging/serving-dotnet-wasm-output-with-vite.md
    title: Serving .NET 10 wasm output with Vite 8
  - id: headless
    resource: /web-packaging/headless-webgpu-testing.md
    title: Headless WebGPU testing
  - id: shaders-rec
    resource: /shaders/build-time-vs-runtime-cross-compilation.md
    title: Build-time vs runtime shader cross-compilation (shader track)
  - id: wgsl-conv
    resource: /shaders/wgsl-conventions-for-vulkan-layer.md
    title: WGSL conventions for the Vulkan layer
generated: { by: claude-fable/5.1, at: 2026-10-16T21:30:00Z }
status: draft
---

# Decision in one paragraph

Build the browser target as a **managed Vulkan ICD on top of WebGPU**: the game keeps writing C# against `Brutal.Vulkan` and `Brutal.Vulkan.Abstractions`; a browser build of those two libraries (compiled from the open-source tree with a small, API-identical "pluggable loader" patch and, by default, managed function pointers) hands every Vulkan entry point to a C# library, `Brutal.Vulkan.WebGpu`, which implements a documented Vulkan subset (`VP_BRUTAL_web_poc_2026`) by recording commands and replaying them through a batched `[JSImport]` bridge onto the browser's WebGPU device. Windowing, input, the frame loop and audio come from a managed `Brutal.Browser` host plus a source-compatible `Brutal.Glfw` drop-in; shaders are authored for Vulkan (GLSL or Slang → SPIR-V) and cross-compiled to WGSL at build time with a runtime fallback; Vite 8 serves the unbundled .NET output through a small plugin. Everything in this paragraph has been exercised by an experiment in this repository.[^exp01][^exp03][^exp04][^exp06]

# Decision drivers

1. The brief: open-source BRUTAL only, WASM, WebGPU, the game author writes C# against BRUTAL (specifically its Vulkan bindings), minimal work but a viable path for a full game, Vite 8.[^brief]
2. BRUTAL's binding shape: every Vulkan command is a function pointer fetched from `vkGetInstanceProcAddr`/`vkGetDeviceProcAddr` (110 instance-level and 697 device-level lookups), structs use explicit 64-bit layouts, handles are `nint`, and only four global commands are P/Invokes.[^loader-contract][^exp04]
3. .NET 10 browser-wasm facts: no `dlopen`; `&UnmanagedCallersOnlyMethod` is null without the `wasm-tools` native relink; interpreted `calli` of unmanaged pointers only works for pointer-shaped signatures or 52 prebuilt ones; 64-bit enums by value are truncated on that path; AOT also needs `wasm-tools`; single-threaded by default.[^fnptr][^exp01]
4. WebGPU is asynchronous where Vulkan is synchronous (adapter/device, buffer mapping, pipeline compilation, work completion) and has no explicit memory, barriers, push constants (except 64-byte immediates in Chrome 150+), bindless, BDA or geometry/tessellation.[^sync][^gaps][^webgpu-status]
5. Nobody has shipped Vulkan graphics in a browser through the Vulkan API; the closest prior art (nikki, VkWebGPU-ICD, godotwebgpu) is compute-only, native-only, or maps an engine's own RHI. The recorded-command/replay design, CPU-side fences and split samplers are common to all of them.[^prior-art]

# Options considered

| # | Option | How the game's Vulkan calls reach WebGPU | Workload needed | Evidence | Verdict |
| --- | --- | --- | --- | --- | --- |
| A1 | **Managed ICD, managed function pointers** (recommended default) | Source-built `Brutal.Vulkan`/`Abstractions` with `delegate* unmanaged[Cdecl]<…>` rewritten to managed `delegate*<…>`; ICD = plain static C# methods; pluggable loader via `AppContext` key | none (interpreter today; AOT later) | exp-06: 52/52 checks pass on desktop and in headless Chromium/Edge without `wasm-tools`; 31–41 ns per BRUTAL wrapper call in the browser interpreter (4–5× cheaper than one `[JSImport]`, 5–6× cheaper than JS trampolines); 8-, 10- and 15-argument calls, 64-bit enums, floats and 64-bit returns arrive intact; ICD exceptions propagate to the caller's `try/catch`; trimming keeps every pointer target[^exp06][^exp01][^fnptr] | **Primary path.** Cheapest call, runtime-agnostic IL, builds today. Costs a mechanical transform (2,441 replacements in `Functions.gen.cs`, 1 in Abstractions, 3 `[UnmanagedCallersOnly]` attributes stripped) applied at build time, and `Abstractions` must be source-built with it (the NuGet DLL fails with `MissingFieldException` on `PushConstants`). |
| A2 | **Managed ICD, UnmanagedCallersOnly** | Stock binding shape; ICD methods are `[UnmanagedCallersOnly]`; `vkGet*ProcAddr` returns `&Method` | `wasm-tools` (relink) + ~29 "anchor" signatures | Pointers are null without relink (exp-01/exp-04); with relink the build emits a thunk per UCO method; 55 wrappers need anchors; 4 wrappers pass 64-bit enums unsafely[^fnptr][^exp04] | **Binary-compatible alternative** once `wasm-tools` is installed; keep the ICD source shape-neutral so both A1 and A2 can be built. |
| B | **Native ICD** (C/C++/Rust compiled with Emscripten, `webgpu.h` via emdawnwebgpu or `-sUSE_WEBGPU`) | `NativeFileReference` named `vulkan` so the four `[LibraryImport("vulkan")]` bind statically; function pointers are wasm table indices | `wasm-tools`; Emscripten pinned to 3.1.56 while emdawnwebgpu wants ≥ 4.0.10 (Uno ships a patched copy) | ABI mismatch: BRUTAL structs have 64-bit layouts and 32-bit `nint` handles, C wasm32 expects 32-bit pointers and 64-bit non-dispatchable handles; two languages; Uno reports hangs when WebGPU events are pumped from a managed stack[^native-linking][^fnptr] | Not for the PoC. Revisit only if C# command encoding proves too slow under AOT. |
| C | **WebGPU-native RHI** (`Brutal.WebGpu` binding + a C# render-hardware interface with Vulkan and WebGPU back ends) | The game stops calling Vulkan and targets the RHI | none for the browser side | Conventional (bgfx/wgpu/sokol pattern); but existing BRUTAL renderers (e.g. KSA's) are Vulkan-specific and would be rewritten; does not satisfy "leverage the BRUTAL Vulkan bindings"[^brief] | Not chosen. The ICD's JS glue is effectively the seed of a `Brutal.WebGpu` package if BRUTAL ever wants one. |
| D | **Software Vulkan in wasm** (Lavapipe/SwiftShader compiled to wasm) | Real Vulkan driver in the page | `wasm-tools` | Prior art reaches compute only; no GPU acceleration; tens of MB[^prior-art] | Rejected. |
| E | **NativeAOT-LLVM** instead of Mono | n/a (runtime choice) | experimental feed; no macOS arm64 host package | Experimental, gaps in JS interop; Microsoft keeps Mono for wasm through .NET 11[^nativeaot] | Rejected for now; revisit when it ships. |

Runtime mode within A: Mono interpreter (plus jiterpreter) for the inner loop today; Mono AOT (`RunAOTCompilation=true`, needs `wasm-tools`) for performance runs and for a real game.[^perf-aot]

# Evidence that the recommended path works

| Claim | Status | Where |
| --- | --- | --- |
| Pure-managed BRUTAL packages (Core, Numerics, Maths, Memory, Collections, Strings, Logging, Vulkan types) load and run in browser-wasm | **Proven** | exp-01 T01–T05b[^exp01] |
| `Brutal.Vulkan` can be rebuilt from source with a 125-line patch that is API-identical (0 differences over 24,614 signatures), builds with 0 warnings, and still drives MoltenVK on desktop | **Proven** | exp-04[^exp04] |
| The unmodified NuGet `Brutal.Vulkan.Abstractions` binds to the source-built `Brutal.Vulkan` (not strong-named, AssemblyVersion 1.0.0.0) | **Proven** (desktop and browser) | exp-04, building-from-source[^exp04][^build-from-source] |
| BRUTAL's `Instance`/`Device` construction tolerates null entries for unimplemented commands (98/110 and 689/697 were null) but calling one is fatal | **Proven** | exp-04[^exp04] |
| A C# `vkGetInstanceProcAddr` can drive the full instance → device → queue → buffer flow in a real headless browser | **Proven** via JS trampolines (exp-04) and via managed `delegate*` with plain static C# methods (exp-06: 52/52, device creation with 697 lookups ≈ 3 ms warm) | exp-04, exp-06[^exp04][^exp06] |
| C# can drive WebGPU through `[JSImport]` with integer handles, wasm-heap pointer reads and a replayed binary command stream; a spinning triangle passes a pixel check in headless Edge/Chromium with no flags | **Proven** | exp-03[^exp03] |
| Batching 1000 draws into one replay call is 2.6× faster than per-call interop (0.228 ms vs 0.60 ms per frame, interpreter) | **Measured** | exp-03[^exp03] |
| Sync `createRenderPipeline` serialises GPU-process compilation (~6.5 ms each cold); async creation has ~7.6× the throughput | **Measured** | exp-03[^exp03] |
| Headless WebGPU on this Mac: Chrome for Testing 151 and Edge 155 expose the Metal adapter with no flags | **Proven** | exp-03, headless testing note[^exp03][^headless] |
| Vite 8.3.4 serves the unbundled `_framework/` through a ~160-line plugin in dev, build and preview, with reload on .NET rebuild | **Proven** | exp-05[^vite] |
| Ogg Vorbis asset decodes with `decodeAudioData` in Edge 155 | **Proven** (Chromium); Safari unverified | io-audio recommendation[^io-rec] |
| The PoC Vulkan subset is implementable: every needed command rates trivial/easy/moderate except push constants, mapping and waits, which have documented emulations | **Analysed** | ICD surface, gaps[^icd-surface][^gaps] |

# Recommended architecture

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ Game code (C#)  —  unchanged BRUTAL API usage                                │
│   Brutal.Vulkan / Brutal.Vulkan.Abstractions / Numerics / Memory / Strings…  │
├──────────────────────────────────────────────────────────────────────────────┤
│ Brutal.Vulkan + Abstractions (browser flavor, built from the open-source     │
│ tree):  pluggable loader (AppContext key)  +  managed function pointers      │
├──────────────────────────────────────────────────────────────────────────────┤
│ Brutal.Vulkan.WebGpu  (the "web ICD", C#)                                    │
│   • implements VP_BRUTAL_web_poc_2026                                        │
│   • object tables (handles are indices), command recording, replay on submit │
│   • host-visible memory = wasm-heap shadow; flush → writeBuffer records      │
│   • SPIR-V → WGSL via build-time manifest (+ naga-wasm fallback)             │
│   • canvas surface + swapchain = getCurrentTexture per frame                 │
├──────────────────────────────────────────────────────────────────────────────┤
│ [JSImport] bridge  ──  webgpu-glue.js (handle table, replayCommandStream,    │
│ async operations stay in JS: requestDevice, mapAsync, onSubmittedWorkDone)   │
├──────────────────────────────────────────────────────────────────────────────┤
│ Brutal.Browser (host, canvas, rAF tick, input queue, Web Audio)              │
│ Brutal.Glfw (browser flavor: source-compatible Brutal.GlfwApi drop-in)       │
├──────────────────────────────────────────────────────────────────────────────┤
│ Vite 8 app (TypeScript): serves _framework/, hot reload, dev server, build   │
└──────────────────────────────────────────────────────────────────────────────┘
```

Component names used across the plan:

| Name | Role | Notes |
| --- | --- | --- |
| `Brutal.Vulkan` (browser flavor) | the binding, source-built with patches | exp-04 patch set; `BrutalManagedFnptr=true` adds the managed-pointer transform (exp-06) |
| `Brutal.Vulkan.Abstractions` (browser flavor) | unchanged source, rebuilt for the flavor | required only for the managed-pointer flavor; the NuGet DLL works with A2 |
| `Brutal.Vulkan.WebGpu` | the ICD | C#; depends only on the binding types and the bridge; testable on desktop with a fake bridge |
| `Brutal.Browser` | host runtime for the browser | canvas, DPR, rAF `Tick`, input queue, `Now`, Web Audio engine, NVorbis fallback |
| `Brutal.Glfw` (browser flavor) | source-compatible `Brutal.GlfwApi` subset | lets GLFW-shaped game code and the ImGui GLFW backend compile unchanged[^io-rec] |
| `VP_BRUTAL_web_poc_2026` / `VP_BRUTAL_web_game_2026` | Vulkan Profiles JSON | validate desktop builds against the web subset with `VK_LAYER_KHRONOS_profiles`[^profile] |
| shader build step | GLSL/Slang → SPIR-V + WGSL + manifest | per the shader track's recommendation[^shaders-rec][^wgsl-conv] |
| Vite plugin | serve/copy `_framework/`, reload on rebuild | exp-05 design[^vite] |

# Contracts the layers rely on

- **Loader**: `AppContext.SetData("BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR", (nint)fnptr)` before the first touch of `Brutal.VulkanApi.Vulkan`; the four global commands resolve through `GIPA(NULL, name)`; every name BRUTAL requests gets either a correct-signature implementation/stub or null (null is safe until called).[^loader-contract][^exp04]
- **ICD surface**: the ~105-command PoC set and the conventions of the web profile (separate samplers, ≤ 4 sets, 256-byte dynamic offsets, 4-byte sizes/offsets, sample counts 1/4, single subpass or dynamic rendering, `STORE` for anything read later, return to the browser after present).[^icd-surface][^profile]
- **Memory and sync**: host-visible allocations are wasm-heap shadows flushed at unmap/flush/submit; upload-only fences signal at submit; readback fences never block (poll across frames); pipeline creation is async behind a "not ready" state.[^memory][^sync]
- **Interop**: integer handles, pointer + length for bulk data read through `localHeapViewU8()` fetched on every call, JSON only for rare descriptor-heavy calls, one `replayCommandStream` per `vkQueueSubmit`.[^exp03][^batching]
- **Shaders**: `set`→`@group`, `binding`→`@binding`, Y-flip handled by the translator's coordinate adjustment (no front-face inversion), push constants ≤ 64 B via immediates where available or a uniform ring at a reserved group otherwise.[^wgsl-conv]
- **Host**: `dotnet.create()` + `getAssemblyExports` (never `dotnet.run()`), rAF-driven `[JSExport] Tick`, `WasmEnableHotReload=false`, `_framework/` served unbundled.[^io-rec][^vite]

# Risks and mitigations

| Risk | Likelihood / impact | Mitigation |
| --- | --- | --- |
| Mixing the managed flavor with NuGet-built dependents (e.g. a prebuilt `Brutal.Vulkan.Abstractions` or `Brutal.Vulkan.Vma`, whose native VMA cannot call managed pointers) | medium / medium | build dependents from source under the same flavor; exclude `Brutal.Vulkan.Vma` from browser builds (use the `DeviceEx` allocator); a build check asserts no NuGet `Brutal.Vulkan*.dll` ships |
| Calling an unimplemented (null) table entry is fatal on both hosts | certain if unhandled / high | generate typed throwing stubs for all 814 commands so an unimplemented call is a named, catchable exception (exp-06 recommendation)[^exp06] |
| Interpreter too slow for a real game | high / high (not for the PoC) | AOT via `wasm-tools`; keep the hot path (command encoding) allocation-free and span-based; measure early |
| Mismatch between Vulkan's blocking calls and the browser's event loop (`vkWaitForFences`, `vkDeviceWaitIdle`, nested frame pumps) | medium / medium | profile conventions + `WebProfileLint`; async init with `NextFrame()`; document for game ports |
| Shader features outside WGSL (push constants > 64 B, combined samplers, bindless) | medium / medium | build-time reflection checks; profile tiers; emulations documented in the shader track |
| `Brutal.Logging` console provider and `Brutal.Concurrency` thread pool break single-threaded wasm | certain / low | browser `ILoggerProvider`; avoid `Brutal.Concurrency` in the PoC, propose an inline job mode upstream[^portability] |
| Upstream BRUTAL rejects loader/generator patches | medium / low | patches are additive and API-identical; carry them as a patch set applied at build time to a pinned commit |
| Licence obligations when distributing a BRUTAL derivative | certain / low | ship NOTICE.txt, credits, logo splash; confirm with Ahwoo before publishing[^io-rec] |
| Safari/Firefox differences (WebGPU features, Ogg decode) | medium / low | feature detection; NVorbis fallback; test matrix in CI |

# Prerequisites to request from the user

1. Install the net10 `wasm-tools` workload (`dotnet workload install wasm-tools`): required for AOT and for the A2 flavor, and recommended by the SDK even for interpreter publishes.
2. Optional: Emscripten and `naga-cli`/`tint` binaries are *not* required (naga runs from the project-local `naga-wasm` npm package).
3. A freely licensed glTF model for the PoC (e.g. a Khronos glTF sample asset) — KSA content stays reference-only.

# What makes this path "BRUTAL front and centre"

- The game author's code is BRUTAL Vulkan code; the browser becomes a Vulkan platform for BRUTAL rather than a separate renderer.
- The web profile gives BRUTAL developers a desktop-side contract (validation layer + profiles layer) for web compatibility before they ever open a browser.[^profile]
- The ICD is ordinary C# — debuggable with .NET tooling (exceptions thrown inside it reach the game's `try/catch`), portable across Mono interpreter, Mono AOT and a future CoreCLR-on-wasm, and hostable on desktop over a native WebGPU implementation for headless CI.
- The loader hook and the generator flag are small, upstreamable additions that make browser support a first-class BRUTAL target.

# Open questions

- How the managed flavor behaves under `wasm-tools` AOT (function pointers are represented differently there) and whether `Brutal.ImGui.Backends` works against it at run time (0 direct `.Invoke(` sites, untested).
- With `wasm-tools` installed: does A2 work end to end, and how large/fast is an AOT build of the PoC?
- Firefox and Safari support for WebGPU immediates and `texture-component-swizzle`; `getPreferredCanvasFormat` per browser/OS.
- Whether Ahwoo/RocketWerkz would accept the loader hook, the generator flag and a browser host package upstream.
- **Resolved 2026-10-16 — advertised `apiVersion` is 1.3, non-conformant** (ICD: `Profile.ApiVersion`, `vkEnumerateInstanceVersion`, `VkPhysicalDeviceProperties`; `profiles/VP_BRUTAL_web.json`: all three profiles 1.3.0, kept in sync with the ICD by `ProfileJsonSyncTests`). Reasons: BRUTAL code uses the Vulkan 1.3 core entry points (`vkCmdBeginRendering`, `vkCmdPipelineBarrier2`, ...), so they must resolve as core names; the Khronos profiles layer (and the validation layer above it) resolves core names only when the simulated device is 1.3, while under a 1.2 profile `vkGetDeviceProcAddr` returns NULL and the desktop sample crashes; and `samples/HelloMesh/HelloMesh.Desktop` passes `scripts/validate-web-profile.sh` with zero VUIDs under the 1.3 profiles. The device stays honest about what it lacks (`bufferDeviceAddress`, descriptor indexing, `privateData`, inline uniform blocks, ... reported false, conformance version 0.0.0.0), which supersedes the 1.2 recommendation of the profile proposal. SPIR-V is unaffected: the PoC compiles shaders at build time with `--target-env=vulkan1.1` (naga rejects `OpCopyLogical` from SPIR-V ≥ 1.4), which matters only for games that compile GLSL at run time.

[^brief]: Project brief and constraints
[^fnptr]: Function pointers, UnmanagedCallersOnly and native resolution on .NET 10 browser-wasm
[^native-linking]: Native linking and Emscripten in .NET 10 wasm builds
[^nativeaot]: NativeAOT-LLVM and alternatives
[^perf-aot]: Performance and AOT on .NET wasm
[^exp01]: exp-01 .NET 10 browser-wasm smoke test for BRUTAL
[^exp03]: exp-03 C# → WebGPU through JSImport
[^exp04]: exp-04 Brutal.Vulkan rebuilt from source with a pluggable loader
[^exp06]: exp-06 managed function-pointer flavor of Brutal.Vulkan
[^prior-art]: Prior art and related layers
[^profile]: Web Vulkan profile proposal
[^icd-surface]: BRUTAL Abstractions ICD surface
[^sync]: Synchronization and the asynchronous shape of WebGPU
[^memory]: Memory model and mapping emulation
[^gaps]: Semantic gaps and emulation strategies
[^batching]: Performance and batching design
[^webgpu-status]: WebGPU status and browser support 2026
[^portability]: BRUTAL library map and portability matrix
[^loader-contract]: Vulkan loader and proc-addr contract
[^build-from-source]: Building BRUTAL from source
[^io-rec]: Windowing, input, main loop and audio recommendation
[^vite]: Serving .NET 10 wasm output with Vite 8
[^headless]: Headless WebGPU testing
[^shaders-rec]: Build-time vs runtime shader cross-compilation (shader track)
[^wgsl-conv]: WGSL conventions for the Vulkan layer
