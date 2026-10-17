---
type: Technology Assessment
title: Prior art — Vulkan-on-WebGPU projects and related translation layers (as of October 2026)
description: Survey of existing Vulkan-on-WebGPU implementations (nikki, VkWebGPU-ICD), the Godot WebGPU RenderingDevice fork, software Vulkan in wasm, the design references MoltenVK, KosmicKrisp, Dozen, gfx-portability, ANGLE's WebGPU backend, Zink and Dawn/wgpu's Vulkan backends, the .NET/engine WebGPU ecosystem, the Vulkan Profiles precedent, and the lessons for a BRUTAL C# ICD.
tags: [prior-art, vulkan, webgpu, moltenvk, angle, zink, dawn, wgpu, vulkan-profiles, dotnet]
sources:
  - id: nikki
    resource: https://github.com/nihui/nikki
    title: nihui/nikki — compute-only Vulkan subset over WebGPU (C++, created 2026-08-03)
  - id: nikki-profile
    resource: https://raw.githubusercontent.com/nihui/nikki/main/docs/vulkan-profile.md
    title: nikki docs/vulkan-profile.md ("Restricted API compute profile")
  - id: vkwebgpu-icd
    resource: https://github.com/Dankular/VkWebGPU-ICD
    title: Dankular/VkWebGPU-ICD — Vulkan ICD over wgpu (Rust, created 2026-02-17)
  - id: godotwebgpu
    resource: https://github.com/dwalter/godotwebgpu
    title: dwalter/godotwebgpu — Godot 4.6 WebGPU RenderingDeviceDriver fork
  - id: godotcon-2026
    resource: https://talks.godotengine.org/godotcon-boston-2026/talk/CT739D/
    title: GodotCon Boston 2026 talk on Godot WebGPU
  - id: llvmpipe2wasm
    resource: https://github.com/Devsh-Graphics-Programming/llvmpipe2wasm
    title: Devsh llvmpipe2wasm — Mesa lavapipe built to WebAssembly (compute only)
  - id: tint-spirv-reader
    resource: https://dawn.googlesource.com/dawn/+/HEAD/docs/tint/spirv-reader-overview.md
    title: Tint SPIR-V reader overview
  - id: moltenvk
    resource: https://github.com/KhronosGroup/MoltenVK
    title: KhronosGroup/MoltenVK (v1.4.2, 2026-07-24)
  - id: moltenvk-config
    resource: https://github.com/KhronosGroup/MoltenVK/blob/main/Docs/MoltenVK_Configuration_Parameters.md
    title: MoltenVK configuration parameters
  - id: moltenvk-guide
    resource: https://github.com/KhronosGroup/MoltenVK/blob/main/Docs/MoltenVK_Runtime_UserGuide.md
    title: MoltenVK Runtime User Guide
  - id: kosmickrisp
    resource: https://docs.mesa3d.org/drivers/kosmickrisp.html
    title: Mesa KosmicKrisp (Vulkan on Metal 4) documentation
  - id: kosmickrisp-14
    resource: https://www.phoronix.com/news/KosmicKrisp-Vulkan-1.4-2026
    title: Phoronix — KosmicKrisp Vulkan 1.4 conformant (2026-10-03)
  - id: dozen
    resource: https://gitlab.freedesktop.org/mesa/mesa/-/raw/main/src/microsoft/vulkan/dzn_device.c
    title: Mesa Dozen (dzn) device source — Vulkan 1.2, non-conformant warning
  - id: gfx-portability
    resource: https://github.com/gfx-rs/portability
    title: gfx-rs/portability — Vulkan Portability on gfx-hal (inactive since 2023)
  - id: gfx-maintenance
    resource: https://github.com/gfx-rs/gfx
    title: gfx-rs/gfx README (gfx-hal maintenance mode)
  - id: angle-wgpu
    resource: https://github.com/google/angle/tree/main/src/libANGLE/renderer/wgpu
    title: ANGLE WebGPU backend source
  - id: zink
    resource: https://docs.mesa3d.org/drivers/zink.html
    title: Mesa Zink documentation
  - id: dawn-cmdbufvk
    resource: https://github.com/google/dawn/blob/main/src/dawn/native/vulkan/CommandBufferVk.cpp
    title: Dawn Vulkan backend CommandBufferVk.cpp
  - id: wgpu-hal
    resource: https://github.com/gfx-rs/wgpu/blob/trunk/wgpu-hal/src/vulkan/conv.rs
    title: wgpu-hal Vulkan conv.rs
  - id: vk-profiles
    resource: https://github.com/KhronosGroup/Vulkan-Profiles
    title: KhronosGroup/Vulkan-Profiles (toolset, layer, JSON schema)
  - id: vk-profiles-tutorial
    resource: https://github.com/KhronosGroup/Vulkan-Profiles/blob/main/layer/TUTORIAL.md
    title: Vulkan Profiles layer tutorial
  - id: vk-profiles-sdk
    resource: /Users/asherwin/VulkanSDK/1.4.328.1/macOS/share/vulkan/config/VK_LAYER_KHRONOS_profiles/
    title: Local Vulkan SDK 1.4.328.1 profile JSONs (VP_KHR_roadmap, VP_LUNARG_*, VP_ANDROID_*)
  - id: webgl-list-2016
    resource: https://www.khronos.org/webgl/public-mailing-list/public_webgl/1608/msg00018.php
    title: public_webgl mailing list (2016) — "WebVulkan" discussion
  - id: webkit-webgpu-2017
    resource: https://webkit.org/blog/7380/next-generation-3d-graphics-on-the-web/
    title: WebKit — Next-generation 3D Graphics on the Web (2017-02-07)
  - id: obsidian
    resource: https://www.phoronix.com/news/Mozilla-Obsidian-Graphics-API
    title: Phoronix — Mozilla Obsidian proposal (2017)
  - id: silk-net
    resource: https://www.nuget.org/packages/Silk.NET.WebGPU/
    title: Silk.NET.WebGPU NuGet package (2.23.0)
  - id: wgpusharp
    resource: https://www.nuget.org/packages/WgpuSharp
    title: WgpuSharp (Blazor WASM WebGPU via JS interop, alpha)
  - id: evergine
    resource: https://github.com/EvergineTeam/WebGPU.NET
    title: EvergineTeam/WebGPU.NET bindings
  - id: webgpusharp
    resource: https://www.nuget.org/packages/WebGPUSharp/
    title: WebGPUSharp (.NET 10, browser experimental via NativeAOT-LLVM)
  - id: unity-webgpu
    resource: https://docs.unity3d.com/6000.6/Documentation/Manual/WebGPU.html
    title: Unity 6000.6 manual — WebGPU
  - id: godot-web
    resource: https://github.com/godotengine/godot-docs/blob/master/tutorials/export/exporting_for_web.rst
    title: Godot docs — Exporting for the Web (WebGL 2 only)
  - id: bevy-features
    resource: https://github.com/bevyengine/bevy/blob/main/docs/cargo_features.md
    title: Bevy cargo features (webgpu vs webgl2)
  - id: sdl-gpu-webgpu
    resource: https://github.com/libsdl-org/SDL/pull/16020
    title: SDL3 GPU WebGPU backend draft PR #16020
  - id: bgfx
    resource: https://github.com/bkaradzic/bgfx
    title: bgfx (WebGPU via Dawn native)
  - id: prior-art-agent
    resource: "scope: sub-agent desk research on prior art, 2026-10-16 (GitHub API checks re-run by this agent)"
    title: Prior-art research compiled 2026-10-16
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:13:57Z }
status: draft
---

