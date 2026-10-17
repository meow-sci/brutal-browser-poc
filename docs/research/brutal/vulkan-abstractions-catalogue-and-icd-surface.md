---
type: Code Analysis
title: Brutal.Vulkan.Abstractions catalogue and the Vulkan command surface it needs (minimal ICD surface)
description: Every public type in Brutal.Vulkan.Abstractions 2026.10.2 with the Vulkan commands it issues, a complete vk-function → caller table for the Abstractions plus Brutal.Vulkan/Manual classes, and the Vulkan semantics in that surface that are hard to emulate on WebGPU.
tags: [brutal, vulkan, abstractions, icd, webgpu, api-surface, memory-mapping, synchronization]
sources:
  - id: deviceex
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/Classes/DeviceEx.cs
    title: DeviceEx (Device + IGlobalAllocator)
  - id: bufferex
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/BufferEx.cs
    title: BufferEx
  - id: imageex
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/ImageEx.cs
    title: ImageEx
  - id: imageviewex
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/ImageViewEx.cs
    title: ImageViewEx
  - id: mappable
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/IMappable.cs
    title: IMappable / AllocationMapInfo / MappableExtensions
  - id: mapped
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/MappedMemory.cs
    title: MappedMemory
  - id: staging
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/StagingPool.cs
    title: StagingPool
  - id: frameres
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/FrameResources.cs
    title: FrameResources
  - id: syncobj
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/SyncObjects.cs
    title: SyncObjects
  - id: descpool
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/DescriptorPoolEx.cs
    title: DescriptorPoolEx
  - id: desclayout
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/DescriptorSetLayoutEx.cs
    title: DescriptorSetLayoutEx
  - id: vertexinput
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/VertexInput.cs
    title: VertexInput (reflection-based vertex layout)
  - id: presets
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/Presets.cs
    title: Presets (PtrOwner-backed pipeline state presets)
  - id: devext
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/Extensions/DeviceExtensions.cs
    title: Extensions/DeviceExtensions.cs
  - id: cmdext
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/Extensions/CommandBufferExtensions.cs
    title: Extensions/CommandBufferExtensions.cs
  - id: queueext
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/Extensions/QueueExtensions.cs
    title: Extensions/QueueExtensions.cs
  - id: physext
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/Extensions/PhysicalDeviceExtensions.cs
    title: Extensions/PhysicalDeviceExtensions.cs
  - id: shaderext
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/Extensions/ShaderModuleExtensions.cs
    title: Extensions/ShaderModuleExtensions.cs
  - id: debugname
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/DebugNameExtensions.cs
    title: DebugNameExtensions ([Conditional("DEBUG")])
  - id: helpers
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/VulkanHelpers.cs
    title: VulkanHelpers
  - id: devinfo
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/VulkanDeviceInfo.cs
    title: VulkanDeviceInfo
  - id: bindbuf
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/IBindBuffer.cs
    title: IBindBuffer / BindBufferExtensions
  - id: shaderstages
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/Shaders/ShaderStages.cs
    title: ShaderStages
  - id: manual-classes
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Manual/Classes/
    title: Brutal.Vulkan/Manual/Classes (Instance, PhysicalDevice, Device, Queue, CommandBuffer)
  - id: usage-scan
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/usage.txt
    title: Automated call-site scan (wrapper name → vk function), manually de-duplicated for this table
  - id: imgui-vk
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ImGui.Backends/RendererBackends/VulkanBackend.cs
    title: Brutal.ImGui.Backends Vulkan renderer backend (secondary consumer)
  - id: asminfo
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/asminfo-full.txt
    title: Metadata dump (Abstractions DLL is a Release build; 71 public top-level types)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:16:59Z }
status: draft
---

# What Abstractions is (and is not)

`Brutal.Vulkan.Abstractions` (NuGet 2026.10.2, 47 source files, 7,590 lines, a **Release** build with 71 public top-level types)[^asminfo] is a thin convenience layer over `Brutal.Vulkan`. It contains:

* a **direct-from-device memory allocator** (`DeviceEx`), with one `VkDeviceMemory` per buffer or image;
* RAII value wrappers (`BufferEx`, `ImageEx`, `ImageViewEx`, `MappedMemory`);
* a staging and upload helper (`StagingPool`);
* bookkeeping wrappers for descriptor pools and layouts;
* pipeline-state presets, a reflection-based `VertexInput` builder, and CPU-only buffer and image sub-allocators ("partitioners");
* small helpers (render-pass builder, extension lists, debug names and labels).

It has **no swapchain helper, no surface helper, no frame loop and no pipeline builder**. Those are written by each app against raw `Brutal.Vulkan` (KSA does exactly this; see [/brutal/ksa-bootstrap-reference.md](/brutal/ksa-bootstrap-reference.md)). Its one render-pass helper builds a **legacy `VkRenderPass`** (`DeviceExtensions.CreateDefaultRenderPass`), and `FrameResources` holds a `VkFramebuffer`. Nothing in Abstractions uses dynamic rendering (`vkCmdBeginRendering`), although the generated `cmd.BeginRendering(in VkRenderingInfo)` is available.[^devext][^frameres]

