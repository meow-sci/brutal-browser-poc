---
type: Proposal
title: Web Vulkan profile — the Vulkan subset a BRUTAL WebGPU ICD implements (PoC tier and game tier)
description: Proposes two Vulkan Profiles (VP_BRUTAL_web_poc_2026 and VP_BRUTAL_web_game_2026) defining exactly which Vulkan version, extensions, functions, features, limits, formats and app-side conventions a Vulkan-on-WebGPU ICD supports, what is emulated and what is forbidden, with a profile-JSON sketch and a desktop validation workflow using the Khronos profiles layer.
tags: [vulkan-profiles, webgpu, icd, portability-subset, brutal, proposal, conventions]
sources:
  - id: vk-profiles
    resource: https://github.com/KhronosGroup/Vulkan-Profiles
    title: KhronosGroup/Vulkan-Profiles (schema, layer, tools)
  - id: vk-profiles-tutorial
    resource: https://github.com/KhronosGroup/Vulkan-Profiles/blob/main/layer/TUTORIAL.md
    title: Vulkan Profiles layer tutorial
  - id: vk-profiles-sdk
    resource: /Users/asherwin/VulkanSDK/1.4.328.1/macOS/share/vulkan/config/VK_LAYER_KHRONOS_profiles/
    title: Local SDK profile examples (VP_LUNARG_minimum_requirements.json, VP_ANDROID_baseline_2022.json, VP_KHR_roadmap.json)
  - id: vk-profiles-layer-manifest
    resource: /Users/asherwin/VulkanSDK/1.4.328.1/macOS/share/vulkan/explicit_layer.d/VkLayer_khronos_profiles.json
    title: VK_LAYER_KHRONOS_profiles 1.4.328 manifest (settings incl. emulate_portability)
  - id: vk-portability-header
    resource: /Users/asherwin/VulkanSDK/1.4.328.1/macOS/include/vulkan/vulkan_beta.h
    title: VkPhysicalDevicePortabilitySubsetFeaturesKHR / PropertiesKHR (vulkan_beta.h)
  - id: vk-limits
    resource: https://docs.vulkan.org/spec/latest/chapters/limits.html
    title: Vulkan specification, Limits chapter
  - id: webgpu-spec
    resource: https://gpuweb.github.io/gpuweb/
    title: WebGPU W3C Editor's Draft, 8 October 2026
  - id: nikki-profile
    resource: https://raw.githubusercontent.com/nihui/nikki/main/docs/vulkan-profile.md
    title: nikki "Restricted API compute profile"
  - id: zink
    resource: https://docs.mesa3d.org/drivers/zink.html
    title: Mesa Zink documentation (VP_ZINK_requirements.json)
  - id: dozen
    resource: https://gitlab.freedesktop.org/mesa/mesa/-/raw/main/src/microsoft/vulkan/dzn_device.c
    title: Mesa Dozen device source (non-conformant Vulkan 1.2)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:13:57Z }
status: draft
---

# Why a profile

A Vulkan-on-WebGPU ICD can never be conformant (WebGPU lacks BDA, geometry, polygon modes, ...). Precedents show the honest way to ship such a layer: MoltenVK reports `VK_KHR_portability_subset`; Dozen advertises Vulkan 1.2 and warns it is non-conformant;[^dozen] Zink publishes its requirements as a Vulkan Profiles JSON;[^zink] nikki documents a "restricted API compute profile".[^nikki-profile] A **Vulkan Profiles JSON** lets BRUTAL developers validate their desktop build against exactly what the browser ICD will offer, using the Khronos profiles layer plus the validation layer, *before* running in a browser.[^vk-profiles][^vk-profiles-tutorial]

Two tiers:

| Tier | Name | Purpose |
| --- | --- | --- |
| PoC | `VP_BRUTAL_web_poc_2026` | what the first ICD implements: core WebGPU defaults only, no optional WebGPU features, minimal emulation |
| Game | `VP_BRUTAL_web_game_2026` | adds ICD emulations (combined samplers, push-constant ring, pipeline variants for dynamic state, readback, queries, triangle fans, ...) and **optional** capabilities gated on WebGPU features (BC/ETC2/ASTC, f16, clip distances, dual-source blend, subgroups, depth clamp, swizzle, timestamps) via the profile `optionals` mechanism |

