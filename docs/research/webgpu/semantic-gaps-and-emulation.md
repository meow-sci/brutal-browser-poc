---
type: Research Note
title: Semantic gaps between Vulkan and WebGPU, with recommended emulations and costs
description: Catalogue of every Vulkan capability a BRUTAL renderer or a game like KSA may rely on that WebGPU lacks or restricts — coordinates, push constants, combined samplers, border colours, swizzle, MSAA counts, texel buffers, raster state, shader types, atomics, subgroups, queues, memory, alignment, formats, bindless/BDA, ray tracing, mesh shaders, multiview and more — each with the recommended emulation, its cost, and whether the Web Vulkan profile allows, emulates or forbids it.
tags: [vulkan, webgpu, emulation, gaps, bindless, push-constants, formats, shaders]
sources:
  - id: webgpu-spec
    resource: https://gpuweb.github.io/gpuweb/
    title: WebGPU W3C Editor's Draft, 8 October 2026
  - id: wgsl-spec
    resource: https://gpuweb.github.io/gpuweb/wgsl/
    title: WGSL W3C Editor's Draft, 8 October 2026
  - id: chrome-immediates
    resource: https://developer.chrome.com/release-notes/150
    title: Chrome 150 release notes — WebGPU immediates
  - id: chrome-143
    resource: https://developer.chrome.com/blog/new-in-webgpu-143
    title: What's New in WebGPU (Chrome 143) — texture-component-swizzle
  - id: toji-bindless
    resource: https://toji.dev/2026/10/06/webgpu-bindless.html
    title: Brandon Jones, "WebGPU bindless" (2026-10-06) — resource-table preview
  - id: texel-buffers
    resource: https://dawn.googlesource.com/dawn/+/HEAD/docs/tint/extensions/texel_buffers.md
    title: Dawn/Tint experimental texel_buffers extension
  - id: zink
    resource: https://docs.mesa3d.org/drivers/zink.html
    title: Mesa Zink documentation (feature emulation in a translation layer)
  - id: moltenvk-guide
    resource: https://github.com/KhronosGroup/MoltenVK/blob/main/Docs/MoltenVK_Runtime_UserGuide.md
    title: MoltenVK Runtime User Guide
  - id: brutal-partitioners
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/Partitioner/Image
    title: Brutal.Vulkan.Abstractions image partitioners (TextureArrayPartitioner, Atlas2DPartitioner, QuadTreePartitioner)
  - id: ksa-devicectx
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/Core/KSADeviceContextEx.cs
    title: KSA decompiled device bootstrap (reference only)
  - id: ksa-bindless
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/RenderCore.Systems/BindlessTextureLibrary.cs
    title: KSA decompiled bindless texture library (reference only)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:13:57Z }
status: draft
---

Profile column: **P** in the PoC profile as-is, **E** emulated by the ICD (cost noted), **X** forbidden (app must avoid), **F** future (browser feature preview/proposal). See [profile proposal](/webgpu/web-vulkan-profile-proposal.md).

# Pipeline and fixed-function

