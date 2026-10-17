---
type: Reference
title: WebGPU limits, optional features, WGSL extensions and compatibility mode (vs Vulkan minimums)
description: Every GPUSupportedLimits entry with its core and compatibility-mode default next to the Vulkan 1.x required minimum it would be reported as, every GPUFeatureName with its Vulkan equivalent and shipping status, the WGSL language extensions, the compatibility-mode restrictions, and the webgpu.h C API status, as of October 2026.
tags: [webgpu, limits, features, compatibility-mode, wgsl, vulkan, webgpu-h]
sources:
  - id: webgpu-spec
    resource: https://gpuweb.github.io/gpuweb/
    title: WebGPU W3C Editor's Draft, 8 October 2026 (commit 25a5dc4)
  - id: webgpu-cr
    resource: https://www.w3.org/TR/webgpu/
    title: WebGPU W3C Candidate Recommendation Draft, 15 September 2026
  - id: wgsl-spec
    resource: https://gpuweb.github.io/gpuweb/wgsl/
    title: WGSL W3C Editor's Draft, 8 October 2026
  - id: vk-limits
    resource: https://docs.vulkan.org/spec/latest/chapters/limits.html
    title: Vulkan specification, Limits chapter (Required Limits table)
  - id: compat-proposal
    resource: https://github.com/gpuweb/gpuweb/blob/main/proposals/compatibility-mode.md
    title: gpuweb proposal — Compatibility mode (status Merged)
  - id: chrome-news
    resource: https://developer.chrome.com/docs/web-platform/webgpu/news
    title: Chrome "What's New in WebGPU" index (posts for Chrome 121–156)
  - id: chrome-146
    resource: https://developer.chrome.com/blog/new-in-webgpu-146
    title: What's New in WebGPU (Chrome 146) — compatibility mode, TRANSIENT_ATTACHMENT
  - id: chrome-immediates
    resource: https://developer.chrome.com/release-notes/150
    title: Chrome 150 release notes — WebGPU immediates
  - id: chrome-155
    resource: https://developer.chrome.com/blog/new-in-webgpu-155-156
    title: What's New in WebGPU (Chrome 155–156) — texture-compression-unaligned, bindless preview
  - id: toji-bindless
    resource: https://toji.dev/2026/10/06/webgpu-bindless.html
    title: Brandon Jones, "WebGPU bindless" (2026-10-06)
  - id: webgpu-headers
    resource: https://github.com/webgpu-native/webgpu-headers
    title: webgpu-native/webgpu-headers (webgpu.h marked stable 2025-09-23)
  - id: emscripten-changelog
    resource: https://github.com/emscripten-core/emscripten/blob/main/ChangeLog.md
    title: Emscripten ChangeLog (-sUSE_WEBGPU deprecated 4.0.10, removed 4.0.18)
  - id: emdawnwebgpu
    resource: https://dawn.googlesource.com/dawn/+/refs/heads/main/src/emdawnwebgpu/pkg/README.md
    title: Dawn emdawnwebgpu package README
  - id: status-agent
    resource: "scope: sub-agent desk research on browser status, 2026-10-16 (see /webgpu/webgpu-status-and-browser-support-2026.md)"
    title: Browser/feature shipping research compiled 2026-10-16
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:13:57Z }
status: draft
---

# Spec baseline

- Normative text used here: the WebGPU and WGSL Editor's Drafts dated **8 October 2026**; the published W3C Candidate Recommendation Draft is dated 15 September 2026.[^webgpu-spec][^webgpu-cr]
- Every limit now has two defaults: the **default** (what a "core" adapter guarantees) and the **compatibility mode default**. Adapters always meet the defaults; a device gets exactly the default unless the app asks for more in `requestDevice({ requiredLimits })`.[^webgpu-spec]
- `requestAdapter({ featureLevel: "compatibility" })` returns an adapter whose devices default to compatibility limits and lack `"core-features-and-limits"`; if the implementation cannot enforce compatibility validation it silently treats the request as `"core"`.[^webgpu-spec]