# API version and extensions

> **Superseded 2026-10-16:** the ICD and `profiles/VP_BRUTAL_web.json` advertise **1.3** (non-conformant) because BRUTAL calls the 1.3 core entry points and the Khronos profiles layer resolves core names only under a 1.3 profile; see `decisions/architecture-options.md`. The text below is the original 1.2 rationale.

**Reported `apiVersion`: 1.2** (non-conformant). Rationale: Vulkan 1.3 makes `bufferDeviceAddress`, `vulkanMemoryModel`, `inlineUniformBlock`, `dynamicRendering`, `synchronization2`, `maintenance4` etc. mandatory; BDA is impossible on WebGPU, so claiming 1.3 would be a lie the validation layer cannot help with. The useful 1.3 functionality is exposed through its KHR extensions, whose entry points alias the core names.

| Kind | PoC | Game |
| --- | --- | --- |
| Instance extensions | `VK_KHR_surface`, `VK_KHR_get_physical_device_properties2`, `VK_KHR_portability_enumeration`, `VK_EXT_debug_utils`, canvas-surface extension (custom, e.g. `VK_BRUTAL_web_surface`) | + `VK_KHR_get_surface_capabilities2`, `VK_EXT_swapchain_colorspace` (HDR canvas, optional) |
| Device extensions | `VK_KHR_swapchain`, `VK_KHR_portability_subset`, `VK_KHR_dynamic_rendering`, `VK_KHR_synchronization2`, `VK_KHR_maintenance1..4` (trivial parts) | + `VK_KHR_push_descriptor`, `VK_KHR_descriptor_update_template` (core 1.1 anyway), `VK_EXT_extended_dynamic_state(2)` (variants), `VK_KHR_pipeline_creation_cache_control`-style `VK_PIPELINE_COMPILE_REQUIRED` (core 1.3 enum), `VK_EXT_depth_clip_enable` (if `depth-clip-control`), `VK_KHR_shader_float16_int8` (f16 only), `VK_EXT_subgroup_size_control` (if available) |
| Layers | none (0 layers enumerated) | none |

# Features

`VkPhysicalDeviceFeatures` (PoC = **P**, Game = **G**, G? = optional, gated on the named WebGPU feature):

| Feature | Value | Feature | Value |
| --- | --- | --- | --- |
| robustBufferAccess | P (WebGPU is robust) | fullDrawIndexUint32 | P |
| imageCubeArray | P | independentBlend | P |
| geometryShader, tessellationShader | false | sampleRateShading | false |
| dualSrcBlend | G? `dual-source-blending` | logicOp | false |
| multiDrawIndirect | G (CPU loop) | drawIndirectFirstInstance | G? `indirect-first-instance` |
| depthClamp | G? `depth-clip-control` | depthBiasClamp | P (core) |
| fillModeNonSolid, depthBounds, wideLines, largePoints, alphaToOne, multiViewport | false | samplerAnisotropy | P |
| textureCompressionBC / ETC2 / ASTC_LDR | G? per adapter | occlusionQueryPrecise | false |
| pipelineStatisticsQuery | false | vertexPipelineStoresAndAtomics | P (core) |
| fragmentStoresAndAtomics | P | shaderTessellationAndGeometryPointSize | false |
| shaderImageGatherExtended | false | shaderStorageImageExtendedFormats | G? `texture-formats-tier1` |
| shaderStorageImageMultisample, ReadWithoutFormat, WriteWithoutFormat | false | shader*ArrayDynamicIndexing | false |
| shaderClipDistance | G? `clip-distances` | shaderCullDistance | false |
| shaderFloat64, shaderInt64, shaderInt16 | false | shaderResourceResidency, shaderResourceMinLod, sparse* | false |
| variableMultisampleRate, inheritedQueries | false | | |

