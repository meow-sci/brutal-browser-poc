---
type: API Mapping
title: Vulkan → WebGPU object mapping (one row per Vulkan object) with enum translation tables
description: For every Vulkan object a renderer creates, the WebGPU counterpart, what information is needed at creation, lifetime differences and the emulation strategy, plus the enum/flag translation tables (usages, descriptor types, blend, compare, stencil, sampler, topology, load/store) a Vulkan-on-WebGPU ICD needs.
tags: [vulkan, webgpu, icd, object-model, descriptors, pipelines, swapchain, mapping]
sources:
  - id: webgpu-spec
    resource: https://gpuweb.github.io/gpuweb/
    title: WebGPU W3C Editor's Draft, 8 October 2026
  - id: vk-spec
    resource: https://docs.vulkan.org/spec/latest/index.html
    title: Vulkan specification (latest, docs.vulkan.org)
  - id: vk-memory
    resource: https://docs.vulkan.org/spec/latest/chapters/memory.html
    title: Vulkan specification, Memory Allocation chapter
  - id: moltenvk-guide
    resource: https://github.com/KhronosGroup/MoltenVK/blob/main/Docs/MoltenVK_Runtime_UserGuide.md
    title: MoltenVK Runtime User Guide
  - id: moltenvk-config
    resource: https://github.com/KhronosGroup/MoltenVK/blob/main/Docs/MoltenVK_Configuration_Parameters.md
    title: MoltenVK configuration parameters (argument buffers, flip Y)
  - id: vkwebgpu-icd
    resource: https://github.com/Dankular/VkWebGPU-ICD
    title: Dankular/VkWebGPU-ICD (Rust Vulkan ICD over wgpu, 2026)
  - id: nikki
    resource: https://github.com/nihui/nikki
    title: nihui/nikki (compute-only Vulkan subset over WebGPU, 2026)
  - id: brutal-abstractions
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions
    title: Brutal.Vulkan.Abstractions source
  - id: brutal-imgui-vk
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ImGui.Backends/RendererBackends/VulkanBackend.cs
    title: BRUTAL ImGui Vulkan backend
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:13:57Z }
status: draft
---

# Ground rules of the mapping