# Bottom line

- **Direct prior art exists but is young and partial.** Two public Vulkan-on-WebGPU layers appeared in 2026: **nikki** (compute-only, C++, runs in the browser via Emscripten + Dawn) and **VkWebGPU-ICD** (graphics, Rust over wgpu, native-only so far).[^nikki][^vkwebgpu-icd] Nobody has shipped Vulkan *graphics* in a browser through a Vulkan API.
- The closest working "Vulkan-shaped renderer → WebGPU in the browser" is the **Godot WebGPU fork**, which maps Godot's Vulkan-modelled `RenderingDevice` (not the Vulkan API) onto WebGPU with Tint for SPIR-V→WGSL.[^godotwebgpu]
- Their design choices converge with this bundle's recommendations: record-then-replay at submit, descriptor sets → bind groups, push constants via ring buffer / immediates, no-op barriers, CPU fences, combined-sampler splitting, SPIR-V→WGSL translation, explicit restricted profile.

# Direct prior art

| Project | Language / deps | Scope | Status (GitHub API, checked 2026-10-16) | Design notes |
| --- | --- | --- | --- | --- |
| nikki | C++, Dawn (native) / Emscripten (browser), Tint | restricted **compute** subset: one compute/transfer queue, buffers + mapped memory, descriptor layouts, push descriptors, update templates, specialization + push constants, compute pipelines, barriers, fences, timestamps; **no images, samplers or graphics**; reports API 1.1 and conformance 0.0.0.0[^nikki-profile] | created 2026-08-03, last push 2026-10-11, 23 stars | unsupported SPIR-V is rejected, "never silently approximated"; command buffers have explicit state machines; errors deferred to `vkEndCommandBuffer`; replay + readback processed asynchronously; browser build links a static library into the app (consumer: ncnn)[^nikki-profile][^nikki] |
| VkWebGPU-ICD | Rust, wgpu, naga 0.20, ash | graphics ICD aimed at running DXVK-translated games in a browser (CheerpX-based "WebX") | created 2026-02-17, last push 2026-07-14, 1 star; browser target still on the roadmap | naga SPIR-V→WGSL with a pre-pass splitting combined image samplers; sets → bind groups; push constants → ring buffer; host-visible memory via staging with coherent flush at `vkQueueSubmit`; barriers no-op; fences CPU state; semaphores rely on in-order submission; record then replay; secondaries inlined[^vkwebgpu-icd][^prior-art-agent] |
| godotwebgpu | C++, Emscripten ≥ 4.0.10, emdawnwebgpu, Tint | Godot 4.6.2 `RenderingDeviceDriver` for WebGPU (Forward Mobile renderer) | 70 stars, last push 2026-05-13; public beta 2026-05-10 | shows a Vulkan-modelled engine abstraction can run on browser WebGPU; GodotCon 2026 talk claims ~4× WebGL and ~70% of native performance (UNVERIFIED benchmark conditions)[^godotwebgpu][^godotcon-2026] |
| llvmpipe2wasm | CMake/C, Mesa lavapipe | software Vulkan compiled to wasm; only compute dispatch validated; no JIT ("LLJIT does not support WebAssembly in this form") | last push 2026-02-18 | not on WebGPU; demonstrates why software Vulkan in wasm is impractical for real-time graphics[^llvmpipe2wasm] |

