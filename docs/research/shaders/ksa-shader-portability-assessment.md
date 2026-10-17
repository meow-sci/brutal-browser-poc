---
type: Research Note
title: KSA shader portability assessment (what a Vulkan GLSL corpus uses that WebGPU cannot do)
description: Measured survey of the KSA reference game's 400 GLSL files (267 stage files) through glslc, naga and the browser's Tint, with per-feature counts, the named-shader outcomes, and the structural gaps (bindless textures, more than four descriptor sets, push constants over 64 bytes, draw parameters, tessellation, ray tracing).
tags: [shaders, ksa, portability, bindless, push-constants, descriptor-sets, naga, tint, assessment]
sources:
  - id: ksa-shaders
    resource: /Users/asherwin/repos/meow-sci/ksa-linux/Content/Core/Shaders
    title: KSA Core shaders (reference only, not copied into the project)
  - id: ksa-textureset
    resource: /Users/asherwin/repos/meow-sci/ksa-linux/Content/Core/Shaders/Common/TextureSet.glsl
    title: KSA bindless texture library header (SET_TEXTURE, runtime arrays aliased as 2D/array/cube/3D)
  - id: ksa-global
    resource: /Users/asherwin/repos/meow-sci/ksa-linux/Content/Core/Shaders/Common/Global.glsl
    title: KSA Global.glsl (set 0 UBOs, bindless handles as ints)
  - id: ksa-genericmesh
    resource: /Users/asherwin/repos/meow-sci/ksa-linux/Content/Core/Shaders/GenericMesh.vert
    title: KSA GenericMesh.vert (gl_DrawID, set 3 storage buffer) and GenericMesh.frag
  - id: ksa-pbr
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA.Rendering.PBR/PbrSpheres.cs
    title: KSA PbrSpheres (decompiled; four external descriptor set layouts, specialization constant, reverse-Z compare op)
  - id: ksa-shadow
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA.Rendering.Lighting/ShadowRenderTarget.cs
    title: KSA ShadowRenderTarget (decompiled; viewport MinDepth/MaxDepth swapped when ReverseZ)
  - id: exp02-ksa-logs
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/logs
    title: exp-02 KSA logs (13-ksa-named, 13-ksa-pushsize, 13-ksa-tint, 13-ksa-survey, 13-ksa-layout-lint, 13-ksa-bench-all)
  - id: exp02-ksa-summaries
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out
    title: exp-02 per-shader summaries (ksa-summary-vulkan1.{1,3}[-prepared].tsv, ksa-tint-summary.json; names and messages only)
  - id: exp02-survey-script
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/ksa-survey.sh
    title: ksa-survey.sh (reproduces the survey; outputs go outside the project)
  - id: webgpu-spec
    resource: https://www.w3.org/TR/webgpu/
    title: WebGPU specification (default limits)
  - id: web3dsurvey-bindgroups
    resource: https://web3dsurvey.com/webgpu/limits/maxBindGroups
    title: Web3DSurvey maxBindGroups distribution
  - id: dawn-dynbind
    resource: https://dawn.googlesource.com/dawn/+/cb66e36c25952abbb95bfbeb00e85e4042affe6c/docs/tint/extensions/chromium_experimental_dynamic_binding.md
    title: Dawn chromium_experimental_dynamic_binding (experimental binding arrays; no spec)
  - id: chrome-next
    resource: https://developer.chrome.com/blog/next-for-webgpu
    title: "What's next for WebGPU (2024-11-21): bindless listed as highly anticipated"
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:38:44Z }
status: draft
---

# Scope and method

Reference only: KSA's shaders were read and compiled in place, nothing was copied into this project. All 267 stage files (`*.vert *.frag *.comp *.tesc *.tese` and ray-tracing stages) under `Content/` (400 shader-related files including 133 `.glsl` includes) were compiled with `glslc -O -I Core/Shaders -I Content` for Vulkan 1.1 (SPIR-V 1.3) and Vulkan 1.3 (SPIR-V 1.6), optionally prepared (`spv-prepare.mjs`), translated with naga-wasm, and the resulting WGSL compiled by Tint in headless Edge 155.[^exp02-survey-script] Counts below are from those runs; feature counts by `grep` are per **file** (includes counted), module counts come from SPIR-V capabilities. Source files read: the Core shader tree[^ksa-shaders], `Common/Global.glsl`[^ksa-global], `GenericMesh.vert/frag`[^ksa-genericmesh]. Per-shader summaries: `out/ksa-summary-*.tsv`, `out/ksa-tint-summary.json`.[^exp02-ksa-summaries] Companion notes: [naga](/shaders/naga-spirv-to-wgsl.md), [conventions](/shaders/wgsl-conventions-for-vulkan-layer.md), [KSA pipeline](/shaders/brutal-shader-pipeline-today.md).