1. **Handles.** Every Vulkan handle the ICD returns is a 64-bit integer (BRUTAL handles are `nint`) indexing a C#-side object table; objects that have a WebGPU counterpart hold an integer id into a **JS-side handle table** (`Array`/`Map` of `GPU*` objects). No JS object ever crosses into .NET.
2. **Eager vs lazy.** Create the WebGPU object at the Vulkan create call whenever all information is present (buffers, textures, views, samplers, shader source). Defer when WebGPU needs information Vulkan only provides later (bind groups, bind-group layouts, pipeline variants, render pass descriptors).
3. **Immutable vs mutable.** WebGPU bind groups and pipelines are immutable; Vulkan descriptor sets are mutable (an update invalidates command buffers that already bound the set, unless the binding is UPDATE_AFTER_BIND)[^vk-spec] and many Vulkan pipeline properties can be dynamic. Mutable Vulkan state becomes a CPU record + version counter; WebGPU objects are created from it on demand and cached by content hash.
4. **Destruction.** `vkDestroy*` may arrive while submitted GPU work still references the object (Vulkan requires the app to wait first, but the ICD's fences may be "signalled early", see [sync](/webgpu/synchronization-and-async-shape.md)). This is harmless in WebGPU: `GPUBuffer.destroy()`/`GPUTexture.destroy()` act on the device timeline, so work already submitted still completes and the implementation frees memory afterwards; only *later* submissions referencing the object fail validation.[^webgpu-spec] The ICD can therefore destroy immediately, except for objects referenced by command buffers that are recorded but not yet replayed (keep those alive until the command buffer is reset/freed).

# Object table

| Vulkan object | WebGPU counterpart | Created when / needs | Lifetime difference | Emulation notes |
| --- | --- | --- | --- | --- |
| `VkInstance` | `navigator.gpu` + the adapter pre-requested by the JS bootstrap | `vkCreateInstance`: validate extension list | — | Advertise `VK_KHR_surface`, a canvas surface entry point (custom `VK_BRUTAL_web_canvas_surface`-style function or `VK_EXT_headless_surface` meaning "the default canvas"), `VK_EXT_debug_utils`, `VK_KHR_get_physical_device_properties2`, `VK_KHR_portability_enumeration` (accepted so macOS-style init code works). Report 0 layers. `apiVersion` 1.3 (BRUTAL's bindings are 1.3+), features gated. |
| `VkPhysicalDevice` | `GPUAdapter` (+ `adapter.info`, `.features`, `.limits`) | `vkEnumeratePhysicalDevices` → exactly 1 | — | Properties: `vendorID` from `info.vendor` string (nvidia 0x10DE, amd 0x1002, intel 0x8086, apple 0x106B, qualcomm 0x5143, arm 0x13B5, else 0); `deviceType` INTEGRATED unless known discrete, CPU if `info.isFallbackAdapter`; `deviceName` from `info.description` (often empty for privacy); limits per [limits table](/webgpu/webgpu-limits-features-and-compat-mode.md); features per [profile](/webgpu/web-vulkan-profile-proposal.md); formats per [format tables](/webgpu/format-tables.md); one queue family (GRAPHICS\|COMPUTE\|TRANSFER, count 1, `timestampValidBits` 64 if `timestamp-query` else 0, `minImageTransferGranularity` (1,1,1)); memory per [memory model](/webgpu/memory-model-and-mapping-emulation.md); add `VK_KHR_portability_subset` with the WebGPU-shaped feature booleans. |
| `VkDevice` | `GPUDevice` (requested **before .NET starts**, because `requestDevice` is async) | `vkCreateDevice`: check requested features/extensions/queues ⊆ what the pre-created device enabled | WebGPU device can be lost at any time (`device.lost`) | Bootstrap requests every adapter feature that maps to something useful and `requiredLimits` = adapter limits for the ones Vulkan apps care about (bind groups, storage sizes, buffer size). Return `VK_ERROR_FEATURE_NOT_PRESENT` / `VK_ERROR_EXTENSION_NOT_PRESENT` for the rest. |
| `VkQueue` | `GPUQueue` (`device.queue`) | `vkGetDeviceQueue(0, 0)` | single queue | Several requested queues all alias the one `GPUQueue`; ordering is total, which is stronger than Vulkan, so correct apps stay correct. |
| `VkDeviceMemory` | **none** | `vkAllocateMemory` | — | CPU record {size, typeIndex, bound resources by offset, optional shadow bytes in the wasm heap}; Vulkan only obliges the ICD to expose one `DEVICE_LOCAL` type and one `HOST_VISIBLE\|HOST_COHERENT` type;[^vk-memory] see [memory model](/webgpu/memory-model-and-mapping-emulation.md). |
| `VkBuffer` | `GPUBuffer` | `vkCreateBuffer` has size + usage → create immediately (size rounded up to 4) | WebGPU size/usage immutable (same as Vulkan) | Usage table below; always add `COPY_DST` (shadow uploads) and `COPY_SRC` (readback); never `MAP_READ/MAP_WRITE` on app buffers (they cannot combine with other usages).[^webgpu-spec] Memory binding is bookkeeping. Sparse/BDA flags rejected. |
| `VkBufferView` | none in core WebGPU | — | — | Texel buffers: WGSL `texel_buffer` exists only as a Dawn experimental extension; emulate `samplerBuffer`/`imageBuffer` in the shader translator as a storage buffer + manual format decode, or forbid (profile). |
| `VkImage` | `GPUTexture` | `vkCreateImage` has everything (type, format, extent, mips, layers, samples, usage, flags) | — | Dimension: 1D → `"1d"` (no mips, not renderable), 2D → `"2d"` (layers in `depthOrArrayLayers`), 3D → `"3d"`. Samples 1 or 4 only.[^webgpu-spec] `CUBE_COMPATIBLE` needs no flag (compat: `textureBindingViewDimension: "cube"`). `MUTABLE_FORMAT` → `viewFormats` = {format, srgb twin} only. `tiling LINEAR` → reject (report no linear features). `initialLayout`, `sharingMode`, queue families ignored. |
| `VkImageView` | `GPUTextureView` | `vkCreateImageView` → `texture.createView({...})` | — | View type: 1D→`1d`, 2D→`2d`, 2D_ARRAY→`2d-array`, CUBE→`cube`, CUBE_ARRAY→`cube-array` (core only), 3D→`3d`, **1D_ARRAY → unsupported**. Aspect: COLOR→`all`, DEPTH→`depth-only`, STENCIL→`stencil-only`, D\|S→`all`. `VK_REMAINING_*` → omit count. Non-identity `components` → `swizzle` string if `texture-component-swizzle`, else reject (or pre-swizzle data on upload). Render-attachment views must be single-mip; WebGPU also requires single-layer for 2D attachments (or `depthSlice` for 3D). |
| `VkSampler` | `GPUSampler` | `vkCreateSampler` → `createSampler` | — | See sampler table. Border colours, `mirrorClampToEdge`, `unnormalizedCoordinates`, `mipLodBias ≠ 0`, `reductionMode`, YCbCr: no WebGPU equivalent. Anisotropy requires all three filters linear in WebGPU; force linear when `anisotropyEnable`. `maxLod` clamp to 32 (`VK_LOD_CLAMP_NONE` = 1000). |
| `VkSamplerYcbcrConversion` | none | — | — | unsupported (video uses `importExternalTexture`). |
| `VkShaderModule` | (`GPUShaderModule`, created later) | `vkCreateShaderModule` gets SPIR-V | Vulkan module can be destroyed right after pipeline creation (BRUTAL `ShaderStages` does)[^brutal-abstractions] | Store SPIR-V (or its hash → prebuilt WGSL from the asset pipeline, see `/shaders/`). Create the `GPUShaderModule` at pipeline creation because the WGSL depends on the pipeline layout (binding remap, push-constant lowering, Y-flip, specialization). Cache modules by (SPIR-V hash, entry point, remap key). Specialization constants → WGSL `override` with numeric `@id(n)` → `GPUProgrammableStage.constants` (`"n": value`). |
| `VkDescriptorSetLayout` | (`GPUBindGroupLayout`, built lazily) | `vkCreateDescriptorSetLayout` | — | Vulkan layouts lack WebGPU-required detail: texture `sampleType` (`float`/`unfilterable-float`/`depth`/`sint`/`uint`), `viewDimension`, `multisampled`, storage-texture `format`+`access`, sampler `type` (`filtering`/`non-filtering`/`comparison`). Fill these from **shader reflection** of each pipeline that uses the set; build the `GPUBindGroupLayout` from the merged descriptor and cache by hash (structurally identical layouts are interchangeable — "group-equivalent").[^webgpu-spec] Binding numbers remapped (combined samplers, arrays) — see descriptor notes. Immutable samplers: store, inject into bind groups. |
| `VkDescriptorPool` | none | `vkCreateDescriptorPool` | — | CPU allocator; `vkResetDescriptorPool` frees all sets (bind-group cache entries dropped lazily). |
| `VkDescriptorSet` | (`GPUBindGroup`, built lazily) | `vkAllocateDescriptorSets` | Vulkan sets are **mutable** until bound/used; WebGPU bind groups immutable | CPU record: per binding/element the resource ids + ranges, plus a version counter bumped by `vkUpdateDescriptorSets`, `vkUpdateDescriptorSetWithTemplate`, copies. Build the `GPUBindGroup` when the ICD replays a draw/dispatch and knows the pipeline's `GPUBindGroupLayout`; cache by (layout id, set id, version). VkWebGPU-ICD also maps sets to bind groups.[^vkwebgpu-icd] |
| `VkPipelineLayout` | `GPUPipelineLayout` | `vkCreatePipelineLayout`: set layouts + push-constant ranges | — | `bindGroupLayouts` = set layouts (lazily materialized) + optionally one ICD-owned group for push constants / driver uniforms when immediates are unavailable or too small. `immediateSize` = push-constant size when ≤ `maxImmediateSize` (64) and immediates are supported. Max 4 bind groups total in default limits. |
| `VkPipelineCache` | none | — | — | No-op; `vkGetPipelineCacheData` returns a valid header with no payload. (MoltenVK stores translated MSL in its cache; an ICD could store translated WGSL.)[^moltenvk-guide] |
| `VkPipeline` (graphics) | `GPURenderPipeline` (one per variant) | `vkCreateGraphicsPipelines` — synchronous `device.createRenderPipeline()` | WebGPU bakes more state | Needs: stages (WGSL), vertex input, topology (+ `stripIndexFormat` for strips, known only at draw → variant), rasterization (cull, front face 1:1 — see [coordinates](/webgpu/coordinate-systems-and-conventions.md), depth bias, `depthClampEnable` → `unclippedDepth` with `depth-clip-control`), multisample (count 1/4, mask, alpha-to-coverage; no `minSampleShading`, no alpha-to-one), depth-stencil (format from render pass, compare, write, single read/write mask for both faces, no depth bounds), colour targets (formats from render pass subpass or `VkPipelineRenderingCreateInfo`, blend, write mask; no logic op). **Dynamic states** WebGPU cannot change at draw time (cull mode, front face, primitive topology, depth test/write/compare, stencil ops/masks, depth bias values, line width, blend enable) → lazily created pipeline **variants** keyed by the dynamic-state values. Tessellation/geometry stages → reject. |
| `VkPipeline` (compute) | `GPUComputePipeline` | `vkCreateComputePipelines` → `createComputePipeline` | — | Workgroup size from the shader; `dispatchWorkgroups` limits apply. |
| `VkRenderPass` | none (`GPURenderPassDescriptor` built at begin) | `vkCreateRenderPass(2)`: CPU record | — | Only **one subpass** maps directly; multi-subpass passes are split into one WebGPU pass per subpass (input attachments become sampled textures in the next pass, which is semantically correct but loses tile memory). Attachment load/store/resolve ops → per-pass descriptor. Subpass dependencies ignored. Layouts ignored. |
| `VkFramebuffer` | none | `vkCreateFramebuffer`: list of image views | Vulkan framebuffer references fixed views; swapchain views change every frame in WebGPU | Store view ids; resolve to `GPUTextureView`s at `vkCmdBeginRenderPass` **replay** time so swapchain proxies pick up the current canvas texture. Imageless framebuffers trivially supported. |
| (dynamic rendering) | `GPURenderPassDescriptor` | `vkCmdBeginRendering` | — | Closest Vulkan model to WebGPU; preferred for new code. |
| `VkCommandPool` | none | — | — | CPU arena for command-stream bytes; reset = rewind. |
| `VkCommandBuffer` (primary) | `GPUCommandEncoder` → `GPUCommandBuffer` (created at **submit**) | `vkAllocateCommandBuffers` | WebGPU command buffers are single-use; Vulkan ones can be resubmitted (unless ONE_TIME_SUBMIT) | Record into a byte stream in wasm memory; replay into a fresh `GPUCommandEncoder` at every `vkQueueSubmit` (so resubmission works). nikki and VkWebGPU-ICD both record-then-replay at submit.[^nikki][^vkwebgpu-icd] See [command mapping](/webgpu/vulkan-to-webgpu-command-mapping.md). |
| `VkCommandBuffer` (secondary) | `GPURenderBundle` (or inline) | — | bundles cannot set viewport/scissor/blend constant/stencil ref and clear pass state after execution[^webgpu-spec] | Simplest: **inline** the secondary's stream at `vkCmdExecuteCommands` (VkWebGPU-ICD does this).[^vkwebgpu-icd] Optimization: encode a `GPURenderBundle` once for secondaries recorded with `RENDER_PASS_CONTINUE` and no dynamic viewport/scissor commands. |
| `VkFence` | none (CPU object) | — | — | Signalled at submit (upload-only work) or when `queue.onSubmittedWorkDone()`/a readback `mapAsync` resolves; see [sync](/webgpu/synchronization-and-async-shape.md). |
| `VkSemaphore` (binary) | none | — | — | No-op: WebGPU has one queue with implicit ordering. MoltenVK's "single queue, rely on implicit submission order" style is the same idea.[^moltenvk-config] |
| `VkSemaphore` (timeline) | none | — | — | CPU counter; signal values applied at submit (upload-only) or on completion; host waits cannot block. |
| `VkEvent` | none | — | — | `vkCmdSetEvent/WaitEvents` → no-op (WebGPU orders everything); host `vkGetEventStatus` → CPU flag. Report portability `events = true` only if this weak emulation is acceptable. |
| `VkQueryPool` (occlusion) | `GPUQuerySet` type `"occlusion"` | `vkCreateQueryPool` | WebGPU occlusion queries are per render pass (`occlusionQuerySet` in the pass descriptor) | Pass descriptor must list the query set; `vkCmdBeginQuery` inside the pass → `beginOcclusionQuery(index)`. Results via `resolveQuerySet` into a buffer (offset 256-aligned) + async readback. |
| `VkQueryPool` (timestamp) | `GPUQuerySet` type `"timestamp"` (`timestamp-query`) | — | only at pass begin/end via `timestampWrites` | `vkCmdWriteTimestamp` → nearest pass boundary (split pass if needed); precision quantized. |
| `VkQueryPool` (pipeline statistics) | none | — | — | unsupported. |
| `VkSurfaceKHR` | `GPUCanvasContext` (`canvas.getContext("webgpu")`) | ICD-specific create call naming a canvas (selector or `OffscreenCanvas` id) | — | Surface support query → true for queue family 0. |
| `VkSwapchainKHR` | `context.configure({...})` + N proxy images | `vkCreateSwapchainKHR` | WebGPU gives one texture per frame, not a ring | `vkGetSwapchainImagesKHR` returns N proxy `VkImage`s; `vkAcquireNextImageKHR` calls `getCurrentTexture()` and binds it to proxy `i = (i+1) % N`; views of proxies are late-bound; `vkQueuePresentKHR` = end-of-frame marker. Recreate on canvas resize (`VK_ERROR_OUT_OF_DATE_KHR`). See [browser status](/webgpu/webgpu-status-and-browser-support-2026.md#canvas-configuration-and-presentation-swapchain-semantics). |
| `VkDescriptorUpdateTemplate` | none | — | — | Easy: template = list of (binding, element, count, offset, stride) → apply as `vkUpdateDescriptorSets`. |
| `VkPrivateDataSlot` | none | — | — | Dictionary keyed by (object handle, slot). |
| `VkDebugUtilsMessengerEXT` | `device.onuncapturederror` / error scopes | — | errors are asynchronous | Forward messages (severity ERROR, type VALIDATION) with the WebGPU message; object names from `vkSetDebugUtilsObjectNameEXT` become `label`s so messages are readable. |
| `VkAccelerationStructureKHR`, ray-tracing pipelines | none | — | — | unsupported. |

# Descriptor types

| `VkDescriptorType` | WebGPU binding layout entry | Notes |
| --- | --- | --- |
| `SAMPLER` | `sampler: {type}` (`filtering` default; `comparison` if the sampler has `compareEnable`; `non-filtering` if all filters nearest and the shader samples an unfilterable texture) | |
| `SAMPLED_IMAGE` | `texture: {sampleType, viewDimension, multisampled}` | sample type from shader reflection / view format |
| `COMBINED_IMAGE_SAMPLER` | **two** entries: `texture` + `sampler` | needs a binding-remap convention shared with the shader translator, e.g. texture at `2b`, sampler at `2b+1`; VkWebGPU-ICD splits combined samplers in a SPIR-V pre-pass.[^vkwebgpu-icd] BRUTAL's ImGui backend uses this type.[^brutal-imgui-vk] |
| `STORAGE_IMAGE` | `storageTexture: {access, format, viewDimension}` | `access` from SPIR-V `NonReadable`/`NonWritable` decorations; `read-write` needs WGSL `readonly_and_readwrite_storage_textures` and format support |
| `UNIFORM_BUFFER` | `buffer: {type: "uniform"}` | offset 256-aligned; size ≤ `maxUniformBufferBindingSize` |
| `UNIFORM_BUFFER_DYNAMIC` | `buffer: {type: "uniform", hasDynamicOffset: true}` | dynamic offsets ordered by binding within each group in both APIs; preserved if the remap keeps binding order monotonic |
| `STORAGE_BUFFER` | `buffer: {type: "storage"}` or `"read-only-storage"` (if the SPIR-V block is `NonWritable`) | read-only variant needed for vertex-stage use in many cases |
| `STORAGE_BUFFER_DYNAMIC` | as above + `hasDynamicOffset` | ≤ 4 per layout |
| `UNIFORM_TEXEL_BUFFER` / `STORAGE_TEXEL_BUFFER` | none | emulate via storage buffer + shader decode, or forbid |
| `INPUT_ATTACHMENT` | `texture` (sampled with `textureLoad` at `position.xy`) | only valid if the producing subpass ended (pass split) |
| `INLINE_UNIFORM_BLOCK` | `buffer: {type: "uniform"}` backed by an ICD-owned buffer | contents written with `writeBuffer` |
| `ACCELERATION_STRUCTURE_KHR`, `MUTABLE_EXT`, `SAMPLE_WEIGHT_IMAGE_QCOM`, ... | none | unsupported |

Descriptor arrays (`descriptorCount > 1`): core WebGPU has no binding arrays (only the Chrome-preview resource tables), so arrays must be **flattened** to consecutive bindings by the translator, and every element counts against the per-stage limits (16 sampled textures).[^webgpu-spec] `UPDATE_AFTER_BIND`/`PARTIALLY_BOUND`/variable-count bindings: unsupported (bind groups must be complete).

# Translation tables

## Buffer usage

| `VkBufferUsageFlags` | `GPUBufferUsage` |
| --- | --- |
| `TRANSFER_SRC` | `COPY_SRC` |
| `TRANSFER_DST` | `COPY_DST` |
| `UNIFORM_BUFFER` | `UNIFORM` |
| `STORAGE_BUFFER` | `STORAGE` |
| `INDEX_BUFFER` | `INDEX` |
| `VERTEX_BUFFER` | `VERTEX` |
| `INDIRECT_BUFFER` | `INDIRECT` |
| (query result destination) | `QUERY_RESOLVE` on an internal buffer, then `copyBufferToBuffer` |
| `UNIFORM/STORAGE_TEXEL_BUFFER` | `STORAGE` (if emulated) |
| `SHADER_DEVICE_ADDRESS`, `ACCELERATION_STRUCTURE_*`, `TRANSFORM_FEEDBACK_*`, `CONDITIONAL_RENDERING` | reject |
| (always added by ICD) | `COPY_DST \| COPY_SRC` |

## Image usage

| `VkImageUsageFlags` | `GPUTextureUsage` |
| --- | --- |
| `TRANSFER_SRC` / `TRANSFER_DST` | `COPY_SRC` / `COPY_DST` |
| `SAMPLED` | `TEXTURE_BINDING` |
| `STORAGE` | `STORAGE_BINDING` |
| `COLOR_ATTACHMENT`, `DEPTH_STENCIL_ATTACHMENT` | `RENDER_ATTACHMENT` |
| `TRANSIENT_ATTACHMENT` | `TRANSIENT_ATTACHMENT` (only if the full usage is exactly transient + attachment; otherwise drop the hint)[^webgpu-spec] |
| `INPUT_ATTACHMENT` | `TEXTURE_BINDING` |
| (ICD adds) | `COPY_SRC` for blit/readback emulation, `RENDER_ATTACHMENT` for images that are blit/clear destinations |

## Sampler state

| Vulkan | WebGPU |
| --- | --- |
| `magFilter`/`minFilter` NEAREST/LINEAR | `"nearest"`/`"linear"` |
| `mipmapMode` NEAREST/LINEAR | `mipmapFilter` `"nearest"`/`"linear"` |
| `addressMode` REPEAT / MIRRORED_REPEAT / CLAMP_TO_EDGE | `"repeat"` / `"mirror-repeat"` / `"clamp-to-edge"` |
| `CLAMP_TO_BORDER` | none → `"clamp-to-edge"` + warning, or shader emulation (see [gaps](/webgpu/semantic-gaps-and-emulation.md)) |
| `MIRROR_CLAMP_TO_EDGE` | none |
| `anisotropyEnable`, `maxAnisotropy` | `maxAnisotropy` (clamped ≤ 16; requires all filters linear) |
| `compareEnable`, `compareOp` | `compare` (sampler becomes a comparison sampler) |
| `minLod`, `maxLod` | `lodMinClamp`, `lodMaxClamp` (≤ 32) |
| `mipLodBias` | none (WGSL `textureSampleBias` in shader only) |
| `borderColor`, `unnormalizedCoordinates`, reduction mode | none |

## Pipeline enums

| Vulkan | WebGPU |
| --- | --- |
| `VkPrimitiveTopology` POINT_LIST / LINE_LIST / LINE_STRIP / TRIANGLE_LIST / TRIANGLE_STRIP | `point-list` / `line-list` / `line-strip` / `triangle-list` / `triangle-strip` |
| TRIANGLE_FAN, *_WITH_ADJACENCY, PATCH_LIST | none (fans: CPU/compute index conversion; others reject) |
| `VkCullModeFlags` NONE / FRONT / BACK / FRONT_AND_BACK | `none` / `front` / `back` / (FRONT_AND_BACK: draw nothing — skip draws for triangles, still rasterize points/lines) |
| `VkFrontFace` CCW / CW | `ccw` / `cw` (1:1, with shader Y-flip) |
| `VkPolygonMode` FILL | (default) — LINE/POINT unsupported |
| `VkCompareOp` NEVER, LESS, EQUAL, LESS_OR_EQUAL, GREATER, NOT_EQUAL, GREATER_OR_EQUAL, ALWAYS | `never`, `less`, `equal`, `less-equal`, `greater`, `not-equal`, `greater-equal`, `always` |
| `VkStencilOp` KEEP, ZERO, REPLACE, INCREMENT_AND_CLAMP, DECREMENT_AND_CLAMP, INVERT, INCREMENT_AND_WRAP, DECREMENT_AND_WRAP | `keep`, `zero`, `replace`, `increment-clamp`, `decrement-clamp`, `invert`, `increment-wrap`, `decrement-wrap` |
| stencil `compareMask`/`writeMask` per face | one `stencilReadMask`/`stencilWriteMask` for both faces (portability `separateStencilMaskRef = false`) |
| stencil `reference` per face | one `setStencilReference` value |
| `VkBlendFactor` ZERO, ONE, SRC_COLOR, ONE_MINUS_SRC_COLOR, DST_COLOR, ONE_MINUS_DST_COLOR, SRC_ALPHA, ONE_MINUS_SRC_ALPHA, DST_ALPHA, ONE_MINUS_DST_ALPHA, CONSTANT_COLOR, ONE_MINUS_CONSTANT_COLOR, SRC_ALPHA_SATURATE | `zero`, `one`, `src`, `one-minus-src`, `dst`, `one-minus-dst`, `src-alpha`, `one-minus-src-alpha`, `dst-alpha`, `one-minus-dst-alpha`, `constant`, `one-minus-constant`, `src-alpha-saturated` |
| CONSTANT_ALPHA / ONE_MINUS_CONSTANT_ALPHA | none — emulate by setting the blend constant to (a,a,a,a) when the pipeline uses no CONSTANT_COLOR (portability `constantAlphaColorBlendFactors = false` otherwise) |
| SRC1_COLOR, ONE_MINUS_SRC1_COLOR, SRC1_ALPHA, ONE_MINUS_SRC1_ALPHA | `src1`, `one-minus-src1`, `src1-alpha`, `one-minus-src1-alpha` (`dual-source-blending`) |
| `VkBlendOp` ADD, SUBTRACT, REVERSE_SUBTRACT, MIN, MAX | `add`, `subtract`, `reverse-subtract`, `min`, `max` (WebGPU requires factors `one` for min/max, Vulkan ignores them) |
| advanced blend ops, `logicOpEnable` | none |
| `colorWriteMask` R/G/B/A | `writeMask` RED/GREEN/BLUE/ALPHA |
| `VkAttachmentLoadOp` LOAD / CLEAR / DONT_CARE / NONE | `load` / `clear` / `clear` (cheapest defined choice) / `load` |
| `VkAttachmentStoreOp` STORE / DONT_CARE / NONE | `store` / `discard` (reads back as zero!) / `store` |
| `VkShaderStageFlags` VERTEX / FRAGMENT / COMPUTE | `GPUShaderStage.VERTEX` / `FRAGMENT` / `COMPUTE`; others reject |
| `VkVertexInputRate` VERTEX / INSTANCE | `stepMode` `vertex` / `instance` |
| `VkIndexType` UINT16 / UINT32 | `uint16` / `uint32` |

# Open questions

- UNVERIFIED: whether WebGPU implementations accept identical-but-separately-created `GPUBindGroupLayout`s across pipelines in all browsers without measurable cost (spec says group-equivalent layouts are compatible).
- Binding-remap convention for combined image samplers and arrays must be agreed with the shader tool chain in `/shaders/` (one function used by both the ICD and the translator).
- Whether the PoC should expose `VkEvent` at all (risk of apps relying on host-signalled events).

[^webgpu-spec]: WebGPU W3C Editor's Draft, 8 October 2026
[^vk-spec]: Vulkan specification (latest, docs.vulkan.org)
[^vk-memory]: Vulkan specification, Memory Allocation chapter
[^moltenvk-guide]: MoltenVK Runtime User Guide
[^moltenvk-config]: MoltenVK configuration parameters (argument buffers, flip Y)
[^vkwebgpu-icd]: Dankular/VkWebGPU-ICD (Rust Vulkan ICD over wgpu, 2026)
[^nikki]: nihui/nikki (compute-only Vulkan subset over WebGPU, 2026)
[^brutal-abstractions]: Brutal.Vulkan.Abstractions source
[^brutal-imgui-vk]: BRUTAL ImGui Vulkan backend
