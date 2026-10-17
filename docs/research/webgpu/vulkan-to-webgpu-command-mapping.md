---
type: API Mapping
title: Vulkan → WebGPU command mapping (vkCmd*, queue and host commands)
description: For every vkCmd* a typical Vulkan 1.3 renderer records, plus vkQueueSubmit/WaitIdle, memory map/flush and the physical-device/surface queries, the WebGPU encoder/queue call or emulation strategy, the encoder context it needs, alignment traps and cost.
tags: [vulkan, webgpu, icd, commands, render-pass, copies, push-constants, barriers]
sources:
  - id: webgpu-spec
    resource: https://gpuweb.github.io/gpuweb/
    title: WebGPU W3C Editor's Draft, 8 October 2026
  - id: vk-sync
    resource: https://docs.vulkan.org/spec/latest/chapters/synchronization.html
    title: Vulkan specification, Synchronization chapter
  - id: chrome-immediates
    resource: https://developer.chrome.com/release-notes/150
    title: Chrome 150 release notes — WebGPU immediates (setImmediates)
  - id: vkwebgpu-icd
    resource: https://github.com/Dankular/VkWebGPU-ICD
    title: Dankular/VkWebGPU-ICD (ring-buffer push constants, inlined secondaries, no-op barriers)
  - id: moltenvk-config
    resource: https://github.com/KhronosGroup/MoltenVK/blob/main/Docs/MoltenVK_Configuration_Parameters.md
    title: MoltenVK configuration parameters
  - id: brutal-imgui-vk
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ImGui.Backends/RendererBackends/VulkanBackend.cs
    title: BRUTAL ImGui Vulkan backend (texture upload sub-rectangles)
  - id: toji-uploads
    resource: https://toji.dev/webgpu-best-practices/buffer-uploads
    title: Brandon Jones, WebGPU best practices — buffer uploads
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:13:57Z }
status: draft
---

# Execution model of the translation

- `vkCmd*` calls append compact records to a per-`VkCommandBuffer` byte stream in wasm memory (no JS calls while recording).
- `vkQueueSubmit` (1) flushes host-visible shadows that changed ([memory model](/webgpu/memory-model-and-mapping-emulation.md)), (2) makes **one** JS call that replays all command buffers of the submit into `GPUCommandEncoder`s, opening/closing render and compute passes as needed, (3) `queue.submit([...])`, (4) updates fence/semaphore bookkeeping ([sync](/webgpu/synchronization-and-async-shape.md)). Details in [performance design](/webgpu/performance-and-batching-design.md).
- WebGPU encoder contexts: **command encoder** (copies, `clearBuffer`, `resolveQuerySet`, begin passes), **render pass encoder** (state + draws), **compute pass encoder** (dispatches). Vulkan dispatches can appear anywhere outside a render pass, so the replayer opens a compute pass lazily on the first dispatch and closes it at the next non-dispatch command, coalescing consecutive dispatches.[^webgpu-spec]
- Render-pass state (pipeline, bind groups, vertex/index buffers, viewport, scissor, blend constant, stencil reference, immediates) does **not** survive `end()` in WebGPU; Vulkan dynamic state set outside a pass, or before a pass split, is shadowed by the ICD and re-applied after every `beginRenderPass`.[^webgpu-spec]

Legend for "Ctx": **CE** command encoder, **RP** render pass, **CP** compute pass, **Q** queue, **—** CPU only.

# Render pass and rendering