# GPUSupportedLimits vs Vulkan required minimums

"Vulkan min" is the Vulkan Core required value from the spec's Required Limits table (Roadmap/1.4 values in brackets where they differ).[^vk-limits] "ICD reports" is what a Vulkan-on-WebGPU ICD should put into `VkPhysicalDeviceLimits` (using the *device's* actual limits, which may exceed defaults).

| WebGPU limit | Default | Compat default | Vulkan equivalent | Vulkan min | ICD reports / notes |
| --- | --- | --- | --- | --- | --- |
| maxTextureDimension1D | 8192 | 4096 | maxImageDimension1D | 4096 [8192] | 1:1 |
| maxTextureDimension2D | 8192 | 4096 | maxImageDimension2D, maxImageDimensionCube, maxFramebufferWidth/Height, maxViewportDimensions | 4096 [8192] | 1:1 (also cube and framebuffer) |
| maxTextureDimension3D | 2048 | 2048 | maxImageDimension3D | 256 [512] | 1:1 |
| maxTextureArrayLayers | 256 | 256 | maxImageArrayLayers, maxFramebufferLayers | 256 [2048] | 1:1; framebuffer layers = 1 (no layered rendering) |
| maxBindGroups | 4 | 4 | maxBoundDescriptorSets | 4 [7 in 1.4] | report `maxBindGroups − 1` if push constants are emulated through a reserved bind group (see push constants) |
| maxBindGroupsPlusVertexBuffers | 24 | 24 | — | — | internal validation only |
| maxImmediateSize | 64 | 64 | maxPushConstantsSize | **128** [256 in 1.4] | WebGPU's 64 B is below Vulkan's minimum; report 128 and spill to a UBO ring when > 64 |
| maxBindingsPerBindGroup | 1000 | 1000 | (binding numbers) | — | binding numbers ≥ 1000 must be remapped |
| maxDynamicUniformBuffersPerPipelineLayout | 8 | 8 | maxDescriptorSetUniformBuffersDynamic | 8 | 1:1 (minus one if a dynamic UBO carries push constants) |
| maxDynamicStorageBuffersPerPipelineLayout | 4 | 4 | maxDescriptorSetStorageBuffersDynamic | 4 | 1:1 |
| maxSampledTexturesPerShaderStage | 16 | 16 | maxPerStageDescriptorSampledImages | 16 [200] | 1:1; combined image samplers count once here and once in samplers |
| maxSamplersPerShaderStage | 16 | 16 | maxPerStageDescriptorSamplers | 16 [64] | 1:1 |
| maxStorageBuffersPerShaderStage | 8 | 8 | maxPerStageDescriptorStorageBuffers | 4 [30] | 1:1 |
| maxStorageBuffersInVertexStage | 8 | **0** | (vertexPipelineStoresAndAtomics) | — | compat: report `vertexPipelineStoresAndAtomics = false` and no SSBOs in VS |
| maxStorageBuffersInFragmentStage | 8 | 4 | — | — | — |
| maxStorageTexturesPerShaderStage | 4 | 4 | maxPerStageDescriptorStorageImages | 4 [16] | 1:1 |
| maxStorageTexturesInVertexStage | 4 | **0** | — | — | — |
| maxStorageTexturesInFragmentStage | 4 | 4 | — | — | — |
| maxUniformBuffersPerShaderStage | 12 | 12 | maxPerStageDescriptorUniformBuffers | 12 [15] | 1:1 |
| maxUniformBufferBindingSize | 65536 | **16384** | maxUniformBufferRange | 16384 [65536] | 1:1 |
| maxStorageBufferBindingSize | 128 MiB | 128 MiB | maxStorageBufferRange | 2^27 = 128 MiB | 1:1 |
| minUniformBufferOffsetAlignment | 256 | 256 | minUniformBufferOffsetAlignment | ≤ 256 | 256 (apps that hard-code 64 break) |
| minStorageBufferOffsetAlignment | 256 | 256 | minStorageBufferOffsetAlignment | ≤ 256 | 256 |
| maxVertexBuffers | 8 | 8 | maxVertexInputBindings | **16** | WebGPU is below the Vulkan minimum; report 8 (non-conformant) |
| maxBufferSize | 256 MiB | 256 MiB | maxMemoryAllocationSize (1.1), maxBufferSize (1.3) | 2^30 | report device value; VkDeviceMemory larger than this can only back many small buffers |
| maxVertexAttributes | 16 | 16 | maxVertexInputAttributes | 16 | in compat, `vertex_index`/`instance_index` each consume one |
| maxVertexBufferArrayStride | 2048 | 2048 | maxVertexInputBindingStride | 2048 | also `maxVertexInputAttributeOffset = 2047`; stride must be a multiple of 4 |
| maxInterStageShaderVariables | 16 | 15 | maxVertexOutputComponents / maxFragmentInputComponents | 64 | report 4 × limit (64 / 60); builtins like `front_facing` count against it |
| maxColorAttachments | 8 | 4 | maxColorAttachments, maxFragmentOutputAttachments | 4 [8] | 1:1 |
| maxColorAttachmentBytesPerSample | 32 | 32 | — (no Vulkan limit) | — | **hidden constraint**: e.g. 4 × RGBA16F = 32 B fits, 3 × RGBA32F = 48 B fails; the ICD must reject such render passes |
| maxComputeWorkgroupStorageSize | 16384 | 16384 | maxComputeSharedMemorySize | 16384 | 1:1 |
| maxComputeInvocationsPerWorkgroup | 256 | 128 | maxComputeWorkGroupInvocations | 128 [256] | 1:1 |
| maxComputeWorkgroupSizeX / Y / Z | 256 / 256 / 64 | 128 / 128 / 64 | maxComputeWorkGroupSize | (128,128,64) | 1:1 |
| maxComputeWorkgroupsPerDimension | 65535 | 65535 | maxComputeWorkGroupCount | 65535 | 1:1 |