| Gap | Vulkan | WebGPU | Recommended approach | Cost | Profile |
| --- | --- | --- | --- | --- | --- |
| Clip-space Y | NDC +y down | NDC +y up | negate `position.y` in the translated vertex shader; keep `frontFace` 1:1 ([coordinates](/webgpu/coordinate-systems-and-conventions.md)) | 1 ALU op | E (free) |
| Negative viewport height, reversed depth viewport | allowed | `height ≥ 0`, `minDepth ≤ maxDepth`[^webgpu-spec] | rewrite viewport + unflipped pipeline variant; reversed depth via projection matrix | variant | E / X (reversed range) |
| Push constants | ≥ 128 B | immediates ≤ 64 B (Chrome 150+)[^chrome-immediates] | immediates when ≤ 64 B; else 256-B slots in a per-frame uniform ring + dynamic offset | ring: 256 B upload per changed draw + 1 bind group | E |
| Polygon mode LINE/POINT | `fillModeNonSolid` | none | app draws a line-list index buffer (BRUTAL `Presets.Rasterization.Line`) or barycentric wireframe in FS | app work | X |
| Wide lines, large points | `wideLines`, `largePoints` | 1-px lines/points only | expand to quads in the app (instanced) | app work | X |
| Logic ops | `logicOp` | none | — | — | X |
| Depth bounds test | `depthBounds` | none | — | — | X |
| Per-face stencil masks/reference | separate front/back | one read mask, write mask, reference[^webgpu-spec] | require equal values (portability `separateStencilMaskRef = false`) | — | X |
| CONSTANT_ALPHA blend factors | yes | `constant` is RGBA only | set blend constant to (a,a,a,a) if the pipeline doesn't also use CONSTANT_COLOR | — | E (partial) |
| Independent blend per attachment | `independentBlend` | core yes, compat no[^webgpu-spec] | — | — | P (core) |
| Alpha-to-one, `minSampleShading`, sample locations | yes | none (sample-rate shading only via WGSL `@interpolate(..., sample)`/`sample_index`) | — | — | X |
| MSAA sample counts | 1/2/4/8/16 | 1 or 4[^webgpu-spec] | report `framebufferColorSampleCounts = 1\|4` | — | P |
| Depth clamp | `depthClamp` | `unclippedDepth` with `depth-clip-control` | 1:1 when the feature exists | — | P (feature-gated) |
| Depth bias clamp | `depthBiasClamp` | core yes, compat must be 0 | — | — | P |
| Dynamic state (cull, topology, depth/stencil ops, bias, ...) | Vulkan 1.3 extended dynamic state | baked into the pipeline | pipeline variants keyed by state, cached | first-use compile stall; memory | E |
| Primitive restart toggles | per pipeline | always on for indexed strips; `stripIndexFormat` fixed at creation[^webgpu-spec] | variant per index format; apps must not use 0xFFFF/0xFFFFFFFF as real indices | — | E |
| Triangle fans | `triangleFans` (portability) | none | convert to lists (CPU index generation for indexed/non-indexed); KSA enables `triangleFans`[^ksa-devicectx] | CPU work per draw | E (slow path) |
| Adjacency topologies, patches | yes | none | — | — | X |
| Tessellation, geometry shaders | yes | none | app redesign: compute pre-pass writing vertex/index buffers + indirect draws | large | X |
| Mesh/task shaders | `VK_EXT_mesh_shader` | none | compute + indirect | large | X |
| Multiview | `multiview` (KSA enables it)[^ksa-devicectx] | none (view-instancing is a Draft proposal) | render each view in its own pass | N× draw calls | X / F |
| Conditional rendering | `VK_EXT_conditional_rendering` | none | CPU decision with frame-old query results, or zero indirect counts | latency | X |
| Indirect count draws | `drawIndirectCount` | none (Chrome `multi-draw-indirect` experimental) | compute zeroes args beyond count, then `maxDrawCount` indirect draws | wasted draws | X / F |
| Multi-draw indirect (`drawCount > 1`) | `multiDrawIndirect` | one draw per call | CPU loop over `drawCount` | one encoder call per draw | E |
| Transform feedback | `VK_EXT_transform_feedback` | none | storage-buffer writes from VS | — | X |

# Resources and binding