Searches for "Vulkan on WebGPU", "VulkanOnWebGPU", "vk2webgpu", "WebVulkan", "vulkan webgpu translation layer", "zink webgpu", "mesa webgpu driver", "swiftshader wasm", "lavapipe emscripten" found nothing else beyond old Mesa softpipe Emscripten ports.[^prior-art-agent]

**Historical context.** "WebVulkan" was discussed on the WebGL list in 2015–16; Google engineers argued Vulkan's explicit memory model could not be exposed safely in browsers.[^webgl-list-2016] 2017 brought Google's NXT prototype, Apple's Metal-flavoured "WebGPU" proposal,[^webkit-webgpu-2017] Mozilla's Vulkan-derived "Obsidian" proposal,[^obsidian] and the Khronos 3D Portability effort; the W3C group converged on today's WebGPU, which deliberately abstracts memory and synchronization — exactly the parts a Vulkan façade must now re-emulate.

# Design references (other translation layers)

## MoltenVK — Vulkan on Metal (the closest analogue)

Metal has WebGPU's coordinate conventions and implicit-hazard flavour, so MoltenVK's choices transfer well:[^moltenvk][^moltenvk-config][^moltenvk-guide]

| Topic | MoltenVK | Lesson for the ICD |
| --- | --- | --- |
| Y flip | `MVK_CONFIG_SHADER_CONVERSION_FLIP_VERTEX_Y` (default on) flips Y in generated MSL; can be disabled for apps using negative viewport heights | same as our shader flip; keep a switch |
| Front face | passed straight through | confirms no inversion with a shader flip |
| Descriptor sets | Metal argument buffers (`MVK_CONFIG_USE_METAL_ARGUMENT_BUFFERS`, default on) or discrete bindings | WebGPU bind groups ≈ discrete path |
| Push constants | implicit buffer via `setVertexBytes`/`setFragmentBytes` | WebGPU immediates are the analogue |
| Memory | Private (device-local), Shared (host-visible + coherent), Managed (non-coherent, discrete Macs), Memoryless (lazily allocated) | advertise few, honest types |
| Sync | semaphore styles from "single queue, implicit ordering" to MTLEvent/MTLSharedEvent; fences CPU-side | single-queue no-op semaphores are an accepted style |
| Portability | reports `VK_KHR_portability_subset`; the loader only lists it when the app opts in with `VK_INSTANCE_CREATE_ENUMERATE_PORTABILITY_BIT_KHR` | same opt-in model fits a web ICD |
| Pipelines | SPIR-V→MSL via SPIRV-Cross; pipeline cache stores translated MSL | cache translated WGSL keyed by SPIR-V hash |