Vulkan limits with **no WebGPU source** that the ICD must invent: `maxSamplerAnisotropy` = 16 (WebGPU clamps `maxAnisotropy` to the platform maximum and most support 1–16), `maxSamplerLodBias` = 0 (no LOD bias on samplers), `maxViewports` = 1, `maxClipDistances` = 8 with `clip-distances` else 0, `maxCullDistances` = 0, `pointSizeRange` = [1,1], `lineWidthRange` = [1,1], `maxDrawIndirectCount` = 1 unless multi-draw is emulated by a CPU loop, `bufferImageGranularity` = 1, `nonCoherentAtomSize` = 4 (host-visible memory is advertised coherent anyway), `optimalBufferCopyRowPitchAlignment` = 256, `optimalBufferCopyOffsetAlignment` = 256, `minMemoryMapAlignment` = 64, `framebuffer*SampleCounts` = 1|4, `timestampPeriod` = 1.0 (WebGPU timestamps are nanoseconds, quantized), `maxTexelBufferElements` = 0/65536 depending on texel-buffer emulation.[^webgpu-spec][^vk-limits]

# GPUFeatureName list (ED 2026-10-08) with Vulkan equivalents

Shipping data is Chrome-centric, taken from Chrome's "What's New in WebGPU" posts (Firefox/Safari data in MDN BCD may lag); see [browser status](/webgpu/webgpu-status-and-browser-support-2026.md).[^chrome-news][^status-agent]

