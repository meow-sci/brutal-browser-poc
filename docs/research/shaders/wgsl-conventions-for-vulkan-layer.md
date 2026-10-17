---
type: Convention
title: WGSL conventions for the Vulkan-on-WebGPU layer (the shader contract)
description: The binding, location, push-constant, combined-sampler, Y-flip, specialization, layout and directive rules that the SPIR-V-to-WGSL pipeline guarantees and the C# layer relies on, each backed by an experiment or marked UNVERIFIED.
tags: [shaders, convention, wgsl, bindings, push-constants, combined-sampler, y-flip, specialization-constants, std140, contract]
sources:
  - id: exp02-readme
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/README.md
    title: exp-02 project README
  - id: exp02-patch
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/src/patch-wgsl.mjs
    title: patch-wgsl.mjs (Y-flip wrapper, push-constant rewrite, directives, binding reflection)
  - id: exp02-prepare
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/src/spv-prepare.mjs
    title: spv-prepare.mjs (split combined samplers + sampler binding offset, strip NonReadable)
  - id: exp02-lint
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/src/spv-layout-lint.mjs
    title: spv-layout-lint.mjs (std140 stride hazards)
  - id: exp02-browser
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/browser-check.result.json
    title: exp-02 results from headless Edge 155 on Apple Metal (Tint, render/winding test, pipeline layouts, std140 probe, limits)
  - id: exp02-logs
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/logs
    title: exp-02 raw logs
  - id: webgpu-spec
    resource: https://www.w3.org/TR/webgpu/
    title: WebGPU W3C specification (coordinate systems, default limits, immediateSize validation)
  - id: chrome-immediates
    resource: https://developer.chrome.com/blog/new-in-webgpu-149-150
    title: "What's new in WebGPU (Chrome 149 and 150): immediates"
  - id: web3dsurvey-bindgroups
    resource: https://web3dsurvey.com/webgpu/limits/maxBindGroups
    title: "Web3DSurvey maxBindGroups (>=4: 100%, >=7: 20%, >=11: 14% of reports; undated)"
  - id: swt
    resource: https://github.com/davnotdev/spirv-webgpu-transform
    title: spirv-webgpu-transform (immediates to uniform set N+1 at binding 0, combimg, bindingarray)
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:38:44Z }
status: draft
---

# Purpose

The C# ICD receives SPIR-V in `vkCreateShaderModule` and builds `GPUBindGroupLayout`/`GPURenderPipeline` objects. This note fixes the numbers and rules that make those two halves agree. Everything marked **verified** was run on a real WebGPU implementation (headless Edge 155 = Chromium 155, Apple GPU via Metal) with `createShaderModule`, `createRenderPipeline`/`createComputePipeline` and read-backs.[^exp02-browser] Reproduction: [exp-02 README].[^exp02-readme] Related notes: [object mapping](/webgpu/vulkan-to-webgpu-object-mapping.md), [coordinate systems](/webgpu/coordinate-systems-and-conventions.md), [semantic gaps](/webgpu/semantic-gaps-and-emulation.md), [limits](/webgpu/webgpu-limits-features-and-compat-mode.md).

# Contract table

