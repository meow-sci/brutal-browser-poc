---
type: Code Analysis
title: Vulkan calls made by Brutal.Vulkan.Abstractions and Manual/Classes — the minimal ICD surface
description: Every vk* function the BRUTAL Vulkan helper layer (Abstractions plus the hand-written Manual/Classes wrappers) calls, grouped by object, with a WebGPU mapping difficulty rating, plus the extra calls a real app and the BRUTAL ImGui Vulkan backend need, and the loader/bootstrap problems a browser build must patch.
tags: [brutal, vulkan, webgpu, icd, api-surface, code-analysis]
sources:
  - id: brutal-abstractions
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions
    title: Brutal.Vulkan.Abstractions source (BRUTAL commit a25cdf8)
  - id: brutal-manual
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Manual/Classes
    title: Brutal.Vulkan Manual/Classes (Instance, PhysicalDevice, Device, Queue, CommandBuffer)
  - id: brutal-importresolver
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/ImportResolver.cs
    title: Brutal.Vulkan ImportResolver.cs (native loader bootstrap)
  - id: brutal-globals
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/PInvoke/VulkanApi/VulkanApi.gen.cs
    title: Generated LibraryImport("vulkan") declarations for the four global commands
  - id: brutal-fntables
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Handles/VkDevice.gen.cs
    title: Generated VkDevice.FunctionTable (eager vkGetDeviceProcAddr of every device command)
  - id: brutal-imgui-vk
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ImGui.Backends/RendererBackends/VulkanBackend.cs
    title: BRUTAL Dear ImGui Vulkan renderer backend (VulkanBackend.cs + VulkanBackend.Helpers.cs)
  - id: ksa-devicectx
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/Core/KSADeviceContextEx.cs
    title: KSA decompiled device bootstrap (reference only)
  - id: ksa-bindless
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/RenderCore.Systems/BindlessTextureLibrary.cs
    title: KSA decompiled bindless texture library (reference only)
  - id: webgpu-spec
    resource: https://gpuweb.github.io/gpuweb/
    title: WebGPU W3C Editor's Draft, 8 October 2026
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:13:57Z }
status: draft
---

# Method

The list below was produced by (1) mapping every C# wrapper method in `Brutal.Vulkan/Generated/Extensions/Vk{Device,Instance}Extensions.gen.cs` to the `PInvoke.vkXxx.Invoke` it performs, (2) scanning `Brutal.Vulkan.Abstractions/**/*.cs` and `Brutal.Vulkan/Manual/Classes/*.cs` for calls to those wrappers and for direct `PInvoke.vk*` uses, and (3) reading every file to remove name-collision false positives (e.g. `GetBufferMemoryRequirements(VkBuffer)` is `vkGetBufferMemoryRequirements`, not the `vkGetDevice*` overload).[^brutal-abstractions][^brutal-manual] The same scan was run over the ImGui Vulkan backend and, for orientation only, over the KSA decompiled game code.[^brutal-imgui-vk][^ksa-devicectx]

Difficulty scale used in the tables: **T** trivial (no WebGPU work or a constant answer), **E** easy (1:1 WebGPU call), **M** moderate (needs a translation table, a cache, or deferred work), **H** hard (semantic emulation with caveats), **X** not feasible in WebGPU (return an error / report the feature as unsupported).

# Bootstrap blockers (must be fixed before any call reaches an ICD)