# Catalogue of public types and the Vulkan commands they issue

| Type (file) | Role | Vulkan commands issued (via generated wrappers) |
|---|---|---|
| `DeviceEx : Device, IGlobalAllocator` (`Classes/DeviceEx.cs`) | Created by `physicalDevice.CreateDevice(DeviceEx.CreateInfo)`; the ctor is internal. `CreateInfo` has `HashSet<string> Extensions`, `Ptr<VkPhysicalDeviceFeatures> Features`, `ReadOnlySpan<VkDeviceQueueCreateInfo> QueueCreateInfos`, `Ptr Next` and `VkAllocator? Allocator`. `AllocateBuffer`/`AllocateImage` do **one `vkAllocateMemory` per resource**, choosing the memory type with `PhysicalDevice.FindMemoryType(bits, required, preferred)`. `PreferGpu` maps to `DeviceLocal`; `PreferCpu` maps to `HostVisible \| HostCoherent`. `IAllocator.MapMemory` keeps a **ref-counted map per buffer allocation** in a native `BufferAllocationHandle` and only allows nested maps that are a subset of the first map.[^deviceex] | `vkCreateDevice`, `vkGetDeviceProcAddr`, `vkCreateBuffer`, `vkGetBufferMemoryRequirements`, `vkGetPhysicalDeviceMemoryProperties`, `vkAllocateMemory` (+ `VkMemoryAllocateFlagsInfo` when `AllocateFlags` is set), `vkBindBufferMemory`, `vkCreateImage`, `vkGetImageMemoryRequirements`, `vkBindImageMemory`, `vkMapMemory`, `vkUnmapMemory`, `vkDestroyBuffer`, `vkDestroyImage`, `vkFreeMemory`, `vkDestroyDevice`; `vkSetDebugUtilsObjectNameEXT` only in a DEBUG build of Abstractions (see `DebugNameExtensions`) |
| `BufferEx` (readonly struct) + `BufferExExtensions.CreateBuffer(this IBufferAllocator, BufferEx.CreateInfo)` | `CreateInfo` flattens `VkBufferCreateInfo` and allocation preferences; `Name` is `required`. `IMappable`, `IBindBuffer`. `GetDeviceAddress()`[^bufferex] | via `DeviceEx`; `vkGetBufferDeviceAddress` |
| `ImageEx` (readonly struct) + `ImageExExtensions.CreateImage(this IImageAllocator, ImageEx.CreateInfo)` | Image plus memory. `CreateImageView(viewType, range, allocator)` returns `ImageViewEx`. `MaxMipLevels`[^imageex] | via `DeviceEx`; `vkCreateImageView` |
| `ImageViewEx` (readonly struct) | Owns a `VkImageView`; `Dispose` destroys it; `AsPartition()`[^imageviewex] | `vkDestroyImageView` |
| `AllocationInfo`, `MemoryPreference`, `IAllocator`, `IBufferAllocator`, `IImageAllocator`, `IGlobalAllocator`, `BufferAllocationInfo`, `ImageAllocationInfo` (`Allocator/*`) | Allocator abstraction, also implemented by `Brutal.Vulkan.Vma` (native VMA) | — |
| `IMappable`, `AllocationMapInfo`, `MapRegion`, `MapInfo`, `MappableExtensions.Map(...)` | `buffer.Map()` / `buffer.Map(new IMappable.MapInfo{ Region = … })` returns `MappedMemory`. `WHOLE_SIZE` is resolved to the buffer size[^mappable] | `vkMapMemory` (through the allocator) |
| `MappedMemory` (readonly struct, `IDisposable`) | `MapPtr`, `As<T>()`, `AsSpan<T>()`, `Offset(...)`, `Flush()`, `Invalidate()`, and `Dispose()` → `Unmap()`[^mapped] | `vkUnmapMemory`, `vkFlushMappedMemoryRanges`, `vkInvalidateMappedMemoryRanges` |
| `StagingPool` + `StagingPoolExtensions.CreateStagingPool(this IBufferAllocator, Queue, int initialCmdBuffers, level)` | Owns a command pool on the queue family, a list of `CommandBuffer`, fences and size-bucketed **host-coherent** staging `BufferEx`. Methods: `NextCommandBuffer()`, `AddStagingBuffer(size)`/`AddStagingBuffer<T>(Span<T>)` (maps, copies, unmaps), `Submit(waits, stages, signals)` (creates or reuses a fence), `WaitFor(nint timeout)`, `Wait()` (= `WaitFor(-1)`), `Dispose()` (Submit + Wait + destroy)[^staging] | `vkCreateCommandPool`, `vkAllocateCommandBuffers`, `vkCreateFence`, `vkQueueSubmit`, `vkWaitForFences`, `vkResetFences`, `vkResetCommandPool`, `vkDestroyCommandPool`, `vkDestroyFence`, plus buffer create, map and destroy |
| `FrameResources(Device)` (class) | Plain holder: `Framebuffer`, color, depth and multisampled image + memory + view, `OwnsColorImage`, `FrameIndex`. `Dispose` destroys everything, **passing possibly-null handles**[^frameres] | `vkDestroyImageView` ×3, `vkDestroyImage` ×1–3, `vkFreeMemory` ×1–3, `vkDestroyFramebuffer` |
| `SyncObjects(Device)` (class) | Holder: `ImageReadySemaphore`, `RenderingFinishedSemaphore`, `Fence`, `CommandBuffer` and `CommandPool`. The app creates them; this class only destroys them[^syncobj] | `vkFreeCommandBuffers`, `vkDestroyFence`, `vkDestroySemaphore` ×2 |
| `DescriptorPoolEx` (`VulkanHandleWrapper<VkDescriptorPool, VkEmptyTable>`) + `DescriptorPoolExExtensions` | Tracks `MaxSets`, `RemainingSets` and per-type `PoolSize` budgets in managed code, and throws `DescriptorPoolOutOfMemoryException` or `DescriptorPoolInvalidOperationException` **before** calling Vulkan. Maps each set to its layout. `CreateDescriptorPool(this Device, CreateInfo, VkAllocator?)`, `AllocateDescriptorSet(s)`, `FreeDescriptorSet(s)` (requires `FreeDescriptorSetBit`)[^descpool] | `vkCreateDescriptorPool`, `vkAllocateDescriptorSets`, `vkFreeDescriptorSets`, `vkDestroyDescriptorPool` |
| `DescriptorSetLayoutEx` + `DescriptorSetLayoutExExtensions.CreateDescriptorSetLayout(this Device, CreateInfo, VkAllocator?)` | Records descriptor counts per type for the pool bookkeeping[^desclayout] | `vkCreateDescriptorSetLayout`, `vkDestroyDescriptorSetLayout` |
| `IDescriptorSetLayoutWrapper`, `IDescriptorSetWrapper` | Interfaces (set + dynamic offsets) used by `BindDescriptorSet` | — |
| `DeviceExtensions` (static) | `CreateDefaultRenderPass(sampleColor, sampleDepth, samples, colorFmt, depthFmt, resolvedFmt, alloc)` builds a **1-subpass render pass with optional MSAA color resolve**; the color `finalLayout` is `ShaderReadOnlyOptimal` (render-to-texture). Also `CreatePipelineLayout(setLayouts, pushRanges, alloc)`, `CreateGraphicsPipeline(cache, info, alloc)`, `CreateComputePipeline`, `GetBufferDeviceAddress`, `AllocateCommandBuffer(s)` (returning `CommandBuffer`), `AllocateMemory(physDev, buffer/image, required, alloc)`, `MapMemory<T>(MapInfo)`, `WriteMemory<T>(span, memory, offset)` (map + copy + unmap), `BindDescriptorSet(...)` ×2 (one direct `PInvoke.vkCmdBindDescriptorSets` call), `CreateFence(flags)`, `WaitForFence(fence, nint timeout)`, `FreeCommandBuffers(pool, ReadOnlySpan<CommandBuffer>)`, `ExecuteCommands(...)`[^devext] | `vkCreateRenderPass`, `vkCreatePipelineLayout`, `vkCreateGraphicsPipelines`, `vkCreateComputePipelines`, `vkGetBufferDeviceAddress`, `vkAllocateCommandBuffers`, `vkGetBuffer/ImageMemoryRequirements`, `vkAllocateMemory`, `vkMapMemory`, `vkUnmapMemory`, `vkCmdBindDescriptorSets`, `vkCreateFence`, `vkWaitForFences`, `vkFreeCommandBuffers`, `vkCmdExecuteCommands` |
| `CommandBufferExtensions` + `DebugLabelScope` | `Reset()`, `Begin()`, `Begin(usage)`, `Begin(usage, inheritance)`, `BeginDebugLabel(label, color)`, `DebugScope(label)`, `EndDebugLabel()`, `CopyBuffer(src, dst, region/size)`, `BindVertexBuffer(binding, buffer, offset)`, `PushConstants<T>(layout, stages, offset, T / ReadOnlySpan<T>)` (direct `PInvoke.vkCmdPushConstants`)[^cmdext] | `vkResetCommandBuffer`, `vkBeginCommandBuffer`, **`vkCmdBeginDebugUtilsLabelEXT`/`vkCmdEndDebugUtilsLabelEXT` (unconditional; no check that `VK_EXT_debug_utils` is enabled)**, `vkCmdCopyBuffer`, `vkCmdBindVertexBuffers`, `vkCmdPushConstants` |
| `QueueExtensions` | `Submit(waitSems, waitStages, cmdBufs, signalSems, fence)` and a **timeline** overload with `VkTimelineSemaphoreSubmitInfo` (wait and signal values)[^queueext] | `vkQueueSubmit` (via the `Lock`-guarded `Queue.Submit`) |
| `PhysicalDeviceExtensions` | `CreateDevice(this PhysicalDevice, DeviceEx.CreateInfo)` returns `DeviceEx`; `FindSupportedFormat(candidates, tiling, features)`; `FindQueueFamily(required, excluded)` picks the family with the fewest extra flags[^physext] | `vkGetPhysicalDeviceFormatProperties`, `vkGetPhysicalDeviceQueueFamilyProperties` |
| `ShaderModuleExtensions.FromSpirv(Device, ReadOnlySpan<byte> spirv, VkAllocator?)` | Plain SPIR-V to `VkShaderModule`; **expects SPIR-V words**[^shaderext] | `vkCreateShaderModule` |
| `ShaderStages(Device, Span<VkPipelineShaderStageCreateInfo>, alloc)` | `NativeList` of stage infos; implicitly converts to `VkPipelineShaderStageCreateInfo*`; `Dispose` destroys modules[^shaderstages] | `vkDestroyShaderModule` |
| `Presets` | Static, **`PtrOwner`-backed (native memory)** pipeline-state presets: `BlendState`, `BlendAttachment`, `DepthStencil`, `InputAssembly`, `Multisample.None`, `Rasterization.Fill/Line.Cull*`, `Components.Default`, `Entrypoint.Main` (`"main"u8`), `ShaderStage.Vertex/Fragment/...`, and `Sampler.*` create-infos[^presets] | — |
| `VertexInput`, `Behavior`, `FixedPointConversionAttribute`, `VertexFormatAttribute` | `VertexInput.For<TVertex>()` / `For<TVertex,TInstance>()` derive `VkVertexInputBindingDescription`/`AttributeDescription` by **reflection over struct fields** (`GetFields(Instance\|Public\|NonPublic)`, cached in a `ConcurrentDictionary`). It maps Brutal.Numerics types (`float3`, `sbyte4`, …) to `VkFormat`; offsets come from the cumulative field sizes; implicitly converts to `VkPipelineVertexInputStateCreateInfo*`[^vertexinput] | — |
| `IBindBuffer` + `BindBufferExtensions` | `BindVertexBuffer(s)<T>`, `BindIndexBuffer<T>`, `DrawIndirect<T>` for `BufferEx` and partitions[^bindbuf] | `vkCmdBindVertexBuffers`, `vkCmdBindIndexBuffer`, `vkCmdDrawIndirect` |
| Partitioners: `IBufferPartitioner`, `LinearBufferPartitioner`, `BinaryTreeBufferPartitioner`, `BufferPartitionInfo`, `IImageViewPartitioner`, `Atlas2DPartitioner`, `QuadTreePartitioner`, `TextureArrayPartitioner`, `ImageViewPartitionInfo`, `ImageStagingRange` | CPU-side sub-allocation inside a buffer or image (offsets, rects, layers) | none (pure managed) |
| `FormatExtensions`, `Subresources`, `SubresourceExtensions`, `ByteSizeExtensions`, `DisposableExtensions`, `InstanceExtensions` (empty) | Pure helpers | none |
| `DebugNameExtensions.SetDebugName<T>(this Device, T handle, string / ReadOnlySpan<byte>)` | **`[Conditional("DEBUG")]`**, so calls compile away in callers built without `DEBUG`, including the Release NuGet `DeviceEx`. At runtime it also checks `Instance.IsExtensionEnabled(VK_EXT_debug_utils)`[^debugname] | `vkSetDebugUtilsObjectNameEXT` |
| `VulkanHelpers` | `MaxMipLevels`, `Api.VERSION_1_0…1_4`, `Validation.IsSupported` (static ctor), `AddPlatformRequiredExtensions` (macOS only, via **`OperatingSystem.IsMacOS()`**: portability enumeration + get_physical_device_properties2), `AddDebugExtensions`, `AddSurfaceExtensions` (`VK_KHR_surface`), `AddSwapchainExtensions`, `AddValidationLayers`, `GetPlatformRequiredInstanceCreateFlags` (macOS → `EnumeratePortabilityBitKHR`), `AddMandatoryIfSupportedDeviceExtensions` (`VK_KHR_portability_subset` if supported), `BuildDefaultDebugMessengerCreateInfo`, and `DebugUtilsMessengerCallbacks.Quiet/Default/Verbose` (`[UnmanagedCallersOnly]`)[^helpers] | `vkEnumerateInstanceLayerProperties` (global P/Invoke); `vkEnumerateDeviceExtensionProperties` (through `SupportsExtension`) |
| `VulkanDeviceInfo` | `GetDeviceName`, `FormatApiVersion`, `FormatDriverVersion` (Intel formatting under `OperatingSystem.IsWindows()`)[^devinfo] | `vkGetPhysicalDeviceProperties` |