| # | Vulkan / SPIR-V | WGSL / WebGPU | Rule | Status |
|---|---|---|---|---|
| C1 | descriptor set `s`, binding `b` | `@group(s) @binding(b)` | 1:1, no renumbering. Applies to uniform/storage buffers, sampled images, samplers, storage images. | verified (naga and Slang outputs keep numbers) |
| C2 | `layout(location = n) in` | `@location(n)` vertex input | `GPUVertexAttribute.shaderLocation = n`. | verified (pipeline A) |
| C3 | `layout(location = n) out` (fragment) | `@location(n)` | color attachment index `n`. Dual-source blending (`index = 1`) is not covered here. | UNVERIFIED for dual-source |
| C4 | varyings vertex-out to fragment-in | `@location(n)` on both | same `n`; `flat` becomes `@interpolate(flat)`. | verified (flat) |
| C5 | `OpEntryPoint ... "name"` / `pName` | `fn name` / `entryPoint: name` | name preserved by naga and Slang; glslc modules use `main`. | verified |
| C6 | `VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER` at binding `b` | texture at `b`, sampler at **`b + 500`** (`COMBINED_SAMPLER_BINDING_OFFSET = 500`) | requires `b < 500` (`maxBindingsPerBindGroup` default is 1000).[^webgpu-spec] Pre-pass: `spv-prepare.mjs`.[^exp02-prepare] Layer builds two layout entries and two bind-group entries from one Vulkan descriptor. | verified (pipeline A: texture@1, sampler@501) |
| C7 | push constants | tiered, see C8 | | verified |
| C8 | push-constant block of `N` bytes | **tier 1** `var<immediate>` if `N <= maxImmediateSize` and the adapter lists `immediate_address_space`; **tier 2** `@group(P) @binding(0) var<uniform>` (or `var<storage, read>`), `P = 3` by default | see below | tier 1 compile verified; tier 2 pipeline verified |
| C9 | Vulkan clip space (Y down) | WebGPU NDC (Y up) | vertex position `.y` negated exactly once; `frontFace`, `cullMode` unchanged. | verified |
| C10 | `layout(constant_id = N)` | `@id(N) override` | `GPUProgrammableStage.constants["N"] = value`. | WGSL verified; pipeline constants UNVERIFIED |
| C11 | depth range [0,1] | NDC z in [0,1] | no change. | from spec[^webgpu-spec] |
| C12 | `gl_FragCoord` (`OriginUpperLeft`) | `@builtin(position)` | framebuffer coordinates, origin top-left, y down in both; no change. | spec; shader-side Vulkan flag `OriginUpperLeft` seen in glslc output |
| C13 | module directives | prepend `diagnostic(off, derivative_uniformity);`, `enable subgroups;` when subgroup builtins appear; keep naga's `enable clip_distances;`; strip `@coherent` | | verified |
| C14 | `gl_PointSize` write, 64-bit types, `binding_array`, `f64` | **forbidden** (fail the build) | no WebGPU equivalent | verified (errors) |
| C15 | `OpDecorate Block` (UBO) / `BufferBlock` + `Uniform` (SPIR-V 1.0 SSBO) / `StorageBuffer` class | `var<uniform>` / `var<storage, read_write>` | naga accepts all three spellings (an old-style `BufferBlock` SSBO from `vulkan1.0` became `var<storage, read_write>`); naga does **not** insist on `Block` (a UBO struct without it, which `spirv-val` rejects, still translated) so validity must be checked with `spirv-val` in the build | verified |

# C6: combined image samplers, in detail

- `glslc` emits `OpTypeSampledImage` variables for `uniform sampler2D tex;`. naga fails with `InvalidId(n)`; Tint rejects explicitly ([Tint](/shaders/tint-spirv-to-wgsl.md)).
- Pre-pass (build time): `spirv-opt --split-combined-image-sampler` ("new variables have the same bindings as the original variable"), then raise the `Binding` of every sampler variable that collides with a non-sampler at the same `(set, binding)` by 500. Genuine `uniform sampler` variables keep their binding.
- Result for the PoC variant `layout(set=0,binding=1) uniform sampler2D tex;`: `@group(0) @binding(1) var global_3: texture_2d<f32>;` and `@group(0) @binding(501) var global_2: sampler;`. A pipeline created with a layout `{0: uniform buffer, 1: texture, 501: sampler}` was accepted by the browser.
- The alternative tool `spirv-webgpu-transform combimg` gives sampler = `b + 1` and shifts later bindings, returning a correction map; that is harder for a stateless ICD, hence the fixed offset.[^swt]
- Descriptor writes: `VkDescriptorImageInfo { sampler, imageView }` for a combined descriptor creates **two** `GPUBindGroupEntry`s (`b` and `b + 500`). A combined descriptor with `descriptorCount > 1` has no WebGPU equivalent unless the array is constant-sized and expanded (`spirv-webgpu-transform bindingarray` numbers elements consecutively, which collides with the `+500` scheme; not designed here).
- Sampler/texture compatibility: WebGPU needs the layout entry's `sampler.type` (`filtering`, `non-filtering`, `comparison`) and `texture.sampleType` (`float`, `unfilterable-float`, `depth`, `sint`, `uint`) to agree. Vulkan descriptor layouts carry neither, so both must come from the WGSL declarations (`texture_2d<f32>`, `texture_depth_2d`, `sampler`, `sampler_comparison`, `texture_storage_2d<fmt,access>`, `texture_multisampled_2d`) via the manifest or `reflectBindings`; textures with unfilterable formats need `unfilterable-float` + `non-filtering`.

# C8: push constants