| Feature | Enables | Closest Vulkan feature / extension | Chrome ships | Value for a Vulkan ICD |
| --- | --- | --- | --- | --- |
| `core-features-and-limits` | lifts compat restrictions, core limits | — | 139 | required by the profile (core devices always have it) |
| `depth-clip-control` | `primitive.unclippedDepth` | `depthClamp` (+ `VK_EXT_depth_clip_enable`) | yes (early) | report `depthClamp` when present |
| `depth32float-stencil8` | `depth32float-stencil8` format | `VK_FORMAT_D32_SFLOAT_S8_UINT` | yes | format support |
| `texture-compression-bc` (+ `-bc-sliced-3d`) | BC1–7 (2D; 3D with sliced) | `textureCompressionBC` | desktop | desktop only; mobile lacks |
| `texture-compression-etc2` | ETC2/EAC | `textureCompressionETC2` | mobile (+ some desktop) | — |
| `texture-compression-astc` (+ `-astc-sliced-3d`) | ASTC LDR | `textureCompressionASTC_LDR` | mobile, Apple | — |
| `texture-compression-unaligned` | compressed textures whose size is not a block multiple | (Vulkan allows this natively) | 155–156 | without it, Vulkan apps creating e.g. 100×100 BC7 images fail |
| `timestamp-query` | `"timestamp"` queries, **only at pass begin/end** (`timestampWrites`) | `timestampComputeAndGraphics` | 121 | `vkCmdWriteTimestamp` only approximable at pass boundaries |
| `indirect-first-instance` | non-zero `firstInstance` in indirect draws | `drawIndirectFirstInstance` | yes | 1:1 |
| `shader-f16` | `f16` in WGSL | `shaderFloat16`, `storageBuffer16BitAccess` (partly) | yes | 1:1-ish |
| `rg11b10ufloat-renderable` | render/blend/MSAA on `rg11b10ufloat` | colour attachment on `B10G11R11_UFLOAT` | yes | format features |
| `bgra8unorm-storage` | storage on `bgra8unorm` | storage image `B8G8R8A8_UNORM` | yes | format features |
| `float32-filterable` | linear filtering of r/rg/rgba32float | `SAMPLED_IMAGE_FILTER_LINEAR` on 32F | yes | format features |
| `float32-blendable` | blending on 32F | `COLOR_ATTACHMENT_BLEND` on 32F | yes | format features |
| `clip-distances` | `clip_distances` builtin (≤ 8) | `shaderClipDistance` | 131 (Safari 27) | 1:1 |
| `dual-source-blending` | `@blend_src`, `src1*` factors | `dualSrcBlend` | 130 | 1:1 |
| `subgroups` | subgroup + quad ops | Vulkan 1.1 subgroup ops | 134 | partial (no shuffle-relative/clustered parity guaranteed) |
| `subgroup-size-control` | `@subgroup_size` | `subgroupSizeControl` | 151–152 | 1:1 |
| `texture-formats-tier1` | r/rg/rgba16 unorm/snorm, more render/storage formats | `shaderStorageImageExtendedFormats`, 16-bit norm formats | 142 | **needed** for `R16_UNORM`-style textures, which otherwise do not exist |
| `texture-formats-tier2` | read-write storage on more formats | `shaderStorageImageReadWithoutFormat`-like | 142 | — |
| `primitive-index` | `primitive_index` in FS | `PrimitiveId` in FS (needs geometry/tess capability in Vulkan) | 142 | — |
| `texture-component-swizzle` | `GPUTextureViewDescriptor.swizzle` (`[rgba01]{4}`); defines depth reads as (V,0,0,1) | `VkComponentMapping`, portability `imageViewFormatSwizzle` | 143 | lets the ICD honour non-identity swizzles |
| `atomic-vec2u-min-max` | `atomicStoreMin/Max` on `atomic<vec2u>` | `shaderBufferInt64Atomics` (min/max subset) | experimental | visibility-buffer style tricks only |

Not in the spec but relevant: **immediates** are not a feature flag — `setImmediates()` and `maxImmediateSize` are core API, shipped in Chrome 150 (2026-06-30); WGSL side is `var<immediate>` behind `requires immediate_address_space`.[^webgpu-spec][^chrome-immediates] **Bindless "resource tables"** (`createResourceTable({size})`, `table.insert(view|sampler)`, WGSL `getResource<texture_2d<f32>>(i)`, feature `"sampling-resource-table"`) is a preview behind `chrome://flags/#enable-unsafe-webgpu` (Chrome 155–156, Windows/Linux/Android) and still a Draft proposal.[^toji-bindless][^chrome-155] **Multi-draw indirect** exists only as `chromium-experimental-multi-draw-indirect` behind the unsafe flag.[^status-agent] **`TRANSIENT_ATTACHMENT`** texture usage (must equal `TRANSIENT_ATTACHMENT | RENDER_ATTACHMENT`) is core, shipped in Chrome 146.[^webgpu-spec][^chrome-146]