| Vulkan | WebGPU | Ctx | Notes |
| --- | --- | --- | --- |
| `vkCmdBeginRenderPass(2)` | `encoder.beginRenderPass(desc)` | CE→RP | desc from VkRenderPass subpass 0 + VkFramebuffer views (late-bound for swapchain proxies) + `pClearValues` + render area. Load/store map: LOAD→`load`, CLEAR→`clear`, DONT_CARE→`clear` (defined and cheap), NONE→`load`; STORE→`store`, DONT_CARE→`discard` (**reads back as zero**; see BRUTAL `CreateDefaultRenderPass` caveat in the [ICD surface](/webgpu/brutal-abstractions-icd-surface.md)). Resolve attachments → `resolveTarget` on the colour attachment. Render area smaller than the attachment: WebGPU has no render area → apply as default scissor. `occlusionQuerySet` added if the pass contains occlusion queries. |
| `vkCmdNextSubpass(2)` | `pass.end()` + `beginRenderPass(next)` | RP | attachments written in subpass N use `store`, read in N+1 via `load` or as sampled textures (input attachments). Correct but loses on-chip tile memory. |
| `vkCmdEndRenderPass(2)` | `pass.end()` | RP→CE | pending debug groups closed (WebGPU requires balanced groups per encoder).[^webgpu-spec] |
| `vkCmdBeginRendering` / `vkCmdEndRendering` | `beginRenderPass` / `end` | CE↔RP | 1:1 with `VkRenderingAttachmentInfo` (loadOp/storeOp/clearValue/resolveImageView); `resolveMode` AVERAGE only for colour; depth/stencil resolve unsupported. `SUSPENDING/RESUMING` flags → merge consecutive passes when nothing intervenes, else split. |
| `vkCmdClearAttachments` | (a) fold into `loadOp: "clear"` if it is the first command of the pass and covers the full area; (b) otherwise draw a scissored full-screen triangle with an ICD "clear" pipeline per attachment-format layout | RP | (a) needs replay-time look-ahead; (b) writes depth via `frag_depth`, stencil via `setStencilReference` + replace op. |

# State binding

