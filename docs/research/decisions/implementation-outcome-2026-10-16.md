---
type: Decision Record
title: Implementation outcome — Proposal A built end to end (2026-10-16)
description: What was actually built from Proposal A in one day, the measured results (desktop validation, browser e2e, frame cost, sizes), the contracts that changed during implementation, and what remains before this is a game-ready base.
tags: [implementation, outcome, proposal-a, icd, hellomesh, e2e, results]
sources:
  - id: plan
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/plans/01-proposal-a-managed-web-icd.md
    title: Proposal A implementation plan
  - id: arch
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/architecture.md
    title: Architecture and component contracts (normative)
  - id: icd-readme
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/src/Brutal.Vulkan.WebGpu/README.md
    title: Brutal.Vulkan.WebGpu README (implemented subset, opcodes, limits)
  - id: sample-readme
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/samples/HelloMesh/README.md
    title: HelloMesh sample README (desktop and browser)
  - id: web-readme
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/samples/HelloMesh.Web/README.md
    title: HelloMesh.Web README (Vite app, plugin, e2e)
  - id: profiles-readme
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/profiles/README.md
    title: Web Vulkan profiles README
  - id: review
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/review-2026-10-16.md
    title: Read-only code quality review, 2026-10-16
  - id: git
    resource: "scope: git history of the brutal-browser repository on 2026-10-16 (commits 518ee19..35cb849)"
    title: Commit history of the implementation day
generated: { by: claude-fable/5.1, at: 2026-10-16T23:30:00Z }
status: draft
---

# Outcome in one paragraph

Proposal A was implemented end to end on 2026-10-16.[^git] The `HelloMesh` sample, written once against BRUTAL's Vulkan API, renders the Khronos sunglasses model in the browser through the managed Vulkan-on-WebGPU ICD, with orbit/pan/zoom input and OGG playback, and the Playwright end-to-end test passes all 17 checks in headless Chromium 156 and Google Chrome 155; the same code runs on desktop MoltenVK clean under the validation layer and under the `VP_BRUTAL_web_poc_2026` profile.[^sample-readme][^profiles-readme] The browser frame matches the desktop frame (outline overlap 0.994, identical sample pixels) at a steady 60 fps with under 1 ms of CPU per frame in interpreter mode.[^sample-readme]

# What was built

| Component | Location | Size of the evidence |
| --- | --- | --- |
| Bindings browser flavor (pluggable loader + managed function pointers) | `src/Brutal.Vulkan.Browser`, `src/Brutal.Vulkan.Abstractions.Browser`, `patches/brutal` | 49 tests; API identical to NuGet except the pointer convention |
| Web ICD | `src/Brutal.Vulkan.WebGpu` | 227 implemented commands, 582 typed stubs, 290 tests, browser smoke test with GPU readback[^icd-readme] |
| Browser host and GLFW drop-in | `src/Brutal.Browser`, `src/Brutal.Glfw.Browser` | 66 tests; Playwright smoke test with 38 checks |
| Shader pipeline | `tools/` | fixtures + Tint validation in headless Chromium |
| Sample game (desktop + browser) | `samples/HelloMesh` | validation-clean on MoltenVK; browser e2e 17/17 |
| Vite 8 app and plugin | `samples/HelloMesh.Web` | dev/build/preview; e2e harness |
| Vulkan profile | `profiles/VP_BRUTAL_web.json`, `scripts/validate-web-profile.sh` | schema + layer validated; ICD/JSON sync tests |

# Measured results

| Metric | Value |
| --- | --- |
| Browser e2e (Chromium 156 / Chrome 155) | 17/17 checks both |
| READY after navigation / FRAME 120 after READY | ≈ 0.6 s / ≈ 2.0 s |
| CPU per frame (interpreter, idle / dragging) | avg 0.35–0.9 ms, p95 ≤ 1.6 ms, occasional single spikes ≤ 16 ms |
| Per-frame managed allocation | ≈ 0 B idle, ≈ 220 B while dragging (console strings), 0 GCs |
| Frame match vs desktop | outline overlap 0.994; corner 26,51,77; lenses 2,2,2 |
| Desktop validation (validation layer, 240 frames) | 0 messages |
| Desktop under the web profile (poc / game / game_max) | 0 VUIDs each |
| ICD assembly | 426 KB (106 KB Brotli) |

# Contract changes made during implementation

- Advertised API version is **1.3** (non-conformant): BRUTAL uses the 1.3 core entry points and the Khronos profiles layer resolves them only at 1.3; the ICD and the profile JSON were reconciled and a sync test keeps them aligned.[^profiles-readme]
- `maxBoundDescriptorSets` is 3 (group 3 holds emulated push constants) and `maxPushConstantsSize` is 128.[^icd-readme]
- Bind group layouts are created lazily from the first pipeline that uses them (WGSL reflection), not at `vkCreateDescriptorSetLayout`; `writeTexture` and readbacks are command-stream records rather than separate bridge calls.[^icd-readme]
- Libraries deliver their JavaScript through Razor-SDK static web assets (`StaticWebAssetBasePath=/`), so `dotnet publish` places them next to `_framework/`; the Vite plugin resolves publish, build and manifest outputs.[^web-readme]
- `Brutal.Gltf` works in the browser with `JsonSerializerIsReflectionEnabledByDefault=true` and a trimmer root; the managed GLB reader fallback was not needed.[^sample-readme]

# What remains (from the review and the plan)

- A developer guide (where game code goes, the supported Vulkan subset, unavailable BRUTAL libraries, performance rules) and small contract corrections in `docs/architecture.md`.[^review]
- ICD hardening items: lazy once-logging, push-constant ring overflow, `VK_ERROR_DEVICE_LOST`, dynamic depth bias, blits.[^review]
- Game-tier features (queries, timeline semaphores, blits, extended dynamic state) and an async pipeline path, per plan Phase 7.[^plan]
- AOT measurement (plan 7.1) — see the exp-07 report when present.

[^git]: Commit history of the implementation day
[^plan]: Proposal A implementation plan
[^arch]: Architecture and component contracts (normative)
[^icd-readme]: Brutal.Vulkan.WebGpu README (implemented subset, opcodes, limits)
[^sample-readme]: HelloMesh sample README (desktop and browser)
[^web-readme]: HelloMesh.Web README (Vite app, plugin, e2e)
[^profiles-readme]: Web Vulkan profiles README
[^review]: Read-only code quality review, 2026-10-16