# WGSL extensions

Enable-extensions (need the matching GPU feature, `enable x;`): `f16`, `clip_distances`, `dual_source_blending`, `subgroups`, `primitive_index`, `subgroup_size_control`, `atomic_vec2u_min_max`.[^wgsl-spec]

Language extensions (no device feature; check `navigator.gpu.wgslLanguageFeatures`, optionally `requires x;`):[^wgsl-spec]

| Language extension | What it gives a SPIR-V→WGSL translator |
| --- | --- |
| `readonly_and_readwrite_storage_textures` | `read`/`read_write` storage textures + `textureBarrier()` (Vulkan storage images) |
| `packed_4x8_integer_dot_product` | `dot4U8Packed`/`dot4I8Packed`, pack/unpack (`shaderIntegerDotProduct`) |
| `unrestricted_pointer_parameters` | pointer params in any address space (helps SPIR-V function lowering) |
| `pointer_composite_access` | `p.m` / `pa[i]` through pointers |
| `uniform_buffer_standard_layout` | std430-style layout in `uniform` buffers (removes the 16-byte array-stride rule that breaks std140-violating SPIR-V) |
| `subgroup_id`, `subgroup_uniformity` | `subgroup_id`/`num_subgroups`; subgroup uniformity scope |
| `texture_and_sampler_let` | `let` of texture/sampler type (helps translating SPIR-V `OpLoad` of images) |
| `texture_formats_tier1` | extra storage texel formats in WGSL |
| `linear_indexing` | `global_invocation_index`, `workgroup_index` |
| `immediate_address_space` | `var<immediate>` (push constants) |
| `fragment_depth` | conservative depth (`depth_mode` less/greater) — temporarily disabled in Chrome 155–156 |
| `buffer_view` | untyped `buffer<N>`/`buffer` + `bufferView` reinterpretation (useful for emulating SPIR-V aliasing/BDA-like access within one binding) |

# Compatibility mode ("compat")

Target hardware: OpenGL ES 3.1 (with `GL_EXT_color_buffer_float`) and Direct3D 11; Chrome shipped it in **146** (2026-03-10) where hardware support exists (Android GLES 3.1 confirmed; Windows D3D11 described as "being explored"). Apple does not plan to ship it; Mozilla has not committed.[^compat-proposal][^chrome-146][^status-agent]