| Gap | Vulkan | WebGPU | Recommended approach | Cost | Profile |
| --- | --- | --- | --- | --- | --- |
| Combined image samplers | `COMBINED_IMAGE_SAMPLER` | separate texture + sampler bindings | split in SPIR-V/WGSL translation with a fixed binding remap shared by ICD and translator | 2 bindings each (counts against both 16-per-stage limits) | E (PoC: prefer separate samplers in shaders) |
| Descriptor arrays | `descriptorCount > 1`, dynamic indexing | no binding arrays in core | flatten to N bindings + switch on index in the shader (uniform index only) | binding-slot pressure; non-uniform indexing impossible | E (small, uniform) / X |
| Bindless (UPDATE_AFTER_BIND, PARTIALLY_BOUND, runtime arrays, non-uniform indexing) | descriptor indexing (KSA's `BindlessTextureLibrary`: one sampled-image array + 16 samplers)[^ksa-bindless] | none in core; Chrome 155–156 preview of **sampling resource tables** behind a flag (`createResourceTable`, WGSL `getResource<texture_2d<f32>>(i)`)[^toji-bindless] | today: **texture arrays** (`2d-array`, same size/format, ≤ 256 layers default) or **atlases** — BRUTAL already ships `TextureArrayPartitioner`, `Atlas2DPartitioner`, `QuadTreePartitioner`;[^brutal-partitioners] per-material bind groups; later: resource tables when they ship | asset-pipeline constraints (uniform sizes), atlas bleeding | X now, F |
| Buffer device address / pointers | `bufferDeviceAddress` | none | one large storage buffer "arena" + 32-bit offsets; WGSL `buffer_view` for reinterpretation within a binding[^wgsl-spec] | shader rewrite; arena ≤ `maxStorageBufferBindingSize` (128 MiB default) | X (app-level redesign) |
| Descriptor buffers | `VK_EXT_descriptor_buffer` | none | — | — | X |
| Push descriptors, update templates | `VK_KHR_push_descriptor`, templates | none | transient descriptor set / template expansion | bind-group creation per change (cached) | E |
| Inline uniform blocks | `inlineUniformBlock` | none | ICD-owned UBO | small upload | E |
| Texel buffers | `UNIFORM/STORAGE_TEXEL_BUFFER` | none in core (Dawn-only experimental `texel_buffer`)[^texel-buffers] | storage buffer + manual format decode in the translated shader | ALU per fetch | E (limited formats) / X |
| Input attachments / subpasses | multi-subpass render passes | single pass | split into passes, input attachment → sampled texture | bandwidth (no tile memory) | E |
| Border colours | `CLAMP_TO_BORDER` | none | (a) `clamp-to-edge` + reserve a cleared 1-texel border in the image (shadow maps: clear to 1.0); (b) shader wrapper `select(border, s, all(uv ∈ [0,1]))` for samplers known to be border-mode at pipeline creation | (a) memory; (b) ALU per sample + variant | E |
| `MIRROR_CLAMP_TO_EDGE`, unnormalized coords, sampler LOD bias, min/max reduction | yes | none | shader rewrite (bias → `textureSampleBias`) or forbid | — | X (bias: E) |
| Non-identity component swizzle | `VkComponentMapping` | `texture-component-swizzle` (Chrome 143+)[^chrome-143] | use the feature when present; else swizzle data on upload or in shader | — | E (feature-gated) |
| Image view format reinterpretation | `MUTABLE_FORMAT` (any compatible) | only `-srgb` twins via `viewFormats`[^webgpu-spec] | copies or compute | — | X (srgb: P) |
| 2D view of 3D image, 1D arrays, multisampled arrays | portability flags | none | — | — | X |
| Cube arrays | `imageCubeArray` | core yes, compat no | — | — | P (core) |
| 3-component / packed 16-bit texel formats | `R8G8B8`, `R5G6B5`, ... | none | expand at asset build time | memory | X |
| 16-bit UNORM/SNORM textures | core Vulkan | only with `texture-formats-tier1`; sample type unfilterable[^webgpu-spec] | feature-gate; prefer float16 | — | E (feature-gated) |
| Float32 filtering/blending | common | `float32-filterable`, `float32-blendable` | feature-gate | — | P (feature-gated) |
| Depth format copies | D24S8/D32 copies | `depth24plus*` depth never copyable; `depth32float` copy-in only from same format[^webgpu-spec] | render depth via `frag_depth` / read via shader | extra pass | E |
| Compressed formats | BC/ETC2/ASTC | per-device feature; block-aligned sizes unless `texture-compression-unaligned` | transcode KTX2/Basis at load to what the adapter has | load time | E |
| Storage image formats | many (`shaderStorageImageExtendedFormats`) | short list; read-write only r32* in core, more with tier2[^webgpu-spec] | choose formats from the list | — | P (restricted) |
| Image atomics | `imageAtomic*` | none in WGSL | storage buffer atomics | — | X |
| Linear tiling, host-visible images | yes | none | staging through buffers | copy | X |
| Sparse resources | yes | none | — | — | X |

# Shaders and numerics

| Gap | Vulkan / SPIR-V | WGSL | Recommended approach | Profile |
| --- | --- | --- | --- | --- |
| `f64`, `i64`/`u64` | `shaderFloat64`, `shaderInt64` | none | forbid; i64 emulation via `vec2<u32>` in a translator is possible but slow | X |
| `i16`/`u16`, 8-bit storage | `shaderInt16`, 8/16-bit storage | no 16-bit integers; `f16` with `shader-f16` | widen to 32-bit; f16 feature-gated | E (f16) / X (int16) |
| Float atomics, 64-bit atomics | `shaderBufferFloat32Atomics`, `Int64Atomics` | i32/u32 atomics only; `atomic-vec2u-min-max` experimental | CAS loops on u32 bitcasts | X |
| Subgroups | Vulkan 1.1 subgroup ops | `subgroups` feature (Chrome 134+), quad ops, size control | feature-gate; don't assume a size | P (feature-gated) |
| Derivative uniformity | allowed in non-uniform control flow (results undefined) | WGSL **rejects** `textureSample`/derivatives in non-uniform control flow unless `diagnostic(off, derivative_uniformity)`[^wgsl-spec] | translator emits the diagnostic directive or `textureSampleLevel` | E (translator) |
| Helper invocations / demote | `OpDemoteToHelperInvocation`, `OpKill` | `discard` has demote semantics | 1:1 | P |
| `gl_Layer`, `gl_ViewportIndex` | multi-layer/viewport rendering | none | one pass per layer | X |
| `gl_ClipDistance` | `shaderClipDistance` | `clip_distances` (`clip-distances` feature) | feature-gate | P |
| `gl_CullDistance` | `shaderCullDistance` | none | clip distance + discard | X |
| `gl_PrimitiveID` in FS | yes | `primitive_index` (feature) | feature-gate | P |
| `gl_BaseVertex/BaseInstance/DrawID` | `shaderDrawParameters` (KSA enables it)[^ksa-devicectx] | none | immediates holding per-draw values written by the ICD | E |
| Specialization constants | yes | `override` constants with `@id` | 1:1 | P |
| Uniform buffer layout | std140 (or std430/scalar with extensions) | uniform address space needs 16-byte array strides unless `uniform_buffer_standard_layout`[^wgsl-spec] | translator check; avoid scalar layout | P (std140) |
| Robustness | optional | always robust (OOB reads return in-bounds or zero values) | none needed; minor shader cost | P |
| Workgroup memory zero-init | optional | always zero-initialized | — | P |
| Compute limits | ≥ 128 invocations, 16 KiB shared, 65535 groups | 256 / 16 KiB / 65535 (compat 128)[^webgpu-spec] | — | P |

# Execution, sync and memory

| Gap | Vulkan | WebGPU | Recommended approach | Profile |
| --- | --- | --- | --- | --- |
| Multiple queues / async compute | yes | one queue | alias all queues to one; async compute becomes serial | E |
| Secondary command buffers | yes | render bundles (limited) | inline replay (default) or bundles | E |
| Fences, binary/timeline semaphores, events | GPU/host sync | implicit ordering, promises | CPU emulation; early-signalled upload fences ([sync](/webgpu/synchronization-and-async-shape.md)) | E |
| Blocking waits | yes | impossible on the event-loop thread | non-blocking returns, frame-lagged readback | X (blocking on readback) |
| Pipeline barriers / layouts | explicit | automatic per usage scope | no-op | P |
| Read/write hazards inside one render pass | allowed with care (e.g. SSBO written in FS, read later in the same pass; feedback loops) | a render pass is one usage scope: a subresource cannot be both writable-storage/attachment and read elsewhere in the pass[^webgpu-spec] | split passes; forbid feedback loops | X |
| Persistent mapping, coherent memory | yes | not expressible | wasm-heap shadow + `writeBuffer` at submit ([memory model](/webgpu/memory-model-and-mapping-emulation.md)) | E |
| Host-visible device-local (ReBAR) | yes | n/a | advertise emulated type | E |
| Memory aliasing | yes | no | forbid overlapping binds | X |
| Uniform/storage offset alignment | ≤ 256 (device-specific, often 16–64) | 256 | report 256; apps must query (hard-coded 64 breaks) | P |
| Texture copy row pitch | any | `bytesPerRow` multiple of 256 | repack (CPU from shadow, or compute) | E |
| Vertex buffer offsets | any | multiple of 4 | forbid unaligned | X |
| Buffer sizes, copy sizes | any | multiples of 4 for copies/`writeBuffer`/`clearBuffer` | round buffer sizes up; forbid unaligned copies | E/X |
| Colour attachment bytes per sample | no limit | ≤ 32 B across all targets[^webgpu-spec] | design G-buffers within 32 B/px | X (beyond) |
| Vertex buffers per pipeline | ≥ 16 | 8 | — | X (beyond) |
| Bind groups | ≥ 4 sets (+ push constants separate) | 4 groups total (push-constant ring takes one) | ≤ 3 sets when push constants exceed immediates | X (beyond) |
| Ray tracing / ray query | `VK_KHR_ray_*` (KSA optional)[^ksa-devicectx] | none | raster fallbacks (shadow maps, SSR, SSAO) | X |
| Query types | occlusion, timestamp, pipeline statistics, ... | occlusion; timestamp at pass boundaries | — | E (partial) |
| Device memory budget | `VK_EXT_memory_budget` | none | not advertised | X |

# How other layers handled the same gaps

- **Zink** ties each OpenGL version to a list of required Vulkan features and emulates missing GL features in shaders (e.g. quads/edge flags/stippling via geometry or fragment shader passes) — the same "profile + targeted shader emulation" pattern proposed here.[^zink]
- **MoltenVK** reports `VK_KHR_portability_subset` booleans for the Vulkan features Metal cannot express and emulates the cheap ones (triangle fans, events), documenting the rest.[^moltenvk-guide]

# Open questions

- UNVERIFIED: whether Chrome's resource-table preview will ship by default in 2027 and whether Firefox/Safari will implement it — decisive for any bindless BRUTAL renderer.
- Agree the combined-sampler/array binding-remap function with the `/shaders/` tool chain.
- Quantify triangle-fan conversion and pipeline-variant counts on real BRUTAL content before promising them.

[^webgpu-spec]: WebGPU W3C Editor's Draft, 8 October 2026
[^wgsl-spec]: WGSL W3C Editor's Draft, 8 October 2026
[^chrome-immediates]: Chrome 150 release notes — WebGPU immediates
[^chrome-143]: What's New in WebGPU (Chrome 143) — texture-component-swizzle
[^toji-bindless]: Brandon Jones, "WebGPU bindless" (2026-10-06) — resource-table preview
[^texel-buffers]: Dawn/Tint experimental texel_buffers extension
[^zink]: Mesa Zink documentation (feature emulation in a translation layer)
[^moltenvk-guide]: MoltenVK Runtime User Guide
[^brutal-partitioners]: Brutal.Vulkan.Abstractions image partitioners (TextureArrayPartitioner, Atlas2DPartitioner, QuadTreePartitioner)
[^ksa-devicectx]: KSA decompiled device bootstrap (reference only)
[^ksa-bindless]: KSA decompiled bindless texture library (reference only)