| # | Finding | Where | Consequence in browser-wasm | Recommended fix |
| --- | --- | --- | --- | --- |
| 1 | The `Vulkan` static constructor loads `vulkan-1` / `VulkanEx` / `vulkan` with `NativeLibrary.Load` depending on `OperatingSystem.IsWindows/IsMacOS/IsLinux`; on any other OS `Dll` stays 0 and it throws `NullReferenceException("Failed to load Vulkan library")`. `GetInstanceProcAddr` is a get-only static set only there.[^brutal-importresolver] | `ImportResolver.cs` | Every first touch of `Vulkan.*` throws on `OperatingSystem.IsBrowser()`. | Fork/patch: add an injection point (e.g. `Vulkan.UseLoader(delegate* unmanaged[Cdecl]<VkInstance, byte*, PfnVkVoidFunction>)`) evaluated before the static ctor's native load, or a `#if BROWSER` branch that takes the ICD's `vkGetInstanceProcAddr`. |
| 2 | The four global commands `vkCreateInstance`, `vkEnumerateInstanceExtensionProperties`, `vkEnumerateInstanceLayerProperties`, `vkEnumerateInstanceVersion` are `[LibraryImport("vulkan")]` P/Invokes resolved through `NativeLibrary.SetDllImportResolver`.[^brutal-globals] | `VulkanApi.gen.cs` | In browser-wasm a P/Invoke only binds to native code statically linked into `dotnet.wasm`; a resolver returning a library handle cannot work (UNVERIFIED for .NET 10, see `/dotnet-wasm/`). | Either (a) change the generator so globals are fetched with `vkGetInstanceProcAddr(NULL, "vkCreateInstance")` (legal Vulkan), or (b) statically link a tiny C shim named `vulkan` that forwards to the managed ICD, or (c) write the ICD in C and link it (see [performance notes](/webgpu/performance-and-batching-design.md)). Option (a) is the cleanest. |
| 3 | `Instance`'s and `Device`'s `FunctionTable` constructors eagerly call `getProc.Invoke(handle, "vkXxx")` for every command (~690 device commands, ~100 instance commands) and store raw `delegate* unmanaged[Cdecl]` pointers.[^brutal-fntables] | `Generated/Handles/*.gen.cs` | The ICD must answer every name (NULL for unsupported). WebAssembly `call_indirect` traps on signature mismatch, so a single generic "unsupported" stub cannot be returned for all names; every non-NULL pointer must have the exact C signature. | Return NULL for unsupported commands; generate exact-signature `[UnmanagedCallersOnly]` stubs only for the supported set (codegen from `vk.xml`). Calling a NULL entry then fails loudly at the call site. |
| 4 | Managed → `delegate* unmanaged` → managed `[UnmanagedCallersOnly]` round trip. | all calls | Whether Mono-wasm (interpreter or AOT) supports `calli` to unmanaged pointers that point back at `[UnmanagedCallersOnly]` methods, for every signature, is UNVERIFIED; this is the single biggest feasibility risk of a *managed* ICD. | Prototype one call (`vkGetPhysicalDeviceProperties`) first; fall back to a native C ICD linked into `dotnet.wasm` if it fails. Coordinate with `/dotnet-wasm/`. |
| 5 | `VulkanHelpers.AddPlatformRequiredExtensions` / `GetPlatformRequiredInstanceCreateFlags` key off `OperatingSystem.IsMacOS()`; on browser they add nothing.[^brutal-abstractions] | `VulkanHelpers.cs` | Fine. The ICD should still accept `VK_KHR_portability_enumeration` + the enumerate-portability flag so code paths written for macOS work. | — |

# Calls made by Manual/Classes (always on the path)