Restrictions when `"core-features-and-limits"` is **not** enabled (from the ED's normative "if device.[[features]] does not contain core-features-and-limits" steps):[^webgpu-spec]

| # | Restriction | Vulkan feature it removes |
| --- | --- | --- |
| 1 | `textureBindingViewDimension` fixed at texture creation; `"2d"` requires 1 layer, `"cube"` requires 6, `"cube-array"` forbidden | `imageCubeArray`; flexible 2D-array vs 2D views |
| 2 | Bind-group texture views must have `baseArrayLayer = 0` and cover all layers | partial-layer views |
| 3 | All bindings of the same texture in a draw must use compatible views (same aspect, mip range, swizzle) | — |
| 4 | All colour targets must share identical blend state and write mask | `independentBlend` |
| 5 | No `dpdxFine`/`dpdyFine`/`fwidthFine` | fine derivatives |
| 6 | No `sample_mask`/`sample_index` builtins; no `@interpolate(linear)`, no `sample` sampling, `flat` only with `either` | `sampleRateShading`, `noperspective` |
| 7 | `depthBiasClamp` must be 0 | `depthBiasClamp` |
| 8 | `vertex_index`/`instance_index` count against `maxVertexAttributes` | — |
| 9 | No `copyTextureToBuffer`/`copyTextureToTexture` of compressed formats; no copies of multisampled textures | — |
| 10 | Depth textures: no `textureLoad`, only comparison samplers | sampling depth as float |
| 11 | Sum of texture+sampler pairs per stage ≤ `maxSampledTexturesPerShaderStage` | — |
| 12 | Lower limits: 4096 textures, 16 KiB UBO, 4 colour attachments, 0 SSBO/storage textures in VS, 4 in FS, 128 invocations, 15 inter-stage vars | see limits table |
| 13 | Several formats lose capabilities: `bgra8unorm-srgb` needs core; integer formats lose multisampling; `rgba16float`/`r32float` lose MSAA; `rg32*` lose storage; srgb `viewFormats` need core | `MUTABLE_FORMAT` srgb aliasing |

Recommendation: the PoC targets **core** only (request the adapter with default `featureLevel`). A compat variant of the Web Vulkan profile is possible later but would drop independent blend, cube arrays and vertex-stage SSBOs, which most Vulkan renderers assume.

# webgpu.h (native C API) — relevance

- `webgpu.h` in `webgpu-native/webgpu-headers` was marked stable on 2025-09-23 (no tagged 1.0 release); implementations are Dawn, Emdawnwebgpu and (not yet the stable revision) wgpu-native.[^webgpu-headers]
- Emscripten's built-in `-sUSE_WEBGPU` was deprecated in 4.0.10 and removed in 4.0.18; the replacement is Dawn's `--use-port=emdawnwebgpu`, which implements `webgpu.h` over the browser's JS API (handle tables + JS glue).[^emscripten-changelog][^emdawnwebgpu]
- For a managed C# ICD the C API is not needed — the ICD talks to JS. It matters only for the alternative **native ICD** (C/C++ compiled with Emscripten and linked into `dotnet.wasm`), which could reuse emdawnwebgpu's tested JS glue; see [performance notes](/webgpu/performance-and-batching-design.md) and [prior art](/webgpu/prior-art-and-related-layers.md) (nikki uses exactly this route).

# Open questions

- UNVERIFIED: maximum `maxBindGroups` actually exposed by Chrome/Firefox/Safari adapters on common desktop GPUs (if > 4 is commonly available, the push-constant bind group costs nothing).
- UNVERIFIED: Firefox and Safari support for immediates, `texture-component-swizzle`, `subgroups`, `primitive-index` and the newer WGSL language extensions (MDN BCD lists them as unsupported but may lag).
- UNVERIFIED: whether Chrome's compatibility mode is live on Windows D3D11 as of Chrome 155.

[^webgpu-spec]: WebGPU W3C Editor's Draft, 8 October 2026 (commit 25a5dc4)
[^webgpu-cr]: WebGPU W3C Candidate Recommendation Draft, 15 September 2026
[^wgsl-spec]: WGSL W3C Editor's Draft, 8 October 2026
[^vk-limits]: Vulkan specification, Limits chapter (Required Limits table)
[^compat-proposal]: gpuweb proposal — Compatibility mode (status Merged)
[^chrome-news]: Chrome "What's New in WebGPU" index (posts for Chrome 121–156)
[^chrome-146]: What's New in WebGPU (Chrome 146) — compatibility mode, TRANSIENT_ATTACHMENT
[^chrome-immediates]: Chrome 150 release notes — WebGPU immediates
[^chrome-155]: What's New in WebGPU (Chrome 155–156) — texture-compression-unaligned, bindless preview
[^toji-bindless]: Brandon Jones, "WebGPU bindless" (2026-10-06)
[^webgpu-headers]: webgpu-native/webgpu-headers (webgpu.h marked stable 2025-09-23)
[^emscripten-changelog]: Emscripten ChangeLog (-sUSE_WEBGPU deprecated 4.0.10, removed 4.0.18)
[^emdawnwebgpu]: Dawn emdawnwebgpu package README
[^status-agent]: Browser/feature shipping research compiled 2026-10-16