Vulkan 1.1/1.2 feature structs: `multiview` false; `shaderDrawParameters` G (immediates-based) else false; `samplerYcbcrConversion` false; `storageBuffer16BitAccess` false; `shaderFloat16` G? `shader-f16`; `shaderInt8` false; descriptor indexing (all) **false**; `samplerFilterMinmax` false; `scalarBlockLayout` false; `imagelessFramebuffer` P; `uniformBufferStandardLayout` G? (WGSL `uniform_buffer_standard_layout`); `shaderSubgroupExtendedTypes` false; `separateDepthStencilLayouts` P; `hostQueryReset` P; `timelineSemaphore` G (CPU emulation, non-blocking waits); `bufferDeviceAddress` **false**; `vulkanMemoryModel` false; `shaderOutputViewportIndex`/`Layer` false. Extension features: `dynamicRendering` P, `synchronization2` P, `maintenance4` P, `extendedDynamicState(2)` G, `shaderDemoteToHelperInvocation` P (WGSL `discard`), `shaderTerminateInvocation` P, `pipelineCreationCacheControl` G, `robustImageAccess` P, `shaderZeroInitializeWorkgroupMemory` P.

`VK_KHR_portability_subset` booleans (all fifteen members of the struct):[^vk-portability-header]

| Member | PoC | Game | Why |
| --- | --- | --- | --- |
| constantAlphaColorBlendFactors | false | true (constant-splat emulation, partial) | `constant` is RGBA only |
| events | false | true (CPU emulation) | no WebGPU events |
| imageViewFormatReinterpretation | false | false | `viewFormats` only differ by `-srgb` |
| imageViewFormatSwizzle | false | G? `texture-component-swizzle` | |
| imageView2DOn3DImage | false | false | |
| multisampleArrayImage | false | false | |
| mutableComparisonSamplers | false | false | WebGPU comparison samplers are a distinct binding type |
| pointPolygons | false | false | no polygon modes |
| samplerMipLodBias | false | false | no sampler LOD bias |
| separateStencilMaskRef | false | false | one mask/reference for both faces |
| shaderSampleRateInterpolationFunctions | false | false | conservative (WGSL has per-sample interpolation, but not the GLSL `interpolateAt*` functions) (UNVERIFIED mapping) |
| tessellationIsolines, tessellationPointMode | false | false | no tessellation |
| triangleFans | false | true (index conversion) | |
| vertexAttributeAccessBeyondStride | false | false | WebGPU validates `offset + size ≤ arrayStride`[^webgpu-spec] |
| `minVertexInputBindingStrideAlignment` (property) | 4 | 4 | |

# Limits (both tiers unless noted; values = WebGPU core defaults, see [limits table](/webgpu/webgpu-limits-features-and-compat-mode.md))

| Limit | Value | Limit | Value |
| --- | --- | --- | --- |
| maxImageDimension1D/2D/Cube | 8192 | maxImageDimension3D | 2048 |
| maxImageArrayLayers | 256 | maxFramebufferWidth/Height | 8192 |
| maxFramebufferLayers | 1 | maxViewports | 1 |
| maxBoundDescriptorSets | 4 (**3 if push constants > 64 B**) | maxPushConstantsSize | 64 PoC (immediates) / 128 Game (ring) |
| maxPerStageDescriptorSamplers | 16 | maxPerStageDescriptorSampledImages | 16 |
| maxPerStageDescriptorUniformBuffers | 12 | maxPerStageDescriptorStorageBuffers | 8 |
| maxPerStageDescriptorStorageImages | 4 | maxPerStageDescriptorInputAttachments | 0 PoC / 4 Game |
| maxDescriptorSetUniformBuffersDynamic | 8 (7 with ring) | maxDescriptorSetStorageBuffersDynamic | 4 |
| maxUniformBufferRange | 65536 | maxStorageBufferRange | 134217728 |
| minUniformBufferOffsetAlignment | 256 | minStorageBufferOffsetAlignment | 256 |
| maxVertexInputBindings | **8** | maxVertexInputAttributes | 16 |
| maxVertexInputBindingStride | 2048 | maxVertexInputAttributeOffset | 2047 |
| maxVertexOutputComponents / maxFragmentInputComponents | 64 | maxColorAttachments | 8 (and ≤ 32 B/sample in total — not expressible as a Vulkan limit) |
| maxComputeSharedMemorySize | 16384 | maxComputeWorkGroupInvocations | 256 |
| maxComputeWorkGroupSize | (256, 256, 64) | maxComputeWorkGroupCount | (65535, 65535, 65535) |
| framebuffer/sampledImage*SampleCounts | 1 \| 4 | maxSamplerAnisotropy | 16 |
| maxSamplerLodBias | 0 | maxClipDistances | 0 PoC / 8 Game? |
| maxDrawIndirectCount | 1 PoC / 65535 Game (CPU loop) | lineWidthRange, pointSizeRange | [1, 1] |
| maxMemoryAllocationCount | 4096 | bufferImageGranularity | 1 |
| nonCoherentAtomSize | 4 | optimalBufferCopyRowPitchAlignment | 256 |