| Vulkan command | Called from | WebGPU mapping | Rating |
| --- | --- | --- | --- |
| `vkGetInstanceProcAddr` | `Vulkan` static ctor, `Instance` function table, `Device` ctor (`GetProcAddr("vkGetDeviceProcAddr")`) | ICD dispatch table lookup (string → exact-signature fn ptr) | T |
| `vkCreateInstance` | `Instance` ctor (global P/Invoke) | Validate requested extensions/layers; JS side already holds `navigator.gpu` + adapter | E (blocked by bootstrap #2) |
| `vkDestroyInstance` | `Instance.Dispose` | free handle table entries | T |
| `vkEnumeratePhysicalDevices` | `Instance` ctor | one `VkPhysicalDevice` = the pre-requested `GPUAdapter` | T |
| `vkEnumerateDeviceExtensionProperties` | `PhysicalDevice.ExtensionNames*` | static list (see [profile](/webgpu/web-vulkan-profile-proposal.md)) | T |
| `vkGetPhysicalDeviceQueueFamilyProperties` | `PhysicalDevice.GetFirstQueueFamilyWithFlag`, `PhysicalDeviceExtensions.FindQueueFamily` | one family: GRAPHICS\|COMPUTE\|TRANSFER, count 1 | T |
| `vkGetPhysicalDeviceMemoryProperties` | `PhysicalDevice.FindMemoryType` | synthetic heaps/types ([memory model](/webgpu/memory-model-and-mapping-emulation.md)) | T |
| `vkCreateDevice` | `PhysicalDevice.CreateDevice` → `Device` ctor | validate features against the pre-created `GPUDevice`; `VK_ERROR_FEATURE_NOT_PRESENT` otherwise | E |
| `vkGetDeviceProcAddr` | `Device` ctor (function table) | same dispatch as instance | T |
| `vkDestroyDevice` | `Device.Dispose` | `device.destroy()` (optional) | T |
| `vkGetDeviceQueue` | `Device.GetQueue` → `Queue` ctor | `device.queue` | T |
| `vkQueueSubmit` / `vkQueueSubmit2` | `Queue.Submit/Submit2` | replay encoded command buffers → `queue.submit([...])`; semaphores ignored; fence bookkeeping ([sync](/webgpu/synchronization-and-async-shape.md)) | M |
| `vkQueueWaitIdle` | `Queue.WaitIdle` | cannot block; flush + return `VK_SUCCESS` (see sync note) | H |
| `vkQueuePresentKHR` | `Queue.PresentKHR` | end-of-frame marker; the browser presents when the rAF task returns | M |
| `vkQueueBindSparse` | `Queue.BindSparse` | — | X |
| `vkQueueBegin/End/InsertDebugUtilsLabelEXT` | `Queue` | no queue-level labels in WebGPU; log or no-op | T |

# Calls made by Brutal.Vulkan.Abstractions

| Area / file | Vulkan commands | WebGPU mapping summary | Rating |
| --- | --- | --- | --- |
| `Classes/DeviceEx.cs` (`AllocateImage`) | `vkCreateImage`, `vkGetImageMemoryRequirements`, `vkAllocateMemory` (+ optional `VkMemoryAllocateFlagsInfo` in `pNext`), `vkBindImageMemory`, `vkSetDebugUtilsObjectNameEXT` (DEBUG builds only) | `createTexture` at create time; memory requirements synthesized; bind is bookkeeping; names → `GPUObjectBase.label` | M |
| `Classes/DeviceEx.cs` (`AllocateBuffer`) | `vkCreateBuffer`, `vkGetBufferMemoryRequirements`, `vkAllocateMemory`, `vkBindBufferMemory`, `vkSetDebugUtilsObjectNameEXT` | `createBuffer` (size rounded to 4, usage translated, `COPY_DST` always added); 1 memory object per buffer, offset 0 — the easy case | M |
| `Classes/DeviceEx.cs` (map/unmap/free) | `vkMapMemory` (whole allocation, ref-counted), `vkUnmapMemory`, `vkDestroyImage`, `vkDestroyBuffer`, `vkFreeMemory` | shadow memory in the wasm heap, uploaded with `queue.writeBuffer` on unmap/flush/submit; `destroy()` deferred until the GPU is done | H (mapping) / E (destroy) |
| `Extensions/DeviceExtensions.cs` | `vkCreateRenderPass` (`CreateDefaultRenderPass`), `vkCreatePipelineLayout`, `vkCreateGraphicsPipelines`, `vkCreateComputePipelines`, `vkAllocateCommandBuffers`, `vkFreeCommandBuffers`, `vkAllocateMemory`, `vkGetBuffer/ImageMemoryRequirements`, `vkMapMemory`/`vkUnmapMemory` (`WriteMemory`), `vkCmdBindDescriptorSets`, `vkCreateFence`, `vkWaitForFences`, `vkCmdExecuteCommands`, `vkGetBufferDeviceAddress` | render pass = CPU record; pipeline layout → `GPUPipelineLayout` (+ push-constant group); pipelines → `createRenderPipeline`/`createComputePipeline`; secondary command buffers → `GPURenderBundle`; BDA → X | M–H; BDA X |
| `Extensions/CommandBufferExtensions.cs` | `vkResetCommandBuffer`, `vkBeginCommandBuffer` (incl. `VkCommandBufferInheritanceInfo`), `vkCmdBeginDebugUtilsLabelEXT`, `vkCmdEndDebugUtilsLabelEXT`, `vkCmdCopyBuffer`, `vkCmdBindVertexBuffers`, `vkCmdPushConstants` | command-stream recording; labels → `pushDebugGroup/popDebugGroup`; push constants → immediates or ring-buffer UBO | E (most), H (push constants) |
| `Extensions/QueueExtensions.cs` | `vkQueueSubmit` incl. a `VkTimelineSemaphoreSubmitInfo` variant | submit; timeline values tracked on CPU | M |
| `Extensions/PhysicalDeviceExtensions.cs` | `vkGetPhysicalDeviceFormatProperties` (`FindSupportedFormat`), `vkGetPhysicalDeviceQueueFamilyProperties` | static format table ([format tables](/webgpu/format-tables.md)) | T |
| `Extensions/ShaderModuleExtensions.cs` | `vkCreateShaderModule` (`FromSpirv`) | keep SPIR-V bytes / look up pre-translated WGSL by hash (`/shaders/`) | M |
| `StagingPool.cs` | `vkCreateCommandPool`, `vkAllocateCommandBuffers`, `vkCreateFence`, `vkQueueSubmit`, `vkWaitForFences` (timeout −1 = infinite), `vkResetFences`, `vkResetCommandPool`, `vkDestroyCommandPool`, `vkDestroyFence`, plus buffer create/map/unmap via the allocator; staging memory **requires** `HOST_VISIBLE\|HOST_COHERENT` | upload-only fences must be signalled at submit (blocking wait is impossible); see sync note | M |
| `FrameResources.cs` | `vkDestroyImageView`, `vkDestroyImage`, `vkFreeMemory`, `vkDestroyFramebuffer` | deferred destroy | T |
| `SyncObjects.cs` | `vkFreeCommandBuffers`, `vkDestroyFence`, `vkDestroySemaphore` | bookkeeping | T |
| `DescriptorPoolEx.cs` | `vkCreateDescriptorPool`, `vkAllocateDescriptorSets`, `vkFreeDescriptorSets`, `vkDestroyDescriptorPool` | CPU-only allocator | T |
| `DescriptorSetLayoutEx.cs` | `vkCreateDescriptorSetLayout`, `vkDestroyDescriptorSetLayout` | CPU record; `GPUBindGroupLayout` built lazily | M |
| `ImageEx.cs` / `ImageViewEx.cs` | `vkCreateImageView`, `vkDestroyImageView` | `texture.createView` | E |
| `BufferEx.cs` | `vkGetBufferDeviceAddress` (`GetDeviceAddress`) | none | X |
| `MappedMemory.cs` | `vkFlushMappedMemoryRanges`, `vkInvalidateMappedMemoryRanges`, `vkUnmapMemory` (via allocator) | flush → `writeBuffer` of the range; invalidate → copy last read-back snapshot into the shadow | M |
| `IBindBuffer.cs` | `vkCmdBindVertexBuffers`, `vkCmdBindIndexBuffer`, `vkCmdDrawIndirect` | `setVertexBuffer`, `setIndexBuffer`, `drawIndirect` (loop for `drawCount > 1`) | E |
| `Shaders/ShaderStages.cs` | `vkDestroyShaderModule` | drop SPIR-V/WGSL record | T |
| `DebugNameExtensions.cs` | `vkSetDebugUtilsObjectNameEXT` | `label` | T |
| `VulkanHelpers.cs` | `vkEnumerateInstanceLayerProperties` (validation-layer probe); builds `VkDebugUtilsMessengerCreateInfoEXT` | return 0 layers (so `Validation.IsSupported == false`); messenger receives WebGPU `uncapturederror` text | T |
| `VulkanDeviceInfo.cs` | `vkGetPhysicalDeviceProperties` | synthesized from `GPUAdapterInfo` + limits | T |
| `Presets.cs`, `VertexInput.cs` | no calls; but they define the state the ICD must translate | see notes below | — |

Unique Vulkan commands reached by Abstractions + Manual/Classes: **74** (counted from the two tables above, duplicates removed: 19 from Manual/Classes, 55 more from Abstractions). Of these, only `vkQueueBindSparse` and `vkGetBufferDeviceAddress` are infeasible, and neither is needed by a conservative app.

# State the helpers can produce that WebGPU cannot express

| Helper | Value it can produce | WebGPU status | ICD behaviour |
| --- | --- | --- | --- |
| `Presets.Rasterization.Line.*` | `VK_POLYGON_MODE_LINE` | no polygon modes in WebGPU[^webgpu-spec] | fail pipeline creation (`fillModeNonSolid = false`) or emulate with a line-list index buffer |
| `Presets.InputAssembly.PatchList` | `VK_PRIMITIVE_TOPOLOGY_PATCH_LIST` | no tessellation | fail (feature not reported) |
| `Presets.InputAssembly.PointList` | point list | supported, but point size is always 1 px | allow; `largePoints = false` |
| `Presets.BlendAttachment.*` | blend factors `SRC_ALPHA`, `ONE_MINUS_SRC_ALPHA`, `DST_ALPHA`, `ONE_MINUS_DST_COLOR`, `DST_COLOR`, ... | all have WebGPU equivalents | 1:1 |
| `Presets.DepthStencil.*` | `LESS_OR_EQUAL`, stencil with default (all-zero) face state | supported | 1:1 |
| `VertexInput` (`Behavior.Scale`) | `*_USCALED` / `*_SSCALED` formats | no scaled vertex formats | reject or rewrite to `uint*`/`sint*` + shader conversion (needs shader rewrite) |
| `VertexInput` (`sbyte3`, `byte3`, `short3`, `ushort3`) | 3-component 8/16-bit formats (`R8G8B8_*`, `R16G16B16_*`) | WebGPU only has x1/x2/x4 for 8/16-bit types[^webgpu-spec] | reject; app must pad to 4 components |
| `VertexInput` (`float4x4`) | four `R32G32B32A32_SFLOAT` locations | fine (counts 4 of 16 attributes) | 1:1 |
| `DeviceExtensions.CreateDefaultRenderPass(sampleColor: true, msaa off)` | colour attachment `storeOp = DONT_CARE` with `finalLayout = SHADER_READ_ONLY_OPTIMAL` | WebGPU `"discard"` makes the attachment read back as zero[^webgpu-spec] | **this is a latent BRUTAL bug that becomes visible on WebGPU** (black offscreen target). Either patch BRUTAL to `STORE`, or have the ICD map `DONT_CARE` → `"store"` for images with `SAMPLED`/`TRANSFER_SRC` usage. |
| `DeviceEx.AllocateMemoryFor` | `VkMemoryAllocateFlagsInfo` (e.g. `DEVICE_ADDRESS_BIT`) | n/a | accept flags, ignore except `DEVICE_ADDRESS` (reject when BDA not supported) |
| `ImageEx.FullRangeOf` | `AspectMask = None` default | n/a | callers must set aspect; ICD treats `None` as "all" |

# Calls a real BRUTAL app adds (not in Abstractions)

The ImGui Vulkan backend shows the minimum a renderer adds on top of the helpers:[^brutal-imgui-vk]

| Group | Commands | Notes |
| --- | --- | --- |
| Surface/swapchain | `vkGetPhysicalDeviceSurfaceSupportKHR`, `vkGetPhysicalDeviceSurfaceCapabilitiesKHR`, `vkGetPhysicalDeviceSurfaceFormatsKHR`, `vkGetPhysicalDeviceSurfacePresentModesKHR`, `vkCreateSwapchainKHR`, `vkGetSwapchainImagesKHR`, `vkAcquireNextImageKHR`, `vkDestroySwapchainKHR`, `vkDestroySurfaceKHR` | surface creation itself is GLFW (`glfwCreateWindowSurface`) on desktop; the browser needs an ICD-specific canvas surface entry point |
| Render passes | `vkCreateFramebuffer`, `vkDestroyFramebuffer`, `vkCreateRenderPass`, `vkDestroyRenderPass`, `vkCmdBeginRenderPass`, `vkCmdEndRenderPass` | framebuffers referencing swapchain views must be late-bound |
| Pipelines | `vkCreateGraphicsPipelines`, `vkDestroyPipeline`, `vkDestroyPipelineLayout`, `vkCreateShaderModule`, `vkDestroyShaderModule` | ImGui uses `VK_DYNAMIC_STATE_VIEWPORT/SCISSOR` |
| Descriptors | `vkCreateSampler`, `vkDestroySampler`, `vkUpdateDescriptorSets` (`COMBINED_IMAGE_SAMPLER`) | combined sampler must be split |
| Commands | `vkCmdBindPipeline`, `vkCmdSetViewport`, `vkCmdSetScissor`, `vkCmdPushConstants` (8 + 8 bytes, vertex stage), `vkCmdBindVertexBuffers`, `vkCmdBindIndexBuffer` (`UINT16`), `vkCmdDrawIndexed`, `vkCmdCopyBufferToImage` (tightly packed rows, arbitrary sub-rectangles), `vkCmdPipelineBarrier` (layout transitions), `vkEndCommandBuffer` | `CopyBufferToImage` with `bufferRowLength = 0` and widths not multiple of 64 texels violates WebGPU's 256-byte `bytesPerRow`; the ICD must repack or turn it into `writeTexture` |
| Sync/teardown | `vkCreateSemaphore`, `vkDeviceWaitIdle`, `vkResetFences`, `vkWaitForFences`, `vkFlushMappedMemoryRanges` | — |
| Instance debug | `vkCreateDebugUtilsMessengerEXT`, `vkDestroyDebugUtilsMessengerEXT` | log only |

So the **PoC ICD surface** is the 74 helper commands plus ~31 app/ImGui commands (including `vkCmdDraw` and the ICD-specific canvas-surface creation): about **105 commands**, all rated T/E/M except push constants, mapping and waits (H), which have documented emulations.

# What the reference game uses beyond that (orientation only)

A scan of the decompiled KSA game code (never linked by the PoC) shows what a full game built on BRUTAL reaches for:[^ksa-devicectx][^ksa-bindless]

- **Enabled device features**: `samplerAnisotropy`, `multiview` (`VK_KHR_multiview`), `shaderDrawParameters`, `shaderDemoteToHelperInvocation`, `maintenance4`, `dynamicRendering`, `synchronization2`, descriptor indexing (`descriptorBindingSampledImageUpdateAfterBind`, `shaderSampledImageArrayNonUniformIndexing`, `descriptorBindingPartiallyBound`, `runtimeDescriptorArray`), portability-subset `mutableComparisonSamplers` and `triangleFans`, optional `VK_KHR_present_id`/`present_wait`, `VK_EXT_memory_budget`, optional acceleration structures + ray query + ray-tracing pipelines (`VK_KHR_buffer_device_address`, `VK_KHR_deferred_host_operations`).
- **Bindless**: `BindlessTextureLibrary` uses one set with binding 0 = sampled-image array of `MaxTextures` and binding 1 = 16 samplers, both `UPDATE_AFTER_BIND | PARTIALLY_BOUND`.
- **Commands beyond the PoC set**: `vkCmdBeginRendering/EndRendering`, `vkCmdPipelineBarrier2`, `vkCmdBlitImage`, `vkCmdCopyImage`, `vkCmdCopyImageToBuffer`, `vkCmdClearColorImage`, `vkCmdClearDepthStencilImage`, `vkCmdFillBuffer`, `vkCmdUpdateBuffer`, `vkCmdDispatch(Indirect)`, `vkCmdDrawIndexedIndirect`, `vkCmdSetDepthBias`, `vkCmdWriteTimestamp`, `vkCmdResetQueryPool`, `vkGetQueryPoolResults`, `vkGetFenceStatus`, `vkCmdTraceRaysKHR`, `vkCmdBuildAccelerationStructuresKHR`, `vkWaitForPresentKHR`, `vkCreatePipelineCache`.
- **Correction to the corpus map**: the many `DescriptorBuffer` hits in KSA code are `VkDescriptorBufferInfo` (core descriptor writes), not `VK_EXT_descriptor_buffer`; no `vkCmdBindDescriptorBuffersEXT`/`vkGetDescriptorEXT` calls were found.

What each of these needs on WebGPU is covered in [semantic gaps](/webgpu/semantic-gaps-and-emulation.md) and the [profile proposal](/webgpu/web-vulkan-profile-proposal.md).

# Open questions

- UNVERIFIED: whether .NET 10 browser-wasm (Mono interpreter and/or AOT) can execute `delegate* unmanaged[Cdecl]` calls whose targets are managed `[UnmanagedCallersOnly]` methods, for arbitrary signatures, without a native relink (`WasmBuildNative`). This decides between a managed and a native ICD.
- UNVERIFIED: whether `[LibraryImport("vulkan")]` + `SetDllImportResolver` can be satisfied at all in browser-wasm, or only through static linking of a library literally named `vulkan`.
- Whether the BRUTAL maintainers would accept an upstream "custom loader" hook (preferred over a fork).

[^brutal-abstractions]: Brutal.Vulkan.Abstractions source (BRUTAL commit a25cdf8)
[^brutal-manual]: Brutal.Vulkan Manual/Classes (Instance, PhysicalDevice, Device, Queue, CommandBuffer)
[^brutal-importresolver]: Brutal.Vulkan ImportResolver.cs (native loader bootstrap)
[^brutal-globals]: Generated LibraryImport("vulkan") declarations for the four global commands
[^brutal-fntables]: Generated VkDevice.FunctionTable (eager vkGetDeviceProcAddr of every device command)
[^brutal-imgui-vk]: BRUTAL Dear ImGui Vulkan renderer backend (VulkanBackend.cs + VulkanBackend.Helpers.cs)
[^ksa-devicectx]: KSA decompiled device bootstrap (reference only)
[^ksa-bindless]: KSA decompiled bindless texture library (reference only)
[^webgpu-spec]: WebGPU W3C Editor's Draft, 8 October 2026