1. **What naga emits** for `layout(push_constant) uniform P { mat4 model; vec4 tint; } pc;`: `var<immediate> global: type_3;` (members anonymous). No `@group/@binding`, no `requires`. naga 30.0.1 spelling; older naga releases and Tint's experimental extension used `var<push_constant>` (UNVERIFIED which naga release changed it), so patching must accept both (`patchPush` regex: `var<push_constant|immediate>`).[^exp02-patch]
2. **Tier 1 (immediates).** Chrome 149/150 ships `var<immediate>`, `setImmediates(offset, data)` and `immediateSize` on the pipeline layout (`layout: 'auto'` infers it).[^chrome-immediates] On the tested adapter `maxImmediateSize` was **64 bytes**; a layout with `immediateSize: 80` is rejected (`immediateSize (80) is larger than the maximum allowed (64)`), 64 is accepted.[^exp02-browser] The spec default is 64 and sizes must be multiples of 4.[^webgpu-spec] Vulkan guarantees at least 128 bytes, so tier 2 is mandatory (KSA: 29 of 94 push-constant shaders exceed 64 bytes, max 128;[^exp02-logs] see [KSA assessment](/shaders/ksa-shader-portability-assessment.md)). Chromium 155 compiled naga's `var<immediate>` **without** `requires immediate_address_space;` and also with it; the line is accepted when the language feature exists. Because naga's own WGSL parser rejects the `requires` line (`unknown language extension`), validate before adding it.
3. **Tier 2 (emulation).** `patch-wgsl.mjs --push uniform --push-group 3 --push-binding 0` rewrites the declaration to `@group(3) @binding(0) var<uniform> NAME: TYPE;`. The ICD owns a uniform buffer bound at group 3 (a per-frame ring of 256-byte slots addressed with a dynamic offset is the design in [semantic gaps](/webgpu/semantic-gaps-and-emulation.md)), `visibility = union of stages that use push constants`. Verified: pipeline layout `[bgl0, empty, empty, bgl3]` with the patched vertex + fragment modules was accepted.
   - Pick `P` once per device: **3** unless the application uses 4 sets (then `P = 4` and `maxBindGroups >= 5` is required; Web3DSurvey reports `maxBindGroups >= 4` for all, `>= 7` for 20%, `>= 11` for 14%, undated[^web3dsurvey-bindgroups]). The ICD should report `maxBoundDescriptorSets = maxBindGroups - 1` when emulating.
   - The block layout can violate uniform rules. Probe: `float weights[4]` in a push block gives naga-valid `var<immediate>` but `var<uniform>` fails naga validation (`The array stride 4 is not a multiple of the required alignment 16`) and, in Edge 155, `immediate` itself rejects arrays in KSA modules (`arrays cannot be used in the <immediate> address space`). Fallback: `--push storage` (`var<storage, read>`, std430 packing, validated by naga and Tint). The build lint decides per shader and writes the mode into the manifest.
   - `spirv-webgpu-transform immediates` does the same at SPIR-V level (uniform block in set `N+1` at binding 0, std430 converted to std140).[^swt]
4. **Stage ranges.** Vulkan push ranges are per stage with offsets; naga emits one struct typed as declared, so vertex and fragment modules each see the full declared block. The ICD uploads the union once; it does not need per-stage ranges for correctness.

# C9: Y flip and winding (verified on a GPU)

A one-triangle Vulkan-convention vertex shader (`shaders/tri.vert`: top-left, bottom-left, bottom-right in Vulkan screen space, i.e. visually counter-clockwise) was translated three ways and drawn into a 16 x 16 `rgba8unorm` target with `cullMode: 'back'`:[^exp02-browser]