## Other layers

| Layer | Direction | Relevant design facts |
| --- | --- | --- |
| KosmicKrisp (LunarG, Mesa) | Vulkan → Metal 4 | macOS 26+, Apple Silicon only; NIR→MSL; Vulkan 1.4 conformant per XDC 2026 reports — shows a Mesa-style driver can reach conformance over a less explicit API[^kosmickrisp][^kosmickrisp-14] |
| Dozen (dzn, Mesa) | Vulkan → D3D12 | SPIR-V→NIR→DXIL; advertises Vulkan 1.2 and warns it is non-conformant — precedent for shipping an honest non-conformant layer[^dozen] |
| gfx-portability | Vulkan → gfx-hal (Metal/DX12) | ran Dota 2, vkQuake, Dolphin; stalled when gfx-hal went into maintenance and wgpu moved to wgpu-hal — a cautionary tale about depending on a moving inner API[^gfx-portability][^gfx-maintenance] |
| ANGLE WebGPU backend | OpenGL ES → WebGPU (in Chromium) | own GLSL→WGSL translator; flips `gl_Position.y` manually through a driver uniform (`flipXY`) and recomputes viewport/scissor on flipped framebuffers; default uniforms packed into a `var<uniform>` block in its own bind group next to a texture/sampler group and a driver-uniform group; still in development[^angle-wgpu] |
| Zink (Mesa) | OpenGL → Vulkan | requirements per GL version kept as a Vulkan Profiles file (`VP_ZINK_requirements.json`) and checked by tooling; missing features emulated in shaders — direct precedent for a profile-driven translation layer[^zink] |
| Dawn / wgpu Vulkan backends | WebGPU → Vulkan (inverse) | best source of format/enum tables (Dawn `TextureVk.cpp`, wgpu-hal `conv.rs`); both flip with a negative viewport height and map front face 1:1[^dawn-cmdbufvk][^wgpu-hal] |
| Tint SPIR-V reader | SPIR-V → WGSL | input must validate for Vulkan 1.1; only `gl_Position` survives from `gl_PerVertex`; `gl_PointSize` must be 1.0; combined `OpSampledImage` traced back to separate texture/sampler; rejects features WGSL lacks[^tint-spirv-reader] (details in `/shaders/`) |

## Software Vulkan in wasm — rejected

