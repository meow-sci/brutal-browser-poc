# BRUTAL in the browser — implementation plans

Written 2026-10-16 from the research bundle in [`docs/research`](../docs/research/index.md). Start with the
[architecture decision record](../docs/research/decisions/architecture-options.md); it explains why the
proposals below are ranked the way they are and lists the evidence (experiments `exp-01` … `exp-06` under
[`projects/`](../projects)).

| Plan | What it builds | Status |
| --- | --- | --- |
| [01 — Proposal A: managed Vulkan-on-WebGPU ICD](01-proposal-a-managed-web-icd.md) | The recommended path: the game keeps using `Brutal.Vulkan`; a C# ICD (`Brutal.Vulkan.WebGpu`) implements a documented Vulkan subset on WebGPU; `Brutal.Browser` + a `Brutal.Glfw` drop-in provide the host, input and audio; Vite 8 packages it. Seven phases with tasks and acceptance criteria. | **Implemented** (Phases 0–6 and part of 7 on 2026-10-16; see `docs/research/decisions/implementation-outcome-2026-10-16.md`) |
| [02 — Alternative proposals](02-alternative-proposals.md) | B: native (Emscripten) ICD; C: WebGPU-native RHI with Vulkan and WebGPU back ends; plus the variant A2 (UnmanagedCallersOnly ICD) and when to switch to it. | Fallbacks / future |

## Recommendation in brief

Build **Proposal A**. Every risky link in its chain has already been exercised in this repository:

- BRUTAL's managed packages run on .NET 10 browser-wasm (exp-01).
- `Brutal.Vulkan` rebuilt from source with a 125-line, API-identical loader patch runs unchanged on MoltenVK and boots in a headless browser with a C# `vkGetInstanceProcAddr` (exp-04).
- C# drives WebGPU through `[JSImport]` with integer handles and a replayed command stream; a pixel-checked triangle renders in headless Edge/Chromium with no flags (exp-03).
- Vite 8.3.4 serves the unbundled .NET output through a ~160-line plugin (exp-05).
- Shaders written as Vulkan GLSL cross-compile to WGSL at build time with naga, including the split of combined samplers and push-constant emulation (exp-02).
- The managed function-pointer flavor of the bindings (exp-06) removes the dependency on the `wasm-tools` native relink for the ICD call path: 52/52 checks on desktop and in headless Chromium/Edge, 31–41 ns per Vulkan call in the interpreter, exceptions propagate, trimming is safe.

## What the user needs to provide

1. `dotnet workload install wasm-tools` (net10). Not needed for Phases 1–6 of Proposal A in interpreter mode, but required for AOT (Phase 7) and for the A2 variant. The SDK also recommends it for publish.
2. A freely licensed glTF model for the sample (Khronos glTF Sample Assets, CC-BY/CC0). KSA assets remain reference-only.
3. Nothing else: Node 24, Vite 8.3.4, `naga-wasm` (npm), glslc/spirv-opt (Vulkan SDK) and headless Chromium builds are already available locally.

## Reading order for a new session

1. `docs/research/index.md` → overview → decisions.
2. `docs/research/experiments/*` for what is proven and the exact commands.
3. This folder's plan 01, then the research notes it links from each task.