| Vertex WGSL | `frontFace: ccw` (Vulkan's value) | `frontFace: cw` | Image |
|---|---|---|---|
| naga default (flip baked in) | 36 red pixels | 0 | triangle with right angle at **bottom-left**, as on Vulkan |
| naga `--keep-coordinate-space` (no flip) | 0 | 36 | mirrored vertically (right angle top-left) |
| no-flip + `patch-wgsl.mjs --y-flip` wrapper | 36 | 0 | identical to naga default |

Consequences: (a) flip once at build time and keep `frontFace` and `cullMode` as the application set them; (b) with naga's SPIR-V front end **do not** run the wrapper (double flip); (c) with Slang-direct WGSL or a GLSL-front-end path the wrapper is required; (d) an application that uses a negative-height viewport or `MinDepth > MaxDepth` needs special handling in the command layer ([coordinate systems](/webgpu/coordinate-systems-and-conventions.md)).

Wrapper form produced by `patchYFlip` (validated by naga and Tint): original function renamed `NAME_vk_inner` (attributes stripped), new `@vertex fn NAME(...)` with the original signature calls it, negates `.y` of the `@builtin(position)` member (found by regex in the returned struct) and returns.

# C10: specialization constants

naga imports `OpSpecConstant` as `@id(N) override override_type: f32 = 2.2f;` (name synthetic, `@id` = Vulkan `constantID`). Per the WebGPU API (not re-checked in this run), `GPUProgrammableStage.constants` is keyed by the override's name **or its numeric id as a string**, so the ICD sets `constants: { "7": 3 }` from `VkSpecializationInfo` (type taken from the WGSL declaration: `bool`, `i32`, `u32`, `f32`). `OpSpecConstantOp` (folded expressions) and `local_size_*_id` are not importable by naga 30.0.1, so shaders for the web must use specialization constants directly. Pipeline-creation-time `constants` were **not** exercised in the browser here (UNVERIFIED).

# Uniform and storage block layout

| Case | std140 UBO (Vulkan) | naga WGSL | Safe? |
|---|---|---|---|
| `vec4`, `mat4`, `mat3`, `vec3` + scalar pack, `ivec2`, struct of those | std140 offsets | natural WGSL layout, same offsets | yes: GPU read-back matched std140 for `vec4`, `mat4`, `vec3` + `float` pack, `mat3` and `vec4[2]` (slots 1, 10, 22, 23, 29, 43); KSA layout lint flagged 0 of 258 modules |
| `float a[N]`, `vec2 a[N]` | element stride 16 | `array<f32, N>` / `array<vec2<f32>, N>` (stride 4 / 8) | **no, silent wrong reads** (GPU probe: slots 1, 3, 6, 8, 11, 17 read instead of 4, 12, 20, 24, 27, 33). In browsers without `uniform_buffer_standard_layout` the module is rejected instead. |
| `mat2`, `matCx2` | column stride 16 | naga rejects: `UnsupportedMatrixStride` | build error |
| std430 SSBO (`float a[]`, structs) | natural | natural | yes |
| `std140` SSBO of structs (KSA material buffer) | struct stride rounded to 16 | natural WGSL struct rules | usually same; spot-check |

Rules: author UBO arrays as `vec4`; run `spv-layout-lint.mjs`[^exp02-lint]; naga emits **no** explicit `@size/@align` for these blocks (it relies on natural layout), whereas Slang emits `@align(16)` members and `_MatrixStorage_float4x4_ColMajorstd140` wrappers.

# Pipeline layout and Vulkan limits implied

- Report `maxBoundDescriptorSets <= 4` (3 with tier-2 push emulation), `maxPushConstantsSize = 128` served by the tiers, `maxPerStageDescriptorSamplers/SampledImages` from the adapter (the test adapter reported 16 samplers and 48 sampled textures per stage).[^exp02-browser]
- Empty bind group layouts are legal fillers for unused set indices (verified pipeline B).
- No arrays of descriptors, no bindless; see [KSA](/shaders/ksa-shader-portability-assessment.md).

# Manifest entry the layer reads (per SPIR-V content hash)

```json
{ "spirvSha256": "...", "wgsl": "shaders/<name>.wgsl", "entryPoints": [{"name":"main","stage":"vertex"}],
  "bindings": [{"group":0,"binding":0,"kind":"uniform"},{"group":0,"binding":1,"kind":"texture","sampleType":"float","dim":"2d"},{"group":0,"binding":501,"kind":"sampler","type":"filtering","fromCombined":1}],
  "push": {"bytes":80,"mode":"uniform","group":3,"binding":0,"stages":["vertex","fragment"]},
  "overrides": [{"id":7,"type":"i32"}], "yFlip":"naga", "directives":["diagnostic(off, derivative_uniformity)"] }
```

## Open questions

- UNVERIFIED: `maxImmediateSize` on other adapters (the one tested reported 64; Vulkan's minimum is 128, so tier 2 must always exist).
- UNVERIFIED: immediates layout rules (WGSL spec text for `var<immediate>` was not retrieved); Edge 155 rejected arrays in immediate blocks.
- UNVERIFIED: `constants: {"<id>": v}` pipeline keying with naga's synthetic override names.
- UNVERIFIED: Firefox and Safari (immediates, `uniform_buffer_standard_layout`, language features); only Chromium was available.
- How should the `+500` offset interact with applications whose real bindings approach 500, and with bindless-style layouts? (In the KSA survey `@binding(500)` appears only as the sampler half of combined samplers declared at binding 0.)

[^exp02-readme]: exp-02 project README
[^exp02-prepare]: spv-prepare.mjs (split combined samplers + sampler binding offset, strip NonReadable)
[^exp02-lint]: spv-layout-lint.mjs (std140 stride hazards)
[^exp02-logs]: exp-02 raw logs
[^exp02-patch]: patch-wgsl.mjs (Y-flip wrapper, push-constant rewrite, directives, binding reflection)
[^exp02-browser]: exp-02 results from headless Edge 155 on Apple Metal (Tint, render/winding test, pipeline layouts, std140 probe, limits)
[^webgpu-spec]: WebGPU W3C specification (coordinate systems, default limits, immediateSize validation)
[^chrome-immediates]: What's new in WebGPU (Chrome 149 and 150): immediates
[^web3dsurvey-bindgroups]: Web3DSurvey maxBindGroups (>=4: 100%, >=7: 20%, >=11: 14% of reports; undated)
[^swt]: spirv-webgpu-transform (immediates to uniform set N+1 at binding 0, combimg, bindingarray)