A software rasterizer (SwiftShader, lavapipe/llvmpipe) compiled to wasm would accept the full Vulkan API with no translation, but: there is no JIT path to wasm (llvmpipe2wasm runs compute only and cannot JIT shaders),[^llvmpipe2wasm] SwiftShader has no wasm port, wasm SIMD is 128-bit and multithreading needs cross-origin isolation, and a CPU rasterizer cannot meet game frame budgets at 1080p. It also bypasses the GPU entirely. **Reject** for the PoC; possibly useful only as an offline conformance oracle.

# .NET and engine ecosystem (WebGPU in the browser)

| Project | Status (Oct 2026) | Relevance |
| --- | --- | --- |
| Silk.NET | 2.23.0 (2026-01-22) incl. `Silk.NET.WebGPU`; 3.0 still in development; browser-wasm support UNVERIFIED[^silk-net] | native bindings; not a browser path today |
| WgpuSharp | 0.3.0-alpha (2026-03-25): Blazor WASM over JS interop with integer handles and a **batched per-frame bridge**[^wgpusharp] | same architecture as the proposed ICD's JS side |
| Evergine WebGPU.NET | Dawn-based bindings; Evergine moved its WebGPU backend from wgpu to Dawn to enable browser templates[^evergine] | commercial precedent for .NET + WebGPU on the web |
| WebGPUSharp | 0.5.7 (.NET 10), Dawn native; browser "experimental via NativeAOT-LLVM"[^webgpusharp] | alternative compile route |
| Unity | 6000.6 manual drops the "(Experimental)" label from WebGPU; WebGL2 still default[^unity-webgpu] | mainstream engines moving to WebGPU |
| Godot (official) | Web export is WebGL 2 only; WebGPU not supported[^godot-web] | the fork above fills the gap |
| Bevy | `webgpu` cargo feature overrides `webgl2` (one or the other per build)[^bevy-features] | — |
| SDL3 GPU | WebGPU backend in draft PR #16020 (2026-07-18), passing all 34 examples on Windows/Linux/Web[^sdl-gpu-webgpu] | an explicit-API abstraction close to Vulkan's model reaching the web |
| bgfx | WebGPU via Dawn native; active development[^bgfx] | — |

No Veldrid WebGPU backend was found (UNVERIFIED).

# Vulkan Profiles as the precedent for a documented subset

- The **Vulkan Profiles** toolset defines device capability sets as JSON (`$schema`, `capabilities` → {`extensions`, `features`, `properties`, `formats`, `queueFamiliesProperties`}, `profiles` → {`version`, `api-version`, `label`, `description`, `capabilities`, optional `optionals`/`fallback`/`status`}), with schemas published by Khronos (e.g. `profiles-0.8.2-*.json`).[^vk-profiles][^vk-profiles-sdk]
- Examples in the local SDK: `VP_KHR_roadmap.json` (2022/2024/2026), `VP_LUNARG_desktop_baseline.json`, `VP_LUNARG_minimum_requirements.json`, `VP_ANDROID_baseline_2021/2022.json`, `VP_ANDROID_15/16_minimums.json`.[^vk-profiles-sdk]
- The **profiles layer** (`VK_LAYER_KHRONOS_profiles`) *simulates* a device restricted to a profile on top of a real desktop driver (it cannot add capabilities); placed nearest the driver with the validation layer above it, any use beyond the profile is reported. Its `emulate_portability` setting makes any GPU report `VK_KHR_portability_subset` with per-boolean toggles — exactly what is needed to test BRUTAL code against a "WebGPU-shaped" Vulkan device on the desktop before touching a browser.[^vk-profiles-tutorial]
- Zink's `VP_ZINK_requirements.json` shows a translation layer publishing its requirements in this format.[^zink]
- nikki publishes a prose "Restricted API compute profile".[^nikki-profile]

These are the basis for the proposed `VP_BRUTAL_web_*` profiles in [web-vulkan-profile-proposal](/webgpu/web-vulkan-profile-proposal.md).

# Lessons for the BRUTAL ICD