Several values are **below Vulkan's required minimums** (`maxVertexInputBindings` 16, `maxPushConstantsSize` 128 for PoC, `maxFramebufferLayers` 256, `maxSamplerLodBias` 2) — another reason the profile is explicitly non-conformant.[^vk-limits]

# Formats

Guaranteed in both tiers (optimal tiling): `R8_UNORM`, `R8G8_UNORM`, `R8G8B8A8_UNORM/SRGB/SNORM/UINT/SINT`, `B8G8R8A8_UNORM/SRGB`, `R16_SFLOAT`, `R16G16_SFLOAT`, `R16G16B16A16_SFLOAT`, `R32_UINT/SINT/SFLOAT`, `R32G32_*`, `R32G32B32A32_*`, `A2B10G10R10_UNORM_PACK32`, `B10G11R11_UFLOAT_PACK32` (sampled), `E5B9G9R9_UFLOAT_PACK32` (sampled), `D16_UNORM`, `X8_D24_UNORM_PACK32`, `D24_UNORM_S8_UINT`, `D32_SFLOAT`, `S8_UINT`, with exactly the feature bits of [format tables](/webgpu/format-tables.md). Game tier optional: BC/ETC2/ASTC families, `D32_SFLOAT_S8_UINT`, 16-bit UNORM/SNORM (tier1), filterable/blendable 32-bit float. Vertex formats: the 42 WebGPU vertex formats only.

# Function set

PoC tier implements the ~105 commands listed in the [ICD surface analysis](/webgpu/brutal-abstractions-icd-surface.md): instance/physical-device queries, device/queue, memory (allocate/map/unmap/flush/invalidate/bind), buffers, images, views, samplers, shader modules, descriptor set layouts/pools/sets/updates, pipeline layouts, graphics/compute pipelines, render passes/framebuffers (single subpass) and dynamic rendering, command pools/buffers, fences/semaphores, surface/swapchain, debug utils, and the commands `vkCmdBeginRenderPass/EndRenderPass`, `vkCmdBeginRendering/EndRendering`, `vkCmdBindPipeline`, `vkCmdBindDescriptorSets`, `vkCmdPushConstants`, `vkCmdBindVertexBuffers`, `vkCmdBindIndexBuffer`, `vkCmdSetViewport/Scissor/BlendConstants/StencilReference`, `vkCmdDraw(Indexed)(Indirect)`, `vkCmdDispatch(Indirect)`, `vkCmdCopyBuffer`, `vkCmdCopyBufferToImage`, `vkCmdCopyImageToBuffer`, `vkCmdCopyImage`, `vkCmdFillBuffer` (zero), `vkCmdPipelineBarrier(2)` (no-op), `vkCmdExecuteCommands` (inline), debug labels.