| Vulkan | WebGPU | Ctx | Notes |
| --- | --- | --- | --- |
| `vkCmdBindPipeline` (graphics) | `setPipeline(variant)` (deferred until next draw) | RP | variant chosen at draw time from current dynamic state (topology, cull, depth/stencil state, `stripIndexFormat` from the bound index type, Y-flip sign) — see [object mapping](/webgpu/vulkan-to-webgpu-object-mapping.md). |
| `vkCmdBindPipeline` (compute) | `setPipeline` | CP | |
| `vkCmdBindDescriptorSets` | `setBindGroup(i, group, dynamicOffsets)` at next draw/dispatch | RP/CP | bind group materialized lazily for the pipeline's layout, cached by (layout, set, version). Dynamic offsets: multiples of 256 in both APIs; order = binding order within the set in both. Vulkan "pipeline layout compatibility / disturbing" rules are looser in WebGPU (groups just need to be group-equivalent at draw), so no special handling. |
| `vkCmdPushDescriptorSet(KHR)` / `WithTemplate` | transient ICD descriptor set → bind group | RP/CP | cache by contents hash (often identical per frame). |
| `vkCmdPushConstants` | **(a)** `setImmediates(offset, data)` when total size ≤ `maxImmediateSize` (64 B) and immediates exist (Chrome 150+)[^chrome-immediates]; **(b)** otherwise append the current push-constant block to a per-frame uniform **ring buffer** (256-B aligned slots) and `setBindGroup(pcGroup, ringGroup, [slotOffset])` before the next draw | RP/CP | WGSL: `var<push_constant>` → `var<immediate>` (a) or `@group(pcGroup) @binding(0) var<uniform>` (b); (b) costs one dynamic UBO and one bind group slot. MoltenVK uses `setVertexBytes` (Metal's immediates);[^moltenvk-config] VkWebGPU-ICD uses a ring buffer.[^vkwebgpu-icd] Push constants set before `vkCmdBeginRenderPass` must be re-applied inside the pass. |
| `vkCmdBindVertexBuffers(2)` | `setVertexBuffer(slot, buf, offset, size)` | RP | **offset must be a multiple of 4** (Vulkan has no such rule).[^webgpu-spec] `pStrides` (dynamic stride) → pipeline variant keyed by stride. |
| `vkCmdBindIndexBuffer(2)` | `setIndexBuffer(buf, "uint16"/"uint32", offset, size)` | RP | offset multiple of index size (same rule as Vulkan); UINT8 → reject or expand. |
| `vkCmdSetViewport` / `WithCount` | `setViewport` | RP | height ≥ 0 and minDepth ≤ maxDepth; negative height → rewrite + flip variant ([coordinates](/webgpu/coordinate-systems-and-conventions.md)). |
| `vkCmdSetScissor` / `WithCount` | `setScissorRect` | RP | clamp to attachment size. |
| `vkCmdSetBlendConstants` | `setBlendConstant` | RP | CONSTANT_ALPHA emulation overrides it. |
| `vkCmdSetStencilReference` | `setStencilReference` | RP | single value for both faces; front ≠ back → unsupported. |
| `vkCmdSetStencilCompareMask` / `WriteMask` | pipeline variant (`stencilReadMask/WriteMask`) | — | per-face masks unsupported. |
| `vkCmdSetDepthBias` / `SetDepthBiasEnable` | pipeline variant (`depthBias`, `depthBiasSlopeScale`, `depthBiasClamp`) | — | compat: clamp must be 0; points/lines: bias must be 0.[^webgpu-spec] Shadow-map code that varies bias per cascade creates one variant per value — cap the cache. |
| `vkCmdSetLineWidth` | none | — | only 1.0 accepted. |
| `vkCmdSetDepthBounds`, `SetDepthBoundsTestEnable` | none | — | reject unless disabled. |
| `vkCmdSetCullMode`, `SetFrontFace`, `SetPrimitiveTopology`, `SetDepthTestEnable`, `SetDepthWriteEnable`, `SetDepthCompareOp`, `SetStencilTestEnable`, `SetStencilOp`, `SetPrimitiveRestartEnable` | pipeline variants | — | Vulkan 1.3 extended dynamic state is fully emulable by variants; cost is pipeline-creation stalls on first use. |
| `vkCmdSetRasterizerDiscardEnable` | variant with a discarding fragment stage, or skip the draw if it has no side effects | — | WebGPU pipelines must match the pass's colour formats, so "no fragment stage" is not an option inside a pass with colour attachments. |
| `vkCmdSetFragmentShadingRateKHR`, `SetSampleLocationsEXT`, `SetLogicOpEXT`, `SetPolygonModeEXT`, ... | none | — | unsupported. |

# Draws and dispatches

| Vulkan | WebGPU | Ctx | Notes |
| --- | --- | --- | --- |
| `vkCmdDraw` | `draw(vertexCount, instanceCount, firstVertex, firstInstance)` | RP | 1:1. WebGPU validates vertex-buffer ranges for non-indexed draws (out-of-range → validation error, Vulkan would be UB). |
| `vkCmdDrawIndexed` | `drawIndexed(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance)` | RP | 1:1; index range validated against the bound index buffer size. |
| `vkCmdDrawIndirect` | loop `drawIndirect(buf, offset + i·stride)` for i < drawCount | RP | offset multiple of 4; indirect `firstInstance ≠ 0` without `indirect-first-instance` makes the draw a **no-op**.[^webgpu-spec] `drawCount` is CPU-side, so `multiDrawIndirect` can be reported true at the cost of one call per draw. |
| `vkCmdDrawIndexedIndirect` | loop `drawIndexedIndirect` | RP | same. |
| `vkCmdDrawIndirectCount` / `DrawIndexedIndirectCount` | none (count is GPU-side) | — | emulation: compute pass zeroes args ≥ count, then loop `maxDrawCount` indirect draws (wasteful); or Chrome's experimental `multiDrawIndirect` behind a flag. Not in the profile. |
| `vkCmdDispatch` | `dispatchWorkgroups(x, y, z)` | CP | each ≤ 65535. |
| `vkCmdDispatchBase` | `dispatchWorkgroups` + base offset via immediates/driver uniform (shader rewrite) | CP | or reject. |
| `vkCmdDispatchIndirect` | `dispatchWorkgroupsIndirect(buf, offset)` | CP | offset multiple of 4. |
| `vkCmdDrawMeshTasks*`, `vkCmdTraceRays*`, `vkCmdBuildAccelerationStructures*` | none | — | unsupported. |
| `vkCmdExecuteCommands` | inline the secondary stream (default) or `executeBundles([bundle])` | RP | bundles reset pass state afterwards and cannot contain viewport/scissor/blend/stencil-ref commands;[^webgpu-spec] inline is the safe default (VkWebGPU-ICD does this).[^vkwebgpu-icd] |

# Transfers (all outside render passes, CE)

| Vulkan | WebGPU | Notes |
| --- | --- | --- |
| `vkCmdCopyBuffer(2)` | `copyBufferToBuffer(src, srcOff, dst, dstOff, size)` per region | offsets and size multiples of 4.[^webgpu-spec] Unaligned regions (Vulkan allows any) → compute-shader byte copy. |
| `vkCmdCopyBufferToImage(2)` | `copyBufferToTexture({buffer, offset, bytesPerRow, rowsPerImage}, {texture, mipLevel, origin, aspect}, size)` | **`bytesPerRow` must be a multiple of 256**; Vulkan `bufferRowLength = 0` means tightly packed (e.g. ImGui's partial font-atlas updates of arbitrary widths).[^webgpu-spec][^brutal-imgui-vk] If the tight pitch is not 256-aligned: when the source is a host-visible staging buffer, **repack on the CPU from the shadow copy** into an ICD upload ring with 256-aligned rows and copy from there (keeps command order); otherwise run a compute "repitch" pass buffer→buffer first. `queue.writeTexture` has no 256 rule but executes before the whole command buffer, so it is only usable when the destination is not touched earlier in the same submission. Offset must be a multiple of the texel block size; depth uploads only for `depth16unorm`/`stencil8`. |
| `vkCmdCopyImageToBuffer(2)` | `copyTextureToBuffer` | same 256-byte pitch rule; copy into an ICD readback buffer with padded rows and de-pad on the CPU after `mapAsync` (readback is async anyway). `depth24plus*` depth aspect not copyable; `depth32float` copy-out OK. |
| `vkCmdCopyImage(2)` | `copyTextureToTexture` | formats equal or differing only by `-srgb`; equal sample counts; depth/stencil must copy whole subresources; compressed copies unsupported in compat.[^webgpu-spec] Size-compatible reinterpretation (e.g. BC7 ↔ RGBA32UI) unsupported. |
| `vkCmdBlitImage(2)` | render pass + full-screen triangle sampling the source (nearest/linear), one pass per destination mip/layer region | needs `RENDER_ATTACHMENT` on the destination (ICD adds it when the format is renderable) and `TEXTURE_BINDING` on the source; mirrored regions via UV transform; non-renderable destinations (compressed, some formats) → compute with storage write or unsupported. The main use, **mip-chain generation**, works this way. |
| `vkCmdResolveImage(2)` | empty render pass: `colorAttachments: [{view: src, loadOp: "load", storeOp: "store", resolveTarget: dst}]` | only at pass end and colour only (`storeOp: "store"` keeps the multisampled source intact, as Vulkan does); depth resolve → shader pass. |
| `vkCmdClearColorImage` | render pass per mip/layer with `loadOp: "clear"` | needs `RENDER_ATTACHMENT` (ICD adds); integer formats clear with integer `clearValue` semantics. Non-renderable formats → `copyBufferToTexture` from a zeroed/pattern buffer. |
| `vkCmdClearDepthStencilImage` | render pass with `depthLoadOp/stencilLoadOp: "clear"` | |
| `vkCmdFillBuffer` | `clearBuffer(buf, off, size)` when `data == 0`; otherwise upload-ring copy (small) or compute fill (large) | offsets/sizes multiples of 4 in both APIs. |
| `vkCmdUpdateBuffer` | write the bytes into the ICD upload ring (via `writeBuffer` before submit) + `copyBufferToBuffer` at this position | **not** `queue.writeBuffer(dst, …)`: that would run before the whole command buffer and break ordering.[^webgpu-spec] |
| `vkCmdCopyQueryPoolResults` | `resolveQuerySet(set, first, count, internalBuf, 256·k)` + `copyBufferToBuffer` | WebGPU resolves 64-bit values; 32-bit or `WITH_AVAILABILITY` layouts need a small compute conversion. |

# Synchronization and queries

| Vulkan | WebGPU | Notes |
| --- | --- | --- |
| `vkCmdPipelineBarrier(2)` | no-op | WebGPU tracks hazards per usage scope and inserts barriers itself; image layout transitions are meaningless.[^webgpu-spec] Can be used as a hint to close an open compute pass. Barriers *inside* a render pass (Vulkan self-dependencies, e.g. feedback loops) have no WebGPU equivalent: a texture cannot be both attachment and sampled in one pass except read-only depth. |
| `vkCmdSetEvent(2)` / `vkCmdResetEvent(2)` / `vkCmdWaitEvents(2)` | no-op (+ CPU event status updated at completion) | single queue, total order. |
| `vkCmdResetQueryPool` | — | CPU bookkeeping. |
| `vkCmdBeginQuery` / `vkCmdEndQuery` (occlusion) | `beginOcclusionQuery(i)` / `endOcclusionQuery()` | must be inside a pass whose descriptor named the `GPUQuerySet`; one query set per pass. `PRECISE_BIT`: WebGPU reports sample counts but exactness is not guaranteed (UNVERIFIED). |
| `vkCmdWriteTimestamp(2)` | `timestampWrites` at the nearest pass boundary (split a pass if needed) | requires `timestamp-query`; mid-pass timestamps impossible. |
| `vkCmdBeginDebugUtilsLabelEXT` / `End` / `Insert` | `pushDebugGroup` / `popDebugGroup` / `insertDebugMarker` | groups must balance within each encoder/pass; the ICD closes groups at pass end and reopens them in the parent encoder.[^webgpu-spec] |
| `vkCmdBeginConditionalRenderingEXT`, transform feedback, `vkCmdSetEvent2` with device-side waits across queues | none | unsupported. |

# Queue and host commands

| Vulkan | WebGPU / ICD | Notes |
| --- | --- | --- |
| `vkQueueSubmit(2)` | flush shadows → replay → `queue.submit([cmdBuffers])` | wait/signal semaphores: no-ops (total order on one queue); timeline values applied to CPU counters; fence handled per [sync](/webgpu/synchronization-and-async-shape.md). Host writes before the submit must be visible to it (Vulkan host-write ordering guarantee) — hence the flush at submit.[^vk-sync] |
| `vkQueueWaitIdle` / `vkDeviceWaitIdle` | cannot block | return `VK_SUCCESS` after flushing; correct for teardown and "upload-then-continue" code because later submits are ordered after earlier ones; incorrect for CPU readback (data arrives asynchronously). |
| `vkQueuePresentKHR` | end-of-frame marker | presentation happens when the rAF callback returns. |
| `vkAcquireNextImageKHR` | `context.getCurrentTexture()` → bind to proxy image `i` | returns `VK_ERROR_OUT_OF_DATE_KHR` when the canvas size changed. Semaphore/fence arguments: signalled immediately. |
| `vkMapMemory` / `vkUnmapMemory` | pointer into wasm-heap shadow / upload dirty range on unmap | [memory model](/webgpu/memory-model-and-mapping-emulation.md). |
| `vkFlushMappedMemoryRanges` | `queue.writeBuffer` of the range into each bound buffer | `writeBuffer` sizes/offsets multiples of 4; pad ranges.[^webgpu-spec] writeBuffer is a good default for wasm (one copy from the heap, no map round trip).[^toji-uploads] |
| `vkInvalidateMappedMemoryRanges` | copy the latest completed readback snapshot into the shadow | data is frames old; see sync note. |
| `vkGetBufferMemoryRequirements(2)` / `vkGetImageMemoryRequirements(2)` / `vkGetDevice*MemoryRequirements` | synthesized: buffer size rounded to 4, alignment 256; image size = Σ mips × layers × texel footprint (estimate), alignment 256; `memoryTypeBits` per memory model | no real memory exists; values only need to be self-consistent. |
| `vkGetPhysicalDeviceFormatProperties(2)` / `ImageFormatProperties` | static table | [format tables](/webgpu/format-tables.md). |
| `vkGetPhysicalDeviceSurfaceCapabilitiesKHR` | from canvas size | `minImageCount` 2, `maxImageCount` 3, `currentExtent` = drawing-buffer size, transforms IDENTITY, composite alpha OPAQUE\|PRE_MULTIPLIED, usage COLOR_ATTACHMENT\|TRANSFER_SRC\|TRANSFER_DST\|SAMPLED. |
| `vkGetPhysicalDeviceSurfaceFormatsKHR` / `PresentModesKHR` / `SupportKHR` | preferred canvas format (+srgb twin) / FIFO only / true | |
| `vkGetQueryPoolResults` | from last completed readback | `WAIT_BIT` cannot block → `VK_NOT_READY` until the async readback lands (frame lag). |
| `vkGetFenceStatus` / `vkWaitForFences` / `vkGetSemaphoreCounterValue` / `vkWaitSemaphores` | CPU state | see sync note. |

# Open questions

- UNVERIFIED: whether occlusion query results in WebGPU are exact sample counts on all backends (affects `occlusionQueryPrecise`).
- Measure the cost of pipeline variants created mid-frame (synchronous `createRenderPipeline` may stall the GPU process on first use); consider pre-warming variants for known dynamic-state combinations.

[^webgpu-spec]: WebGPU W3C Editor's Draft, 8 October 2026
[^vk-sync]: Vulkan specification, Synchronization chapter
[^chrome-immediates]: Chrome 150 release notes — WebGPU immediates (setImmediates)
[^vkwebgpu-icd]: Dankular/VkWebGPU-ICD (ring-buffer push constants, inlined secondaries, no-op barriers)
[^moltenvk-config]: MoltenVK configuration parameters
[^brutal-imgui-vk]: BRUTAL ImGui Vulkan backend (texture upload sub-rectangles)
[^toji-uploads]: Brandon Jones, WebGPU best practices — buffer uploads