The `OperatingSystem.Is*` uses in Abstractions only pick macOS portability extensions and Windows Intel driver-version formatting. On browser both evaluate to false and the generic path runs, so Abstractions needs **no patch** for them.[^helpers][^devinfo]

# vk function → used by (Abstractions + Brutal.Vulkan Manual classes)

This is the **complete** set of Vulkan commands reachable from Abstractions and from the hand-written classes in `Brutal.Vulkan/Manual/Classes`. It was derived by mapping every called wrapper name to the `PInvoke.vkXxx` it invokes and then removing name collisions by hand.[^usage-scan][^manual-classes] **74 commands** in total: 4 global or loader (`vkGetInstanceProcAddr`, `vkGetDeviceProcAddr`, `vkCreateInstance`, `vkEnumerateInstanceLayerProperties`), 8 instance and physical-device, 62 device, queue and command-buffer. Most are reached only when the app calls the corresponding helper. The few that BRUTAL invokes unconditionally while constructing `Instance`, `Device` and `Queue` are marked "hard" in [/brutal/vulkan-loader-and-proc-addr-contract.md](/brutal/vulkan-loader-and-proc-addr-contract.md). A BRUTAL app that relies on Abstractions plus its own swapchain and draw code needs these plus the "app-level" list after the table.

| Vulkan command | Used by (type.method, file:line) |
|---|---|
| `vkGetInstanceProcAddr` | `static Vulkan()` (ImportResolver.cs:42); `VkInstance.FunctionTable` ctor; `Device` ctor (Device.cs:84) for `"vkGetDeviceProcAddr"` |
| `vkCreateInstance` | `Instance` ctor (Instance.cs:88) |
| `vkEnumerateInstanceLayerProperties` | `VulkanHelpers.Validation.CheckSupport` (VulkanHelpers.cs:65–66) |
| `vkDestroyInstance` | `Instance.Dispose`/finalizer (Instance.cs:156) |
| `vkEnumeratePhysicalDevices` | `Instance` ctor (Instance.cs:94, 97) |
| `vkGetPhysicalDeviceProperties` | `VulkanDeviceInfo.GetDeviceName` (VulkanDeviceInfo.cs:26) |
| `vkGetPhysicalDeviceQueueFamilyProperties` | `PhysicalDevice.GetFirstQueueFamilyWithFlag` (PhysicalDevice.cs:82–83); `PhysicalDeviceExtensions.FindQueueFamily` (PhysicalDeviceExtensions.cs:55–57) |
| `vkGetPhysicalDeviceMemoryProperties` | `PhysicalDevice.FindMemoryType` ×2 (PhysicalDevice.cs:101, 136), so every `DeviceEx.AllocateBuffer`/`AllocateImage` and `DeviceExtensions.AllocateMemory` |
| `vkGetPhysicalDeviceFormatProperties` | `PhysicalDeviceExtensions.FindSupportedFormat` (PhysicalDeviceExtensions.cs:26) |
| `vkEnumerateDeviceExtensionProperties` | `PhysicalDevice.ExtensionNamesUtf8` (PhysicalDevice.cs:50–51), so `SupportsExtension`, `ExtensionNames` and `VulkanHelpers.AddMandatoryIfSupportedDeviceExtensions` |
| `vkCreateDevice` | `Device` ctor (Device.cs:79), and therefore `DeviceEx` |
| `vkGetDeviceProcAddr` | `Device` ctor (Device.cs:85); `VkDevice.FunctionTable` ctor |
| `vkDestroyDevice` | `Device.Dispose` (Device.cs:122) |
| `vkGetDeviceQueue` | `Queue` ctor (Queue.cs:22) via `Device.GetQueue` |
| `vkQueueSubmit` | `Queue.Submit` (Queue.cs:77) ← `QueueExtensions.Submit` ×2 (QueueExtensions.cs:54, 133) ← `StagingPool.Submit` (StagingPool.cs:160) |
| `vkQueueSubmit2` | `Queue.Submit2` (Queue.cs:85); app only |
| `vkQueueWaitIdle` | `Queue.WaitIdle` (Queue.cs:37); app only |
| `vkQueueBindSparse` | `Queue.BindSparse` (Queue.cs:45); app only |
| `vkQueuePresentKHR` | `Queue.PresentKHR` (Queue.cs:93); app only |
| `vkQueueBegin/End/InsertDebugUtilsLabelEXT` | `Queue.Begin/End/InsertDebugUtilsLabelEXT` (Queue.cs:53, 61, 69); app only |
| `vkAllocateMemory` | `DeviceEx.AllocateMemoryFor` (DeviceEx.cs:63, 80); `DeviceExtensions.AllocateMemory` ×2 |
| `vkFreeMemory` | `DeviceEx.FreeAllocation` ×2 (DeviceEx.cs:146, 164); `FrameResources.Dispose` (FrameResources.cs:55, 60, 61) |
| `vkMapMemory` | `DeviceEx.IAllocator.MapMemory` (DeviceEx.cs:243) ← `MappedMemory` ctor (MappedMemory.cs:39) ← `MappableExtensions.Map`; `DeviceExtensions.MapMemory`/`WriteMemory` |
| `vkUnmapMemory` | `DeviceEx.IAllocator.UnmapMemory` (DeviceEx.cs:274) ← `MappedMemory.Unmap/Dispose`; `DeviceExtensions.WriteMemory` |
| `vkFlushMappedMemoryRanges` | `MappedMemory.Flush` (MappedMemory.cs:88) |
| `vkInvalidateMappedMemoryRanges` | `MappedMemory.Invalidate` (MappedMemory.cs:107) |
| `vkCreateBuffer` | `DeviceEx.AllocateBuffer` (DeviceEx.cs:182) ← `BufferEx` ← `StagingPool.AddStagingBuffer` |
| `vkDestroyBuffer` | `DeviceEx.FreeAllocation(BufferAllocationInfo)` (DeviceEx.cs:163) |
| `vkGetBufferMemoryRequirements` | `DeviceEx.AllocateBuffer` (DeviceEx.cs:185); `DeviceExtensions.AllocateMemory(…, VkBuffer, …)` |
| `vkBindBufferMemory` | `DeviceEx.AllocateBuffer` (DeviceEx.cs:195) |
| `vkGetBufferDeviceAddress` | `BufferEx.GetDeviceAddress` (BufferEx.cs:177); `DeviceExtensions.GetBufferDeviceAddress` |
| `vkCreateImage` | `DeviceEx.AllocateImage` (DeviceEx.cs:98) ← `ImageEx` |
| `vkDestroyImage` | `DeviceEx.FreeAllocation(ImageAllocationInfo)` (DeviceEx.cs:145); `FrameResources.Dispose` |
| `vkGetImageMemoryRequirements` | `DeviceEx.AllocateImage` (DeviceEx.cs:100); `DeviceExtensions.AllocateMemory(…, VkImage, …)` |
| `vkBindImageMemory` | `DeviceEx.AllocateImage` (DeviceEx.cs:111) |
| `vkCreateImageView` | `ImageEx.CreateImageView` (ImageEx.cs:237) |
| `vkDestroyImageView` | `ImageViewEx.Dispose` (ImageViewEx.cs:39); `FrameResources.Dispose` ×3 |
| `vkDestroyFramebuffer` | `FrameResources.Dispose` (FrameResources.cs:63) |
| `vkCreateRenderPass` | `DeviceExtensions.CreateDefaultRenderPass` (DeviceExtensions.cs:209) |
| `vkCreateShaderModule` | `ShaderModuleExtensions.FromSpirv` (ShaderModuleExtensions.cs:7) |
| `vkDestroyShaderModule` | `ShaderStages.Dispose` (ShaderStages.cs:25) |
| `vkCreatePipelineLayout` | `DeviceExtensions.CreatePipelineLayout` (DeviceExtensions.cs:247) |
| `vkCreateGraphicsPipelines` | `DeviceExtensions.CreateGraphicsPipeline` (DeviceExtensions.cs:282) |
| `vkCreateComputePipelines` | `DeviceExtensions.CreateComputePipeline` (DeviceExtensions.cs:314) |
| `vkCreateDescriptorSetLayout` / `vkDestroyDescriptorSetLayout` | `DescriptorSetLayoutEx` ctor / `Dispose` (DescriptorSetLayoutEx.cs:56, 70) |
| `vkCreateDescriptorPool` / `vkDestroyDescriptorPool` | `DescriptorPoolEx` ctor / `Dispose` (DescriptorPoolEx.cs:76, 220) |
| `vkAllocateDescriptorSets` | `DescriptorPoolEx.AllocateDescriptorSet(s)` (DescriptorPoolEx.cs:89, 112, 141) |
| `vkFreeDescriptorSets` | `DescriptorPoolEx.FreeDescriptorSets` (DescriptorPoolEx.cs:214) |
| `vkCreateCommandPool` / `vkDestroyCommandPool` / `vkResetCommandPool` | `StagingPool` ctor / `Dispose` / `RewindCommandBuffers` (StagingPool.cs:35, 257, 224) |
| `vkAllocateCommandBuffers` | `DeviceExtensions.AllocateCommandBuffer(s)` (DeviceExtensions.cs:350, 373) ← `StagingPool` |
| `vkFreeCommandBuffers` | `DeviceExtensions.FreeCommandBuffers` (DeviceExtensions.cs:663) ← `SyncObjects.Dispose` |
| `vkResetCommandBuffer` / `vkBeginCommandBuffer` | `CommandBufferExtensions.Reset` / `Begin` ×3 (CommandBufferExtensions.cs:29, 39, 129, 150) |
| `vkCreateFence` / `vkDestroyFence` / `vkResetFences` / `vkWaitForFences` | `DeviceExtensions.CreateFence`, `StagingPool.Submit` / `StagingPool.Dispose`, `SyncObjects.Dispose` / `StagingPool.ReclaimSignalled` / `DeviceExtensions.WaitForFence`, `StagingPool.WaitFor` |
| `vkDestroySemaphore` | `SyncObjects.Dispose` ×2 (SyncObjects.cs:47–48) |
| `vkCmdCopyBuffer` | `CommandBufferExtensions.CopyBuffer` (CommandBufferExtensions.cs:170) |
| `vkCmdBindVertexBuffers` / `vkCmdBindIndexBuffer` / `vkCmdDrawIndirect` | `CommandBufferExtensions.BindVertexBuffer`, `BindBufferExtensions.*` (IBindBuffer.cs:60–148) |
| `vkCmdBindDescriptorSets` | `DeviceExtensions.BindDescriptorSet` ×2 (DeviceExtensions.cs:563 via wrapper, 592 direct `PInvoke`) |
| `vkCmdPushConstants` | `CommandBufferExtensions.PushConstants<T>` (CommandBufferExtensions.cs:237 via wrapper, 262 direct `PInvoke`) |
| `vkCmdExecuteCommands` | `DeviceExtensions.ExecuteCommands` (DeviceExtensions.cs:692) |
| `vkCmdBeginDebugUtilsLabelEXT` / `vkCmdEndDebugUtilsLabelEXT` | `CommandBufferExtensions.BeginDebugLabel` / `EndDebugLabel` / `DebugScope` (CommandBufferExtensions.cs:61, 118) |
| `vkSetDebugUtilsObjectNameEXT` | `DebugNameExtensions.SetDebugName` (DebugNameExtensions.cs:81), DEBUG builds only |