# Headline numbers

| Stage of the pipeline | Result (267 stage files) |
|---|---|
| glslc fails | 9 at Vulkan 1.1 (5 ray-tracing stages need Vulkan 1.2+ SPIR-V; 3 files begin with a UTF-8 BOM and, once stripped, still contain real GLSL errors (`overlapping use of location 0`, a redeclared block); 1 needs `FFX_BLUR_OPTION_KERNEL_DIMENSION`) |
| naga translates, target `vulkan1.3` (SPIR-V 1.6), raw / with pre-pass | 76 / 118 |
| naga translates, target `vulkan1.1` (SPIR-V 1.3), raw / with pre-pass | 122 / **187** |
| Tint (Edge 155) compiles the 187, raw / with directive patches | 156 / **164** |
| Of 258 compiled modules: not bindless / bindless (declare `RuntimeDescriptorArray`) | 186 / 72; Tint OK 141 of 186 (76%) vs 23 of 72 (32%) |
| By stage (Tint OK / total) | compute 97 / 139, fragment 34 / 68, vertex 33 / 51, tessellation 0 / 4, ray tracing 0 / 5 |
| Time to translate the 187 modules (2.5 MB SPIR-V, 3.4 MB WGSL) with naga-wasm in Node | about 100 ms total (98 and 109 ms in two runs) |

So about **61% of KSA's stage files** reach valid browser WGSL with the generic pipeline and no source edits when the ICD advertises Vulkan 1.1; the remainder needs shader or architecture changes, listed next. Advertising Vulkan 1.3 instead (shaderc then emits SPIR-V 1.6 with `OpCopyLogical` and `OpDemoteToHelperInvocation`) cuts naga's success to 118 even with the pre-pass.[^exp02-ksa-logs]

# Features used by KSA versus WebGPU

