# Brutal.Vulkan.WebGpu — the web ICD

A C# implementation of a documented Vulkan subset on the browser's WebGPU. BRUTAL's `Brutal.Vulkan` bindings (browser flavor,
managed function pointers) reach it through `vkGetInstanceProcAddr`; game code stays plain BRUTAL/Vulkan
(docs/architecture.md sections 2–6).

```csharp
WebIcd.Install();                                   // loader key + BrowserSurfaces.CreateSurface hook; reads the adapter limits
WebIcd.LoadShaderManifest(manifestJsonBytes);       // Shaders/manifest.json from tools/build-shaders.mjs
using var instance = Vulkan.CreateInstance(...);    // everything else is the normal Brutal.Vulkan API
```

JS side: `wwwroot/webgpu-glue.js` (module `webgpu`, published at the root of the app's `wwwroot`): `init({ device, canvas, format,
getHeapU8 })` before `dotnet.create()`, then `setModuleImports('webgpu', imports)`. Test hooks: `window.__brutal.readPixel(x, y)`,
`frameCount`, `lastError`.

Public API: `WebIcd` (`Install`, `LoadShaderManifest`, `Options`, `LoaderKey`), `WebIcdOptions`, `VulkanUnsupportedException` and
`VkWebSurfaceCreateInfoBRUTAL`. Everything else, including the `WebGpuBridge` abstraction, is internal; the unit tests reach it
through `InternalsVisibleTo` and the seams collected in `IcdTestHooks` (log capture, recorded lint violations, per-test reset,
stream decoder).

## How it works

| Layer | Files |
| --- | --- |
| Entry points: plain `static` methods with BRUTAL's exact signatures, one file per concern | `Icd.*.cs` |
| Name → function pointer; every one of BRUTAL's 804 command structs + the 4 globals + `vkCreateWebSurfaceBRUTAL` resolves; unimplemented commands are generated stubs that throw `VulkanUnsupportedException(name)` (a null managed pointer would abort the runtime) | `Registry.cs`, `Generated/*.g.cs` |
| Objects behind `nint` handles (20-bit slot + 11-bit generation: never 0, 32-bit safe, use-after-destroy detected) | `Objects/*.cs` |
| Command buffers record into unmanaged streams; `vkQueueSubmit` concatenates them and makes **one** `replayCommandStream` JS call | `CommandStream.cs`, `Opcodes.cs`, `Icd.Queue.cs` |
| WebGPU access: abstract bridge, `[JSImport]` implementation, JS handle table + replayer | `Bridge/*.cs`, `wwwroot/webgpu-glue.js` |
| Profile, formats, enum tables, WebGPU-only lint | `Profile.cs`, `Formats.cs`, `Mappings.cs`, `WebProfileLint.cs` |

Key behaviour (design notes: docs/research/webgpu/*.md):

- **Memory.** Type 0 `DEVICE_LOCAL` is bookkeeping; type 1 `HOST_VISIBLE|HOST_COHERENT` is a zero-filled wasm-heap shadow that
  `vkMapMemory` returns. `vkFlushMappedMemoryRanges`/`vkUnmapMemory` upload the bound buffer ranges with `writeBuffer` at once.
  Because coherent memory may be written without a flush, every submit also uploads the mapped range of every mapped allocation
  bound to a buffer (cost: all persistently mapped bytes, every submit; buffers written by GPU copies are skipped). Keep
  persistently mapped memory small (uniform rings), or unmap/flush explicitly.
- **Readback.** Copies into host-visible buffers, and shader-writable storage buffers bound from host-visible memory, are
  followed by a staging copy + `mapAsync` into the shadow; the submit's fence signals when the data has landed (a frame or more
  later), and a fence-only submit (no command buffers) waits for every readback in flight. All other fences signal at submit.
  While a readback is pending its buffer is not re-uploaded from the shadow, and freeing its memory is deferred until it lands.
  `vkWaitForFences` never blocks: it returns `VK_SUCCESS` or `VK_TIMEOUT` (a non-zero timeout on a pending readback fence is
  reported once).
- **Swapchain.** N=3 virtual images over the canvas; views of them resolve at replay to the frame's `getCurrentTexture()` (cached
  per frame, dropped at `vkQueuePresentKHR`). A canvas resize makes `vkAcquireNextImageKHR` return `VK_ERROR_OUT_OF_DATE_KHR`.
  Semaphores are inert (one queue, total order).
- **Shaders.** `vkCreateShaderModule` looks the SPIR-V up by SHA-256 in the manifest (no runtime translation); a miss fails with
  `VK_ERROR_INVALID_SHADER_NV` naming the hash. Bindings: set → `@group`, binding → `@binding`, combined image sampler `b` →
  texture `b` + sampler `b + 500`, push constants (≤ 128 bytes) → `@group(3) @binding(0)` uniform (or storage) fed from a ring of
  256-byte slots: one slot per draw or dispatch that follows a `vkCmdPushConstants`. The ring starts with 4096 slots and doubles
  inside a replay when a submit needs more (the full ring keeps serving the commands recorded so far and is destroyed after the
  submit), so slots are never reused within a submit; past `maxBufferSize` the submit is dropped and returns
  `VK_ERROR_OUT_OF_DEVICE_MEMORY` with an ERROR.
- **Bind group layouts** are built on first need (first pipeline using the set layout, or first bind): texture sample type/view
  dimension, comparison samplers and storage access come from the WGSL of that pipeline, else from the first descriptor write,
  else defaults (`float`, `2d`, `filtering`; read-only storage when visible to the vertex stage). A layout is built once: a later
  pipeline that needs different binding types logs an ERROR (use separate set layouts). Never-written descriptors get stand-ins
  (buffer, 1x1 2D float/depth texture, sampler) with a WARN. Pipeline layouts are built per pipeline: groups its shaders do not
  use are bound to an empty group at draw. Pipelines without a fragment stage used with colour attachments get an empty one.
- **Copies.** Buffer→image copies from host-visible buffers become `queue.writeTexture` from the shadow (any row pitch; the
  replayer submits earlier commands first to keep order). Copies to/from device-local buffers need 256-byte rows (multi-row
  copies). Compressed extents are rounded to whole blocks; depth/stencil image→image copies use all aspects; buffer copies of
  `depth24plus` depth are unsupported. Unaligned buffer copy sizes are padded only at the end of both buffers.
- **Render passes.** Dynamic rendering and single-subpass `VkRenderPass`es become one record. `DONT_CARE` stores of images that
  can be read later (SAMPLED/TRANSFER_SRC) are stored (WebGPU `discard` reads back as zero) with a lint warning. Read-only depth/
  stencil layouts set `depthReadOnly`/`stencilReadOnly`; resumed passes load, suspended ones store. WebGPU clears whole
  attachments, so a clear with a smaller render area is linted.
- **Swapchain extent.** `currentExtent` = `minImageExtent` = `maxImageExtent` = the canvas drawing-buffer size.

## Supported subset: `VP_BRUTAL_web_poc_2026`

apiVersion 1.3 (non-conformant: no `bufferDeviceAddress`, descriptor indexing, `privateData`, inline uniform blocks, subgroup size
control, ...; the limits are below Vulkan's minimums). 1.3 is advertised because BRUTAL calls the 1.3 core entry points
(`vkCmdBeginRendering`, `vkCmdPipelineBarrier2`, ...) and the Khronos profiles layer resolves those names only under a 1.3 profile
(`vkEnumerateInstanceVersion` and `VkPhysicalDeviceProperties.apiVersion` both report `Profile.ApiVersion`). Instance extensions `VK_KHR_surface`, `VK_KHR_get_physical_device_properties2`,
`VK_KHR_portability_enumeration`, `VK_EXT_debug_utils`, `VK_BRUTAL_web_surface`; device extensions `VK_KHR_swapchain`,
`VK_KHR_portability_subset`, `VK_KHR_dynamic_rendering`, `VK_KHR_synchronization2`, `VK_KHR_maintenance1..4`; 0 layers. One
physical device, one queue family (graphics|compute|transfer, 1 queue), two memory types. Limits are the WebGPU core defaults
clamped by the device (`maxBoundDescriptorSets` 3 — group 3 is reserved for push constants —, `maxPushConstantsSize` 128,
`maxVertexInputBindings` 8, offsets aligned to 256, sample counts 1|4, one viewport). Features: robustBufferAccess,
fullDrawIndexUint32, imageCubeArray, independentBlend, depthBiasClamp, samplerAnisotropy, vertex/fragment stores; dynamicRendering,
synchronization2, maintenance4, imagelessFramebuffer, separateDepthStencilLayouts, hostQueryReset, robustImageAccess,
shaderDemoteToHelperInvocation, shaderTerminateInvocation, shaderZeroInitializeWorkgroupMemory (reported through
`VkPhysicalDeviceVulkan13Features` and the stand-alone promoted structs, which agree); BC/ETC2/ASTC and depthClamp only when the
WebGPU device has the feature. `VkPhysicalDeviceVulkan11/12/13Properties` are answered too (no subgroups, no update-after-bind, no
inline uniform blocks, `conformanceVersion` 0.0.0.0, `maxBufferSize` from the WebGPU limit). `vkCreateDevice` rejects anything
else, including the 1.3 features the ICD lacks (`privateData`, `pipelineCreationCacheControl`, `inlineUniformBlock`, ...), with
`VK_ERROR_FEATURE_NOT_PRESENT`.

`profiles/VP_BRUTAL_web.json` (`VP_BRUTAL_web_poc_2026`) describes this same device for the Khronos profiles layer;
`ProfileJsonSyncTests` in `tests/Brutal.Vulkan.WebGpu.Tests` fails when `Profile.cs` and the JSON drift apart (api version,
every limit, extensions, features, formats, queue family). Change both together.

Implemented: instance/physical-device queries (+`2`/`KHR` variants), device/queue, memory (allocate/map/unmap/flush/invalidate/
bind), buffers, images, views, samplers, shader modules, descriptor set layouts/pools/sets/updates/copies, pipeline layouts,
graphics/compute pipelines, pipeline caches (no-op), render passes/framebuffers (one subpass) and dynamic rendering, command
pools/buffers, fences/semaphores/events, surface/swapchain, debug utils, and the commands bind pipeline/descriptor sets/vertex/index
buffers, viewport/scissor/blend constants/stencil reference, push constants, draw/drawIndexed/indirect (CPU loop for
`drawCount > 1`), dispatch/indirect, copy buffer/buffer↔image/image, fill/update buffer, barriers and events (no-ops), execute
commands (inlined), debug labels. `Registry.ImplementedNames` is the authoritative list (227 names including KHR/EXT aliases).

Known gaps (stubs or explicit errors): dynamic depth bias (`vkCmdSetDepthBias` is ignored with one warning: set the bias in the
pipeline, e.g. one shadow pipeline per bias), descriptor arrays, texel buffers, input attachments, multi-subpass render passes, blits,
resolves and image clears outside a render pass (`vkCmdBlitImage`, `vkCmdResolveImage`, `vkCmdClear*` — game tier), queries,
timeline semaphores, push descriptors, extended dynamic state (dynamic cull/depth/stencil state is ignored with a warning),
negative viewport heights, border colours, non-identity swizzles, sparse resources, buffer device address, `var<immediate>` push
constants. Strip topologies with indexed draws get a pipeline variant per index format. Debug labels opened inside a render
pass are closed at the end of the pass (WebGPU groups cannot span passes), so label nesting across passes is approximate.
A draw or dispatch whose state cannot be applied (e.g. an unbound descriptor set) is skipped and reported; a replay that throws
fails the readbacks of its submit (status -1, an ERROR at the fence).

## Diagnostics

Console lines `ERROR [webgpu] …` (the call failed or the command was dropped), `WARN [webgpu] …` (ignored/approximated feature,
lint warning; once per function and reason, checked before the message is formatted, so an ignored command called every frame does
not allocate) and `INFO`, also sent to every `VkDebugUtilsMessengerEXT`. WebGPU validation errors (uncaptured errors, and error
scopes with `WebIcd.Options.DebugErrorScopes`) are polled after submits and logged the same way. Device loss (`device.lost`): the
glue stops replaying, and `vkQueueSubmit`, `vkAcquireNextImageKHR`, `vkQueuePresentKHR` and `vkWaitForFences` return
`VK_ERROR_DEVICE_LOST` from then on (one ERROR naming it).
`WebIcd.Options`: `LogUnsupported`, `ValidateProfile` (WebProfileLint: 256-byte dynamic offsets, 4-byte offsets/sizes, ≤ 8 vertex
buffers, ≤ 4 bind groups, positive viewport heights, STORE for attachments read later, no memory aliasing, no blocking waits on
readback fences), `StrictExtensions`, `DebugErrorScopes`.

## Replay stream (`Opcodes.cs` ↔ `OP` in `webgpu-glue.js`)

Little-endian u32 words; each record starts with `opcode | (recordWords << 16)` (record length includes the header word); inline
data is padded to 4 bytes. Ids are JS handle-table ids; 1/2 = the frame's canvas view (linear/sRGB), 3 = the canvas texture,
4/5 = empty bind group layout/bind group, 6/7 = push-constant group layouts. Codes: loadOp 0 clear, 1 load, 2 omit; storeOp
0 store, 1 discard, 2 omit; clearKind 0 float, 1 sint, 2 uint; aspect 0 all, 1 depth-only, 2 stencil-only; bindPoint 0 graphics,
1 compute; pushMode 0 none, 1 uniform, 2 storage; index format 0 uint16, 1 uint32. A unit test checks that both tables agree.
`replayCommandStream` returns the number of errors waiting in `pollErrors` plus the `BridgeStatus` flags (bit 16 device lost, bit 17
push-constant ring exhausted); `acquireCanvasTexture` and `present` return -1 instead of the canvas size after device loss.

| Op | Name | Payload (u32 words) |
| --- | --- | --- |
| 0 | End | — |
| 1 | BeginRenderPass | colorCount, flags (bit 0 = depth/stencil), area x, y, w, h, attachment w, h; per colour: view, resolveView, loadOp, storeOp, clearKind, clear[4]; if depth/stencil: view, depthLoadOp, depthStoreOp, clearDepth (f32), stencilLoadOp, stencilStoreOp, clearStencil, 0 |
| 2 | EndRenderPass | — |
| 3 | BindPipeline | pipeline, bindPoint, pushMode, fillerMask (groups bound to the empty group), groupCount |
| 4 | BindDescriptorSets | bindPoint, firstSet, setCount; per set: bindGroup, dynamicCount, dynamicOffsets… |
| 5 | BindVertexBuffers | firstBinding, count; per buffer: buffer, offset, size (0 = rest) |
| 6 | BindIndexBuffer | buffer, offset, indexFormat, size (0 = rest) |
| 7 | SetViewport | x, y, width, height, minDepth, maxDepth (f32) |
| 8 | SetScissor | x, y, width, height |
| 9 | SetBlendConstants | r, g, b, a (f32) |
| 10 | SetStencilReference | reference |
| 11 | Draw | vertexCount, instanceCount, firstVertex, firstInstance |
| 12 | DrawIndexed | indexCount, instanceCount, firstIndex, vertexOffset (i32), firstInstance |
| 13 | DrawIndirect | buffer, offset, drawCount, stride |
| 14 | DrawIndexedIndirect | buffer, offset, drawCount, stride |
| 15 | Dispatch | x, y, z |
| 16 | DispatchIndirect | buffer, offset |
| 17 | CopyBuffer | src, srcOffset, dst, dstOffset, size |
| 18 | CopyBufferToTexture | buffer, offset, bytesPerRow, rowsPerImage, texture, mip, x, y, z, aspect, width, height, depthOrLayers |
| 19 | WriteTexture | srcPtr (wasm heap), byteCount, bytesPerRow, rowsPerImage, texture, mip, x, y, z, aspect, width, height, depthOrLayers |
| 20 | CopyTextureToBuffer | texture, mip, x, y, z, aspect, buffer, offset, bytesPerRow, rowsPerImage, width, height, depthOrLayers |
| 21 | CopyTextureToTexture | src texture, mip, x, y, z, aspect, dst texture, mip, x, y, z, aspect, width, height, depthOrLayers |
| 22 | ClearBuffer | buffer, offset, size |
| 23 | WriteBuffer | buffer, offset, srcPtr (wasm heap), size — coherent uploads at the start of a submit |
| 24 | UpdateBuffer | buffer, offset, size, data… |
| 25 | PushConstants | offset, size, data… |
| 26 | PushDebugGroup | byteLength, UTF-8… |
| 27 | PopDebugGroup | — |
| 28 | InsertDebugMarker | byteLength, UTF-8… |
| 29 | Readback | buffer, offset, size, dstPtr (shadow), statusPtr (i32 set to 1/-1 when the copy into the shadow completed) |
| 30 | Barrier | — (pipeline barriers / event waits: no-ops on WebGPU) |

## Regenerating the stubs

```sh
python3 -I tools/gen-icd-stubs.py            # rewrites Generated/Stubs.g.cs and Generated/StubTable.g.cs
python3 -I tools/gen-icd-stubs.py --check    # exit 1 when the checked-in files are stale (also run by the unit tests)
```

It reads `$BRUTAL_SRC` (default `../BRUTAL`)`/Brutal.Vulkan/Generated/Functions.gen.cs`, resolves BRUTAL's assembly-local
`global using` type aliases, and registers each stub through BRUTAL's own function-pointer struct so the compiler checks every
signature. Hand-written implementations go into `Icd.*.cs` and `ImplementedTable` in `Registry.cs` (they override stubs).

## Tests

- `dotnet test tests/Brutal.Vulkan.WebGpu.Tests` — desktop xunit: the real Brutal.Vulkan wrappers drive the ICD against a
  recording bridge (scene/frame streams, memory and readback, descriptors, lint, profile, registry coverage, opcode table vs JS,
  formats, stub freshness).
- `cd tests/Brutal.Vulkan.WebGpu.Smoke/test && npm ci && node run.mjs` — publishes the smoke app (Release, trimmed), renders a
  textured, depth-tested triangle pair in headless Chromium and checks pixels (`readPixel` + screenshot) and a
  `vkCmdCopyImageToBuffer` readback; `--chrome` runs Google Chrome, `--config Debug` an untrimmed build.