Line numbers in the second column refer to the files as printed with doc comments stripped. Use them to find the method, not as exact line numbers.

**App-level commands that Abstractions does *not* wrap but any presenting app needs:** `vkGetPhysicalDeviceSurfaceSupportKHR`, `…SurfaceCapabilitiesKHR`, `…SurfaceFormatsKHR`, `…SurfacePresentModesKHR`, `vkDestroySurfaceKHR`, `vkCreateSwapchainKHR`, `vkGetSwapchainImagesKHR`, `vkAcquireNextImageKHR`, `vkDestroySwapchainKHR`, `vkCreateSemaphore`, `vkCreateFramebuffer` (render-pass path), `vkCreateSampler`, `vkUpdateDescriptorSets`, `vkCmdBeginRenderPass`/`vkCmdEndRenderPass` or `vkCmdBeginRendering`/`vkCmdEndRendering`, `vkCmdBindPipeline`, `vkCmdSetViewport`, `vkCmdSetScissor`, `vkCmdDraw`/`vkCmdDrawIndexed`, `vkCmdPipelineBarrier(2)`, `vkCmdCopyBufferToImage`, `vkEndCommandBuffer`, `vkDeviceWaitIdle`, `vkDestroyPipeline`, `vkDestroyPipelineLayout`, `vkDestroyRenderPass`, `vkDestroySampler`. `Brutal.ImGui.Backends`' Vulkan renderer, the second in-tree consumer, uses exactly this style: render pass + framebuffer, `vkCreateSwapchainKHR`, `vkUpdateDescriptorSets`, `vkCmdSetViewport/Scissor`, `vkCmdDrawIndexed`, `vkCmdPipelineBarrier`, and `vkFlushMappedMemoryRanges`, with SPIR-V embedded in C#.[^imgui-vk]