1. Restrict and say so: publish a profile, reject unsupported SPIR-V/state loudly (nikki), report portability-subset booleans (MoltenVK).
2. Record-then-replay at submit is the universal architecture (nikki, VkWebGPU-ICD); it is also what enables batching the JS boundary.
3. Coordinate flip in the shader with unchanged front face (MoltenVK, wgpu-hal fallback, ANGLE).
4. Reuse proven tables (Dawn/wgpu-hal inverse maps) rather than re-deriving formats.
5. Consider the native route (C/C++ ICD + emdawnwebgpu linked into `dotnet.wasm`) if the managed-function-pointer path fails — nikki proves the static-link browser model works.

# Open questions

- UNVERIFIED: whether VkWebGPU-ICD runs in a browser yet (README lists WASM as future work).
- UNVERIFIED: Godot fork performance claims and which browsers it targets beyond Chrome.
- UNVERIFIED: Silk.NET.WebGPU usability under Blazor/browser-wasm.

[^nikki]: nihui/nikki — compute-only Vulkan subset over WebGPU (C++, created 2026-08-03)
[^nikki-profile]: nikki docs/vulkan-profile.md ("Restricted API compute profile")
[^vkwebgpu-icd]: Dankular/VkWebGPU-ICD — Vulkan ICD over wgpu (Rust, created 2026-02-17)
[^godotwebgpu]: dwalter/godotwebgpu — Godot 4.6 WebGPU RenderingDeviceDriver fork
[^godotcon-2026]: GodotCon Boston 2026 talk on Godot WebGPU
[^llvmpipe2wasm]: Devsh llvmpipe2wasm — Mesa lavapipe built to WebAssembly (compute only)
[^tint-spirv-reader]: Tint SPIR-V reader overview
[^moltenvk]: KhronosGroup/MoltenVK (v1.4.2, 2026-07-24)
[^moltenvk-config]: MoltenVK configuration parameters
[^moltenvk-guide]: MoltenVK Runtime User Guide
[^kosmickrisp]: Mesa KosmicKrisp (Vulkan on Metal 4) documentation
[^kosmickrisp-14]: Phoronix — KosmicKrisp Vulkan 1.4 conformant (2026-10-03)
[^dozen]: Mesa Dozen (dzn) device source — Vulkan 1.2, non-conformant warning
[^gfx-portability]: gfx-rs/portability — Vulkan Portability on gfx-hal (inactive since 2023)
[^gfx-maintenance]: gfx-rs/gfx README (gfx-hal maintenance mode)
[^angle-wgpu]: ANGLE WebGPU backend source
[^zink]: Mesa Zink documentation
[^dawn-cmdbufvk]: Dawn Vulkan backend CommandBufferVk.cpp
[^wgpu-hal]: wgpu-hal Vulkan conv.rs
[^vk-profiles]: KhronosGroup/Vulkan-Profiles (toolset, layer, JSON schema)
[^vk-profiles-tutorial]: Vulkan Profiles layer tutorial
[^vk-profiles-sdk]: Local Vulkan SDK 1.4.328.1 profile JSONs (VP_KHR_roadmap, VP_LUNARG_*, VP_ANDROID_*)
[^webgl-list-2016]: public_webgl mailing list (2016) — "WebVulkan" discussion
[^webkit-webgpu-2017]: WebKit — Next-generation 3D Graphics on the Web (2017-02-07)
[^obsidian]: Phoronix — Mozilla Obsidian proposal (2017)
[^silk-net]: Silk.NET.WebGPU NuGet package (2.23.0)
[^wgpusharp]: WgpuSharp (Blazor WASM WebGPU via JS interop, alpha)
[^evergine]: EvergineTeam/WebGPU.NET bindings
[^webgpusharp]: WebGPUSharp (.NET 10, browser experimental via NativeAOT-LLVM)
[^unity-webgpu]: Unity 6000.6 manual — WebGPU
[^godot-web]: Godot docs — Exporting for the Web (WebGL 2 only)
[^bevy-features]: Bevy cargo features (webgpu vs webgl2)
[^sdl-gpu-webgpu]: SDL3 GPU WebGPU backend draft PR #16020
[^bgfx]: bgfx (WebGPU via Dawn native)
[^prior-art-agent]: Prior-art research compiled 2026-10-16