Game tier adds: `vkCmdBlitImage`, `vkCmdResolveImage`, `vkCmdClearColorImage`, `vkCmdClearDepthStencilImage`, `vkCmdClearAttachments`, `vkCmdUpdateBuffer`, non-zero `vkCmdFillBuffer`, `vkCmdNextSubpass`, queries (occlusion, timestamps at pass boundaries, `vkCmdCopyQueryPoolResults`, `vkGetQueryPoolResults`), events, timeline semaphores, push descriptors, extended dynamic state, multi-draw indirect loop, triangle fans, readback.

# Forbidden in both tiers

Geometry/tessellation/mesh shaders; ray tracing and acceleration structures; buffer device address; descriptor indexing/bindless (`UPDATE_AFTER_BIND`, `PARTIALLY_BOUND`, runtime arrays, non-uniform indexing); descriptor buffers; sparse resources; multiview; logic ops; polygon modes other than FILL; line widths/point sizes ≠ 1; depth bounds; per-face stencil masks/references; 64-bit types and atomics; 16-bit integers; image atomics; memory aliasing; linear-tiled images; MSAA counts other than 1/4; view-format reinterpretation beyond sRGB; 1D-array and 2D-on-3D views; 3-component and packed-16-bit texel/vertex formats; `*SCALED` vertex formats; negative viewport heights (PoC) and reversed depth ranges; read/write hazards or feedback loops inside one render pass; blocking waits on readback fences; transform feedback; conditional rendering; indirect-count draws; pipeline statistics queries.

# Required app-side conventions

| Area | Convention |
| --- | --- |
| Shaders | separate `texture*` and `sampler` objects (PoC; Game tier splits combined samplers itself); no descriptor arrays; push constants ≤ 64 B (PoC) / ≤ 128 B (Game); std140 uniform layout; binding numbers < 500; no `gl_PointSize ≠ 1`, `gl_Layer`, `gl_ViewportIndex`, `gl_CullDistance`; no derivatives/implicit-LOD sampling in non-uniform control flow; shaders compiled to SPIR-V 1.0–1.3 (Vulkan 1.1 env) for the translator (see `/shaders/`) |
| Descriptor sets | ≤ 4 sets, **≤ 3 if push constants exceed 64 B**; dynamic offsets 256-aligned (query the limit, never hard-code); ≤ 8 dynamic UBOs, ≤ 4 dynamic SSBOs per layout |
| Buffers | sizes multiples of 4; copy offsets/sizes multiples of 4; vertex-buffer bind offsets multiples of 4; vertex stride multiple of 4 and ≤ 2048; ≤ 8 vertex bindings, ≤ 16 attributes |
| Images | sample counts 1 or 4; compressed images block-aligned; `storeOp = STORE` for anything read later (`DONT_CARE` reads back as zero); identity swizzles (PoC); no `CLAMP_TO_BORDER` (Game: approximated); anisotropy only with linear filters; `mipLodBias = 0` |
| Copies | prefer `bufferRowLength × texel size` multiple of 256 (others take a slower repack path); no depth-aspect copies of D24 formats; depth uploads via a shader |
| Render passes | single subpass or dynamic rendering; colour attachments ≤ 32 bytes per pixel in total; no attachment also sampled in the same pass (except read-only depth) |
| Memory | no overlapping binds; keep host-visible memory small (it lives in the wasm heap); persistent mapping is fine; flushing explicit ranges is cheaper than relying on coherency |
| Sync | treat fences of upload-only submits as signalled at submit; never wait with a non-zero timeout on a fence guarding GPU→CPU data — poll across frames; one queue |
| Frame loop | render one frame per callback and **return** after `vkQueuePresentKHR`; handle `VK_ERROR_OUT_OF_DATE_KHR` by recreating the swapchain |
| Surface | pick the format from `vkGetPhysicalDeviceSurfaceFormatsKHR` (B8G8R8A8 or R8G8B8A8 ± sRGB); FIFO only; positive-height viewports; `minDepth ≤ maxDepth` |

# Profile JSON sketch