# Semantics in this surface that are hard on WebGPU

| Vulkan behaviour relied on | Where | WebGPU gap / implementation strategy (detail in `/webgpu/`) |
|---|---|---|
| **Host-visible, host-coherent, (persistently) mapped memory.** `DeviceEx` maps the whole range and ref-counts nested maps; `MappedMemory.AsSpan<T>()` hands out a `Span` over the raw pointer; `StagingPool` requires `HostVisible\|HostCoherent`; `PreferCpu` → `HostVisible\|HostCoherent` | DeviceEx.cs:222–276; MappedMemory.cs; StagingPool.cs:94–100[^deviceex][^mapped][^staging] | WebGPU only maps `MAP_WRITE\|COPY_SRC` or `MAP_READ\|COPY_DST` buffers, asynchronously, and invalidates the JS view on unmap. The practical emulation: back every `HOST_VISIBLE` `VkDeviceMemory` with a shadow block in wasm linear memory (what `vkMapMemory` returns). Push dirty ranges with `GPUQueue.writeBuffer` at `vkQueueSubmit`, which is when host-coherent writes become visible under Vulkan rules, and at `vkFlushMappedMemoryRanges`/`vkUnmapMemory`. GPU→CPU readback (`Invalidate`, `HOST_CACHED`) needs `mapAsync`, which a synchronous `vkWaitForFences` cannot await on the browser main thread |
| Memory-type selection by property flags | `PhysicalDevice.FindMemoryType` | The implementation decides the memory types it advertises. A single `DEVICE_LOCAL\|HOST_VISIBLE\|HOST_COHERENT` type, as on UMA hardware, satisfies both `PreferGpu` and staging lookups |
| **One allocation per resource**, with `vkBindBufferMemory` at offset 0 | DeviceEx.AllocateBuffer/AllocateImage | Maps well: create the `GPUBuffer`/`GPUTexture` at bind time with the size from `VkBufferCreateInfo` and the usage flags translated |
| **Fences with blocking waits**: `StagingPool.Wait()` = `vkWaitForFences(…, timeout = -1)`; `WaitForFence(fence, nint)` | StagingPool.cs:180, 240[^staging] | A main-thread wasm app cannot block on GPU completion. WebGPU executes submissions in order, so the implementation can treat fences as signalled at submit, plus `onSubmittedWorkDone` for real completion. Only readback really needs completion |
| **Binary semaphores** for acquire/present, **timeline semaphores** via `QueueExtensions.Submit(…, values …)` | QueueExtensions.cs[^queueext] | Single queue, in-order execution: binary semaphores can be no-ops. Timeline values can be tracked CPU-side |
| **Command-buffer reuse** after `vkResetCommandPool`, not one-time-submit; secondary buffers via `vkCmdExecuteCommands` | StagingPool.RewindCommandBuffers; DeviceExtensions.ExecuteCommands[^staging][^devext] | `GPUCommandBuffer` is single-use. The implementation should record into its own command list and replay into a fresh `GPUCommandEncoder` at submit, inlining secondaries; render bundles are optional |
| **Legacy render passes + framebuffers + MSAA resolve** (`CreateDefaultRenderPass`, `FrameResources.MultisampledImage`) | DeviceExtensions.cs:5–207; FrameResources.cs[^devext][^frameres] | Maps to `GPURenderPassDescriptor` (loadOp, storeOp, `resolveTarget`). WebGPU supports **sampleCount 1 or 4 only**. Subpass dependencies and image layouts are ignored |
| **Push constants** (`vkCmdPushConstants`, two paths) | CommandBufferExtensions.cs:237–262[^cmdext] | WebGPU core has no push constants. Emulate with a per-draw uniform ring buffer and dynamic offsets, or a WebGPU immediate-data feature if available (see `/webgpu/`) |
| **Descriptor sets updated after allocation** (`vkUpdateDescriptorSets`); pools with free-able sets | DescriptorPoolEx; app code | `GPUBindGroup` is immutable. Create or recreate bind groups lazily at `vkCmdBindDescriptorSets`/draw time from the current descriptor contents |
| `vkCmdCopyBuffer` with arbitrary sizes | CommandBufferExtensions.CopyBuffer | `copyBufferToBuffer` needs 4-byte-aligned offsets and sizes. Pad or fall back to `writeBuffer` |
| `vkGetBufferDeviceAddress` (`BufferEx.GetDeviceAddress`) | BufferEx.cs:177[^bufferex] | No equivalent; unsupported |
| Debug labels issued without checking for `VK_EXT_debug_utils` | CommandBufferExtensions.BeginDebugLabel[^cmdext] | Return real functions for `vkCmdBegin/EndDebugUtilsLabelEXT` (`pushDebugGroup`/`popDebugGroup`) even if the extension was not enabled; a NULL here crashes |
| Destroy calls with `VK_NULL_HANDLE` | FrameResources.Dispose[^frameres] | Destroy and free entry points must accept null handles, as the Vulkan spec allows |
| `VK.WHOLE_SIZE` widened from 32-bit `nuint` | MapRegion default, `Map`, `Flush` | On wasm32, treat `0xFFFFFFFF` as WHOLE_SIZE (see [/brutal/vulkan-binding-architecture.md](/brutal/vulkan-binding-architecture.md)) |
| SPIR-V only (`FromSpirv`, `ShaderStages`, `Presets.Entrypoint.Main`) | ShaderModuleExtensions.cs[^shaderext] | The implementation must translate SPIR-V to WGSL at `vkCreateShaderModule` (e.g. Naga/Tint compiled to wasm) or accept pre-translated WGSL in a side channel (see `/shaders/`) |
| Reflection in `VertexInput.For<T>()` | VertexInput.cs[^vertexinput] | Works on Mono-wasm. Under trimming, vertex structs' fields must be preserved; they usually are, because the code uses them |