| Feature (KSA usage) | Evidence | WebGPU / toolchain status | Impact and workaround |
|---|---|---|---|
| **Bindless textures**: `texture2D globalTextures[]`, `texture2DArray globalTextureArrays[]`, `textureCube`, `texture3D` all declared at the **same** `(set, binding 0)` plus `sampler samplers[]`, indexed by integer handles read from UBOs, `GL_EXT_nonuniform_qualifier` in 5 files, `nonuniformEXT` in 7 | `TextureSet.glsl`; 72 of 258 modules declare `RuntimeDescriptorArray`; 4 use `ShaderNonUniform`.[^ksa-textureset] | No binding arrays in core WebGPU (naga emits `enable wgpu_binding_array;`, Tint rejects it); Dawn has an experimental `chromium_experimental_dynamic_binding` with no spec; Chrome lists bindless as anticipated.[^dawn-dynbind][^chrome-next] Aliasing four resource types on one binding is invalid in WGSL (`Bindings ... conflict with other resource`, 7 fragment shaders). | **High.** Material system, sky/atmosphere LUT lookups, PBR IBL all go through it. Needs redesign: per-material bind groups, `texture_2d_array` atlases, or a fixed small set of bindings. |
| **More than 4 descriptor sets**: GenericMesh uses sets 0 to 5 (global, bindless library, per-draw, instances, CSM, lighting); PBR spheres lists four external layouts plus others[^ksa-pbr] | 26 of 258 modules reference set >= 4 (max 8); 71 reference set >= 3, which collides with the reserved push-constant group; `@group(4..6)` seen in translated WGSL | `maxBindGroups` default 4; Web3DSurvey: >=4 for all adapters, >=7 for 20%, >=11 for 14% (undated).[^webgpu-spec][^web3dsurvey-bindgroups] | **High.** Merge sets; split resources by update frequency into at most 3 groups plus the push group. |
| **Push constants** | 94 of 258 modules use them (88 files by grep); sizes: 40 modules <= 16 B, 14 in 17-32, 11 in 33-64, **29 in 65-128 B** (max 128) | immediates <= 64 B (adapter limit on the test machine), Chrome 149+; `immediateSize 80` rejected in a browser test | **Medium.** Tier-2 uniform/storage emulation at group 3; 4 modules have arrays in the block (immediates reject arrays; use storage fallback). |
| **`gl_DrawID` / draw parameters** (indirect mesh draws) | 8 modules (`GenericMesh.vert`, `MeshIndirect.vert`, ...) fail with `UnsupportedCapability(DrawParameters)` | no draw-index builtin in WGSL; `@builtin(instance_index)` includes first instance | **Medium.** Per-draw index through an instance-rate attribute or immediates written by the ICD (see [semantic gaps](/webgpu/semantic-gaps-and-emulation.md)). |
| **Specialization constants** (`constant_id` in 42 files; e.g. cascaded shadow filter on id 10) | simple scalars import as `@id(N) override`; 22 modules fail with `UnsupportedInstruction(Type, SpecConstantOp)` (folded expressions, spec-sized arrays, `local_size_*_id`) | WGSL `override` exists; naga 30.0.1 cannot import `OpSpecConstantOp` | **Medium.** Precompile variants per value (the pipeline knows them), or rewrite array sizes as `const`. |
| **Combined `sampler2D`** | 80 files | no combined type | **Low**, automated (texture@b + sampler@b+500). |
| **Tessellation** (planet terrain: 2 `.tesc`, 2 `.tese`) | `UnsupportedCapability(Tessellation)`; 4 modules | not in WebGPU | **High for the planet renderer.** Compute tessellation or pre-tessellated chunks. |
| **Ray tracing / ray queries** (5 RT stages; `RAY_QUERIES_SUPPORTED`, 2 files use `rayQueryEXT`) | not translated | none | Disable by not defining `RAY_QUERIES_SUPPORTED` (KSA already makes it optional). |
| **Subgroup ops** (5 files) | `GroupNonUniformElect` unsupported; arithmetic/ballot translate but need `enable subgroups;` | `subgroups` feature in Chromium | Low; patch directive, request the feature. |
| **Storage images / formats** (65 files; 39 modules need `StorageImageExtendedFormats`) | translate to `texture_storage_2d<...>`; 4 modules fail `InvalidImage` | WebGPU storage formats are a subset | Medium; check each format ([format tables](/webgpu/format-tables.md)). |
| **Write-only SSBOs** (`writeonly buffer`, about 20 modules) | validation error until `NonReadable` is stripped | no write-only storage buffers | Low, automated. |
| **Atomics, shared memory, barriers** (35 and 35 files) | translate; 5 modules call `workgroupBarrier()` in non-uniform control flow and fail in Tint | WGSL uniformity rule is an error for barriers | Low to medium; restructure early returns. |
| **Derivatives / implicit LOD in divergent flow** (19 files with `dFdx/fwidth`, many `texture()` calls) | 6 modules that Tint rejects raw compile once `diagnostic(off, derivative_uniformity)` is prepended (plus 2 fixed by stripping `@coherent`) | WGSL uniformity analysis | Low, automated. |
| **64-bit (`double`, `int64`)** (12 files) | `f64`/`u64` emitted, Tint rejects | none | Low; remove or emulate. |
| **Buffer references (BDA)** (1 file, plus `GL_EXT_buffer_reference_uvec2`) | `UnsupportedCapability(PhysicalStorageBufferAddresses)` | none | Low; one shader. |
| **Subpass inputs** (1 file) | not supported | none | Low; rewrite as texture load. |
| **`isnan` / `isinf`** | `Unsupported relational function: IsInf` (e.g. `Grid.frag` after the pre-pass) | naga writer gap | Low; `spirv-webgpu-transform isnanisinf` or avoid. |
| **Reverse-Z with swapped viewport depth** (shadow targets use `MinDepth = 1, MaxDepth = 0`; PBR uses `DepthCompareOp.Greater`) | decompiled C#[^ksa-shadow][^ksa-pbr] | WebGPU `setViewport` validation (see [coordinates](/webgpu/coordinate-systems-and-conventions.md)) | Low; put reverse-Z in the projection matrix. |
| **std140 scalar/vec2 arrays in UBOs** | `spv-layout-lint` over 258 modules: 0 hazards | silent wrong reads in naga output | none found; keep authoring discipline. |
| **Y flip, winding** | KSA uses positive viewports and Vulkan projection matrices | naga flips by default | no shader change. |

# Named shaders (outcomes of `ksa-named.sh`)