```json
{
  "$schema": "https://schema.khronos.org/vulkan/profiles-0.8-latest.json#",
  "capabilities": {
    "web_core": {
      "extensions": {
        "VK_KHR_swapchain": 1, "VK_KHR_portability_subset": 1,
        "VK_KHR_dynamic_rendering": 1, "VK_KHR_synchronization2": 1
      },
      "features": {
        "VkPhysicalDeviceFeatures": {
          "robustBufferAccess": true, "fullDrawIndexUint32": true, "imageCubeArray": true,
          "independentBlend": true, "depthBiasClamp": true, "samplerAnisotropy": true,
          "vertexPipelineStoresAndAtomics": true, "fragmentStoresAndAtomics": true
        },
        "VkPhysicalDeviceVulkan12Features": {
          "imagelessFramebuffer": true, "separateDepthStencilLayouts": true, "hostQueryReset": true
        },
        "VkPhysicalDeviceDynamicRenderingFeaturesKHR": { "dynamicRendering": true },
        "VkPhysicalDeviceSynchronization2FeaturesKHR": { "synchronization2": true },
        "VkPhysicalDevicePortabilitySubsetFeaturesKHR": {
          "constantAlphaColorBlendFactors": false, "events": false,
          "imageViewFormatReinterpretation": false, "imageViewFormatSwizzle": false,
          "imageView2DOn3DImage": false, "multisampleArrayImage": false,
          "mutableComparisonSamplers": false, "pointPolygons": false,
          "samplerMipLodBias": false, "separateStencilMaskRef": false,
          "shaderSampleRateInterpolationFunctions": false, "tessellationIsolines": false,
          "tessellationPointMode": false, "triangleFans": false,
          "vertexAttributeAccessBeyondStride": false
        }
      },
      "properties": {
        "VkPhysicalDeviceProperties": {
          "limits": {
            "maxImageDimension2D": 8192, "maxImageDimension3D": 2048, "maxImageArrayLayers": 256,
            "maxBoundDescriptorSets": 4, "maxPushConstantsSize": 64,
            "maxPerStageDescriptorSamplers": 16, "maxPerStageDescriptorSampledImages": 16,
            "maxPerStageDescriptorUniformBuffers": 12, "maxPerStageDescriptorStorageBuffers": 8,
            "maxPerStageDescriptorStorageImages": 4,
            "maxUniformBufferRange": 65536, "maxStorageBufferRange": 134217728,
            "minUniformBufferOffsetAlignment": 256, "minStorageBufferOffsetAlignment": 256,
            "maxVertexInputBindings": 8, "maxVertexInputAttributes": 16,
            "maxVertexInputBindingStride": 2048, "maxColorAttachments": 8,
            "maxComputeSharedMemorySize": 16384, "maxComputeWorkGroupInvocations": 256,
            "maxViewports": 1, "maxSamplerLodBias": 0.0,
            "framebufferColorSampleCounts": ["VK_SAMPLE_COUNT_1_BIT", "VK_SAMPLE_COUNT_4_BIT"]
          }
        },
        "VkPhysicalDevicePortabilitySubsetPropertiesKHR": { "minVertexInputBindingStrideAlignment": 4 }
      },
      "formats": {
        "VK_FORMAT_R8G8B8A8_UNORM": { "VkFormatProperties": { "optimalTilingFeatures": [
          "VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT", "VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT",
          "VK_FORMAT_FEATURE_COLOR_ATTACHMENT_BIT", "VK_FORMAT_FEATURE_COLOR_ATTACHMENT_BLEND_BIT",
          "VK_FORMAT_FEATURE_STORAGE_IMAGE_BIT", "VK_FORMAT_FEATURE_TRANSFER_SRC_BIT",
          "VK_FORMAT_FEATURE_TRANSFER_DST_BIT" ] } }
      },
      "queueFamiliesProperties": [
        { "VkQueueFamilyProperties": { "queueFlags": ["VK_QUEUE_GRAPHICS_BIT", "VK_QUEUE_COMPUTE_BIT", "VK_QUEUE_TRANSFER_BIT"], "queueCount": 1 } }
      ]
    }
  },
  "profiles": {
    "VP_BRUTAL_web_poc_2026": {
      "version": 1, "api-version": "1.3.0", "status": "ALPHA",
      "label": "BRUTAL Web (WebGPU) PoC profile",
      "description": "Vulkan subset implemented by the BRUTAL Vulkan-on-WebGPU ICD (non-conformant).",
      "capabilities": ["web_core"]
    }
  }
}
```