# Open questions

* UNVERIFIED: whether any shipped consumer relies on `HOST_CACHED` readback through `MappedMemory.Invalidate`. Nothing in Abstractions calls it implicitly.
* Design decision (for `/webgpu/`): which memory types and heaps the browser implementation advertises, and whether to expose a single unified type.

[^deviceex]: DeviceEx.cs
[^bufferex]: BufferEx.cs
[^imageex]: ImageEx.cs
[^imageviewex]: ImageViewEx.cs
[^mappable]: IMappable.cs
[^mapped]: MappedMemory.cs
[^staging]: StagingPool.cs
[^frameres]: FrameResources.cs
[^syncobj]: SyncObjects.cs
[^descpool]: DescriptorPoolEx.cs
[^desclayout]: DescriptorSetLayoutEx.cs
[^vertexinput]: VertexInput.cs
[^presets]: Presets.cs
[^devext]: Extensions/DeviceExtensions.cs
[^cmdext]: Extensions/CommandBufferExtensions.cs
[^queueext]: Extensions/QueueExtensions.cs
[^physext]: Extensions/PhysicalDeviceExtensions.cs
[^shaderext]: Extensions/ShaderModuleExtensions.cs
[^debugname]: DebugNameExtensions.cs
[^helpers]: VulkanHelpers.cs
[^devinfo]: VulkanDeviceInfo.cs
[^bindbuf]: IBindBuffer.cs
[^shaderstages]: Shaders/ShaderStages.cs
[^manual-classes]: Brutal.Vulkan/Manual/Classes
[^usage-scan]: Automated call-site scan (usage.txt)
[^imgui-vk]: Brutal.ImGui.Backends VulkanBackend.cs
[^asminfo]: Metadata dump