| Shader | `vulkan1.3` raw | `vulkan1.1` + pre-pass | Tint |
|---|---|---|---|
| `FullScreenTriangle.vert` (const array indexed by `gl_VertexIndex`) | OK | OK | OK |
| `Copy.frag` (combined `sampler2D`, `texelFetch`; MSAA variant uses `sampler2DMS` + `gl_SampleID`) | `InvalidId(14)` (combined sampler) | OK | OK |
| `Line.vert`, `Line.frag` (set 0 UBOs via `Common/Global.glsl`, `gl_FragDepth` write) | OK | OK | OK |
| `Grid.vert` | OK | OK | OK |
| `Grid.frag` | `UnsupportedCapability(DemoteToHelperInvocation)` | `Unsupported relational function: IsInf` | n/a |
| `GenericMesh.vert` (`gl_DrawID`, set 3 SSBO) | `UnsupportedCapability(DrawParameters)` | same | n/a |
| `GenericMesh.frag` (bindless `globalTextures[]` + sampler, sets 1, 2, 4, 5) | `UnsupportedInstruction(CopyLogical)` | validation: `Entry point main at Fragment is invalid` (bindless aliasing conflict) | n/a |

# Interpretation for a web port

1. **The toolchain is not the bottleneck; the rendering architecture is.** The generic pipeline (Vulkan 1.1 target, pre-pass, naga, directive patches) handles the compute-heavy and simple graphics shaders, but the lit-mesh path (bindless materials, six descriptor sets, draw ids) cannot be translated mechanically.
2. A realistic first target is a **web profile of the shaders**: `#define WEB_TARGET` branches in the includes that replace `TextureSet.glsl` with fixed per-material bindings, cap descriptor sets at three plus the push group, replace `gl_DrawID`, and drop tessellation and ray tracing.
3. For the PoC (mesh, UBO, texture, sampler) none of these limits apply; this assessment tells the project which BRUTAL-based content to expect to be hard.
4. Vulkan-side consequences for the layer (limits to report, features to hide) are in [limits](/webgpu/webgpu-limits-features-and-compat-mode.md).

## Open questions

- UNVERIFIED: how many of KSA's 72 bindless-declaring modules are used by shipping pipelines (some declare the arrays without using them after optimisation; 36 of the 72 translate).
- UNVERIFIED: whether the 3 BOM files are dead code (they fail with genuine GLSL errors once the BOM is stripped; KSA may tolerate the BOM in its shaderc path).
- UNVERIFIED: WebGPU storage-texture format coverage for the 39 `StorageImageExtendedFormats` modules; formats were not enumerated.
- UNVERIFIED: that the Tint-OK modules also create valid pipelines against real layouts (only compile validation was run for KSA-derived WGSL).
- UNVERIFIED: 5 modules whose barrier uniformity failure may be a false positive of Tint's analysis on naga's structured control flow (not diagnosed).

[^ksa-shaders]: KSA Core shaders (reference only, not copied into the project)
[^ksa-global]: KSA Global.glsl (set 0 UBOs, bindless handles as ints)
[^ksa-genericmesh]: KSA GenericMesh.vert (gl_DrawID, set 3 storage buffer) and GenericMesh.frag
[^ksa-pbr]: KSA PbrSpheres (decompiled; four external descriptor set layouts, specialization constant, reverse-Z compare op)
[^ksa-shadow]: KSA ShadowRenderTarget (decompiled; viewport MinDepth/MaxDepth swapped when ReverseZ)
[^exp02-ksa-summaries]: exp-02 per-shader summaries (names and messages only)
[^exp02-survey-script]: ksa-survey.sh (reproduces the survey; outputs go outside the project)
[^exp02-ksa-logs]: exp-02 KSA logs (13-ksa-named, 13-ksa-pushsize, 13-ksa-tint, 13-ksa-survey, 13-ksa-layout-lint, 13-ksa-bench-all)
[^ksa-textureset]: KSA bindless texture library header (SET_TEXTURE, runtime arrays aliased as 2D/array/cube/3D)
[^dawn-dynbind]: Dawn chromium_experimental_dynamic_binding (experimental binding arrays; no spec)
[^chrome-next]: What's next for WebGPU (2024-11-21): bindless listed as highly anticipated
[^webgpu-spec]: WebGPU specification (default limits)
[^web3dsurvey-bindgroups]: Web3DSurvey maxBindGroups distribution