The game tier adds capabilities `web_game_emulated` (push constants 128, events, triangle fans, timeline semaphores, extended dynamic state) and `optionals` entries per WebGPU feature (`web_bc`, `web_f16`, `web_clip_distances`, ...). Structure follows the SDK's examples (`capabilities` → `extensions`/`features`/`properties`/`formats`/`queueFamiliesProperties`; `profiles` → `version`/`api-version`/`label`/`description`/`capabilities`).[^vk-profiles-sdk] The JSON above is a sketch; validate it against the schema before use.

# Desktop validation workflow

1. Put `VP_BRUTAL_web.json` in a folder; enable layers so the **profiles layer sits nearest the driver** and validation above it (validation then flags anything beyond the simulated device).[^vk-profiles-tutorial]
2. Configure `VK_LAYER_KHRONOS_profiles` (settings from the 1.4.328 manifest): `profile_dirs`, `profile_name = VP_BRUTAL_web_poc_2026`, `simulate_capabilities` = API version + features + properties + formats + extensions, `emulate_portability = true` with the fifteen booleans set as above and `minVertexInputBindingStrideAlignment = 4`, optionally `debug_fail_on_error`.[^vk-profiles-layer-manifest] Via vkconfig, `vk_layer_settings.txt` (`khronos_profiles.<key>`), or environment variables following the layer-settings convention (`VK_KHRONOS_PROFILES_<KEY>` — exact names UNVERIFIED).
3. Run the BRUTAL app on macOS (MoltenVK, which already reports a portability subset) or Windows/Linux; fix every validation error.
4. Rules the layers cannot see (WebGPU-only): 256-byte `bytesPerRow`, 4-byte vertex offsets, 32 B/px colour budget, combined samplers/arrays in SPIR-V, `DONT_CARE` stores that are later read, Y-convention mixing, blocking fence waits on readbacks. Add a debug-only **`WebProfileLint`** in BRUTAL's wrapper layer (or a tiny Vulkan layer) that asserts these, plus offline SPIR-V reflection checks in the shader build.

# Open questions

- ~~Decide `apiVersion` 1.2 (recommended) vs claiming 1.3 non-conformantly~~ Resolved 2026-10-16: 1.3 (BRUTAL uses the 1.3 core entry-point names; see `decisions/architecture-options.md`).
- UNVERIFIED: exact environment-variable names for profiles-layer settings and whether the layer accepts `apiVersion` below the driver's version together with 1.3-era extension structs.
- UNVERIFIED: whether WGSL's sample-interpolation support is enough to set `shaderSampleRateInterpolationFunctions = true` in the game tier.

[^vk-profiles]: KhronosGroup/Vulkan-Profiles (schema, layer, tools)
[^vk-profiles-tutorial]: Vulkan Profiles layer tutorial
[^vk-profiles-sdk]: Local SDK profile examples (VP_LUNARG_minimum_requirements.json, VP_ANDROID_baseline_2022.json, VP_KHR_roadmap.json)
[^vk-profiles-layer-manifest]: VK_LAYER_KHRONOS_profiles 1.4.328 manifest (settings incl. emulate_portability)
[^vk-portability-header]: VkPhysicalDevicePortabilitySubsetFeaturesKHR / PropertiesKHR (vulkan_beta.h)
[^vk-limits]: Vulkan specification, Limits chapter
[^webgpu-spec]: WebGPU W3C Editor's Draft, 8 October 2026
[^nikki-profile]: nikki "Restricted API compute profile"
[^zink]: Mesa Zink documentation (VP_ZINK_requirements.json)
[^dozen]: Mesa Dozen device source (non-conformant Vulkan 1.2)
