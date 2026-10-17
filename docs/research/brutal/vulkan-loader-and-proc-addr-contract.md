---
type: Code Analysis
title: Brutal.Vulkan loader and proc-address contract for a replacement Vulkan implementation
description: Exactly which global entry points, which vkGetInstanceProcAddr/vkGetDeviceProcAddr names (in call order) and which non-null results Brutal.Vulkan 2026.10.2 needs so that Instance, PhysicalDevice, Device and Queue creation succeed against a browser (C#) Vulkan implementation.
tags: [brutal, vulkan, loader, icd, vkGetInstanceProcAddr, contract, browser]
sources:
  - id: import-resolver
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/ImportResolver.cs
    title: Brutal.Vulkan/ImportResolver.cs
  - id: instance-cs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Manual/Classes/Instance.cs
    title: Manual/Classes/Instance.cs
  - id: physdev-cs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Manual/Classes/PhysicalDevice.cs
    title: Manual/Classes/PhysicalDevice.cs
  - id: device-cs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Manual/Classes/Device.cs
    title: Manual/Classes/Device.cs
  - id: queue-cs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Manual/Classes/Queue.cs
    title: Manual/Classes/Queue.cs
  - id: vkinstance-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Handles/VkInstance.gen.cs
    title: VkInstance.FunctionTable constructor (line 152)
  - id: vkdevice-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Handles/VkDevice.gen.cs
    title: VkDevice.FunctionTable constructor (line 738)
  - id: instext-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Extensions/VkInstanceExtensions.gen.cs
    title: Generated instance extensions (GetProcAddr at line 440)
  - id: devext-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Extensions/VkDeviceExtensions.gen.cs
    title: Generated device extensions (vx* batch destroy at 26616-26686)
  - id: pinvoke-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/PInvoke/VulkanApi/VulkanApi.gen.cs
    title: Global LibraryImport externs
  - id: helpers-cs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/VulkanHelpers.cs
    title: Brutal.Vulkan.Abstractions/VulkanHelpers.cs (Validation.CheckSupport, extension helpers)
  - id: proc-lists
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/scripts/procnames.py
    title: Script that extracted the ordered getProc name lists from the two FunctionTable constructors
  - id: vulkanex
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/nupkg-extract/ahwoo.brutal.vulkan/runtimes/osx-arm64/native/libVulkanEx.dylib
    title: libVulkanEx.dylib from Ahwoo.BRUTAL.Vulkan 2026.10.2
  - id: ksa-procs
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/Brutal.VulkanApi/VkDevice.cs
    title: KSA decompiled VkDevice.FunctionTable (634 names; reference only)
  - id: fakeicd
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/build/fakeicd/Program.cs
    title: Fake C# ICD test program (desktop CoreCLR) exercising a patched Brutal.Vulkan loader
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:16:59Z }
status: draft
---

# Contract in one table

A replacement Vulkan implementation (an "ICD", here meaning a C# Vulkan-on-WebGPU library) has to provide the following for BRUTAL's `Instance → PhysicalDevice → Device → Queue` path to work. "Hard" means BRUTAL invokes the pointer during construction or disposal; a null result there crashes. "Soft" means a null result is stored silently and only matters if the application later calls that command.

| # | Entry point | Obtained by | Invoked by BRUTAL during | Required |
|---|---|---|---|---|
| G1 | `vkGetInstanceProcAddr` | `NativeLibrary.GetExport(Dll, "vkGetInstanceProcAddr")` in `static Vulkan()`, `ImportResolver.cs:42–47`; throws if null[^import-resolver] | everything below | **hard**; it is the root |
| G2 | `vkCreateInstance` | `[LibraryImport("vulkan")]`[^pinvoke-gen] | `Instance` ctor via `Vulkan.CreateInstanceInternal` (`Instance.cs:88`)[^instance-cs] | **hard** |
| G3 | `vkEnumerateInstanceLayerProperties` | `[LibraryImport("vulkan")]` | only if the app reads `VulkanHelpers.Validation.IsSupported` (static ctor, `VulkanHelpers.cs:65–66`)[^helpers-cs] | soft (app-dependent) |
| G4 | `vkEnumerateInstanceExtensionProperties` | `[LibraryImport("vulkan")]` | only app code | soft |
| G5 | `vkEnumerateInstanceVersion` | `[LibraryImport("vulkan")]` | only app code | soft |
| I1–I105 | the 105 instance-level names below | `vkGetInstanceProcAddr(instance, name)`, once each, in the fixed order below, inside `new VkInstance.FunctionTable(Handle, Vulkan.GetInstanceProcAddr)` (`Instance.cs:90`, `VkInstance.gen.cs:152`)[^vkinstance-gen] | — | soft at load time |
| I2 | `vkEnumeratePhysicalDevices` | instance table | `Instance` ctor, `Instance.cs:94` (count) and `:97` (write)[^instance-cs] | **hard** |
| I1 | `vkDestroyInstance` | instance table | `Instance.Dispose()` and the `~Instance` finalizer (`Instance.cs:150–160`) | **hard** at teardown |
| I9 | `vkCreateDevice` | instance table | `Device` ctor via `CreateDeviceInternal` (`Device.cs:79`)[^device-cs] | **hard** |
| I106 | `vkGetDeviceProcAddr` | **`vkGetInstanceProcAddr(instance, "vkGetDeviceProcAddr")`**, via `physicalDevice.Instance.GetProcAddr("vkGetDeviceProcAddr"u8)` (`Device.cs:84`; `VkInstanceExtensions.gen.cs:440–449`)[^device-cs][^instext-gen] | `Device` ctor, which immediately calls it 697 times | **hard** |
| D1–D697 | the 697 device-level names below | `vkGetDeviceProcAddr(device, name)` in fixed order inside `new VkDevice.FunctionTable(Handle, getDeviceProcAddr)` (`Device.cs:85`, `VkDevice.gen.cs:738`)[^vkdevice-gen] | — | soft at load time |
| D2 | `vkGetDeviceQueue` | device table | `Device.GetQueue(family, index)` → `new Queue` → `GetQueueInternal` (`Queue.cs:22`)[^queue-cs] | **hard** if the app gets a queue (every app does) |
| D1 | `vkDestroyDevice` | device table | `Device.Dispose()` (`Device.cs:122`) | **hard** at teardown |
| — | `vkEnumerateDeviceExtensionProperties` (I10) | instance table | lazily, on the first `PhysicalDevice.SupportsExtension(...)` or `ExtensionNames` access (`PhysicalDevice.cs:50–51`). It is called with **`pLayerName = NULL`**, and a non-`Success` result throws `InvalidOperationException`[^physdev-cs] | hard if the app checks extensions (Abstractions' `AddMandatoryIfSupportedDeviceExtensions` does) |
| — | `vkGetPhysicalDeviceQueueFamilyProperties` (I7), `vkGetPhysicalDeviceMemoryProperties` (I8) | instance table | `PhysicalDevice.GetFirstQueueFamilyWithFlag` (`:82–83`) and `FindMemoryType` (`:101`, `:136`), the latter used by every `DeviceEx.AllocateBuffer`/`AllocateImage` | hard in practice |

Properties of the contract (all read from the code):

* **BRUTAL never calls `vkGetInstanceProcAddr(NULL, …)`.** Pre-instance functions are bound as direct P/Invokes (G2–G5) instead.[^pinvoke-gen] The implementation does not need the loader's `NULL`-instance lookup table.
* **BRUTAL never asks `vkGetInstanceProcAddr` for device-level commands**, apart from `"vkGetDeviceProcAddr"`. Device commands come only from `vkGetDeviceProcAddr`.[^device-cs]
* **No null checks at load time.** Each result is cast with `(VkXxx)pfn` and stored. Returning `NULL` for an unsupported name is safe. Calling such a command later invokes a null function pointer: a crash on CoreCLR and a wasm trap on Mono, not a managed exception. A friendlier implementation can return a pointer to a stub that throws `NotSupportedException`, but it needs one stub per signature (about 51 lowered signatures; see [/brutal/vulkan-binding-architecture.md](/brutal/vulkan-binding-architecture.md)).
* **The tables load every core and extension name unconditionally**, even for extensions the app did not enable. Core and KHR/EXT aliases are separate fields: `vkCmdBeginRendering` is D169 and `vkCmdBeginRenderingKHR` is D225. The generated wrappers call the **core** names (`cmd.BeginRendering` → `vkCmdBeginRendering`; `queue.Submit2` → `vkQueueSubmit2`). An implementation that only fills the KHR alias would break the core-named wrappers. Fill both, or at least the core name.
* **`vx*` names.** D694–D697 (`vxDestroyImages`, `vxDestroyImageViews`, `vxDestroyBuffers`, `vxDestroyBufferViews`) are requested through `vkGetDeviceProcAddr`. A Khronos loader returns NULL for them; the functions are exported by the package's `libVulkanEx` instead.[^vulkanex] Only the generated batch helpers `device.DestroyImages/DestroyImageViews/DestroyBuffers/DestroyBufferViews` (`VkDeviceExtensions.gen.cs:26616–26686`) call them, and nothing in Brutal.Vulkan or Brutal.Vulkan.Abstractions does.[^devext-gen] A browser implementation can implement them, which is trivial loops, or return NULL.
* **Instance creation inputs** (`Instance.cs:24–88`): `VkInstanceCreateInfo` carries `ApplicationInfo = &createInfo.AppInfo`, `Flags`, `Next` (the app may chain a `VkDebugUtilsMessengerCreateInfoEXT`), and UTF-8 arrays from the two `HashSet<string>` fields. **Both `EnabledExtensions` and `EnabledLayers` must be non-null**: the ctor reads `.Count`. Expect apps to request `VK_KHR_surface` plus a platform surface extension. On desktop that comes from `Glfw.GetRequiredInstanceExtensions`; on macOS `VulkanHelpers.AddMetalRequiredExtensions` adds `VK_KHR_portability_enumeration` and `VK_KHR_get_physical_device_properties2` and sets `EnumeratePortabilityBitKHR`. Debug builds may add `VK_EXT_debug_utils`, `VK_EXT_debug_report` and the `VK_LAYER_KHRONOS_validation` layer.[^helpers-cs] The browser implementation should accept or ignore unknown extensions and layers rather than return `VK_ERROR_EXTENSION_NOT_PRESENT`/`VK_ERROR_LAYER_NOT_PRESENT`, or the app-side extension lists must be adjusted for the browser.
* **Device creation inputs** (`Device.cs:32–85`): `VkDeviceCreateInfo` with `ppEnabledExtensionNames` (UTF-8 from `HashSet<string>`), `pQueueCreateInfos`, `pEnabledFeatures` (`Ptr<VkPhysicalDeviceFeatures>`, may be null) and `pNext`. Apps typically chain `VkPhysicalDeviceVulkan1{1,2,3}Features` or `VkPhysicalDeviceFeatures2` here. The implementation must walk `pNext` by `sType` and tolerate unknown structures.

# Empirical check

A scratch fork of Brutal.Vulkan with a pluggable `vkGetInstanceProcAddr` was run on desktop CoreCLR (see [/brutal/building-from-source.md](/brutal/building-from-source.md)). Its fake C# implementation served only `vkCreateInstance`, `vkEnumeratePhysicalDevices` and `vkDestroyInstance` and returned NULL for the other 103 names. `Vulkan.CreateInstance` succeeded after exactly **106 lookups**, `Instance.PhysicalDevices` was empty, and `Dispose` called `vkDestroyInstance`. This confirms the "soft at load time" rows above.[^fakeicd]

# Ordered call sequence for a typical BRUTAL app (`Vulkan` + `Vulkan.Abstractions`)

1. *(optional)* `VulkanHelpers.Validation.IsSupported`: G3 twice (count, then write).[^helpers-cs]
2. First touch of any `Vulkan.*` member runs `static Vulkan()` → G1 (`GetExport`).[^import-resolver]
3. `Vulkan.CreateInstance(new Instance.CreateInfo{…})` → G2 → I1…I105 in table order → I2 twice (count, then write) → one `PhysicalDevice` object per handle, with no calls.[^instance-cs]
4. Surface creation. Upstream this is `Glfw.CreateWindowSurface(instance.VkHandle, window, out ulong surface)`, which runs **inside native GLFW** and uses GLFW's own loader lookup of `vkCreate{Win32,Xlib,Xcb,Wayland,Metal}SurfaceKHR`/`EXT`. In the browser the GLFW replacement must call the C# implementation directly. Brutal's instance table already has fields for `vkCreateHeadlessSurfaceEXT` (I78) and `vkCreateMetalSurfaceEXT` (I104), so a browser surface could be modelled as one of those or exposed as a non-standard C# API. Design choice; see `/webgpu/`.
5. Physical-device queries, depending on the app: `GetProperties` (I6), `GetFeatures`/`GetFeatures2` (I3/I14), `GetQueueFamilyProperties_Count/_Write` (I7), `GetSurfaceSupportKHR` (I26), `GetSurfaceCapabilitiesKHR` (I27), `GetSurfaceFormatsKHR_Count/_Write` (I28), `GetSurfacePresentModesKHR_Count/_Write` (I29), `GetMemoryProperties` (I8), `GetFormatProperties` (I4, via `FindSupportedFormat`), and `SupportsExtension` → I10 with a NULL layer.
6. `physicalDevice.CreateDevice(new DeviceEx.CreateInfo{…})` (Abstractions) or `new Device.CreateInfo` (raw) → I9 → I106 (`vkGetInstanceProcAddr(instance, "vkGetDeviceProcAddr")`) → D1…D697 in table order.[^device-cs]
7. `device.GetQueue(family, 0)` → D2.[^queue-cs]
8. Teardown: D1 in `Device.Dispose()`; I1 in `Instance.Dispose()` or the finalizer.

# Minimal non-null set for "instance + device + queue + one triangle"

Beyond the hard entries above (G1, G2, I1, I2, I9, I106, D1, D2), a frame of the recipe in [/brutal/desktop-hello-mesh-recipe.md](/brutal/desktop-hello-mesh-recipe.md) also needs the following. The full Abstractions-driven list is in [/brutal/vulkan-abstractions-catalogue-and-icd-surface.md](/brutal/vulkan-abstractions-catalogue-and-icd-surface.md).

* Instance: I6, I7, I8, I10, I26, I27, I28, I29; `vkDestroySurfaceKHR` (I25).
* Device: `vkCreateSwapchainKHR`, `vkGetSwapchainImagesKHR`, `vkAcquireNextImageKHR`, `vkQueuePresentKHR`, `vkDestroySwapchainKHR`; `vkCreateImageView`/`vkDestroyImageView`; `vkCreateShaderModule`/`vkDestroyShaderModule`; `vkCreatePipelineLayout`; `vkCreateGraphicsPipelines`; `vkCreateDescriptorSetLayout`, `vkCreateDescriptorPool`, `vkAllocateDescriptorSets`, `vkUpdateDescriptorSets`; `vkCreateBuffer`, `vkGetBufferMemoryRequirements`, `vkAllocateMemory`, `vkBindBufferMemory`, `vkMapMemory`, `vkUnmapMemory`, `vkFreeMemory`, `vkDestroyBuffer`; `vkCreateCommandPool`, `vkAllocateCommandBuffers`, `vkResetCommandBuffer`, `vkBeginCommandBuffer`, `vkEndCommandBuffer`; `vkCreateSemaphore`, `vkCreateFence`, `vkWaitForFences`, `vkResetFences`; `vkQueueSubmit` (or `vkQueueSubmit2`); `vkCmdPipelineBarrier` (or `…2`), `vkCmdBeginRendering`/`vkCmdEndRendering` (or render pass + framebuffer + `vkCmdBeginRenderPass`/`vkCmdEndRenderPass`), `vkCmdBindPipeline`, `vkCmdSetViewport`, `vkCmdSetScissor`, `vkCmdBindVertexBuffers`, `vkCmdBindIndexBuffer`, `vkCmdBindDescriptorSets`, `vkCmdPushConstants`, `vkCmdDrawIndexed`; `vkDeviceWaitIdle`.

# Instance-level names, in the order `VkInstance.FunctionTable` requests them

Extracted from `VkInstance.gen.cs:152ff`.[^vkinstance-gen][^proc-lists] The field `vkGetInstanceProcAddr` is assigned from the incoming `getProc` and is **not** looked up. The same table is reached through `PhysicalDevice.PInvoke`.

```text
  1: vkDestroyInstance vkEnumeratePhysicalDevices vkGetPhysicalDeviceFeatures vkGetPhysicalDeviceFormatProperties vkGetPhysicalDeviceImageFormatProperties vkGetPhysicalDeviceProperties
  7: vkGetPhysicalDeviceQueueFamilyProperties vkGetPhysicalDeviceMemoryProperties vkCreateDevice vkEnumerateDeviceExtensionProperties vkEnumerateDeviceLayerProperties vkGetPhysicalDeviceSparseImageFormatProperties
 13: vkEnumeratePhysicalDeviceGroups vkGetPhysicalDeviceFeatures2 vkGetPhysicalDeviceProperties2 vkGetPhysicalDeviceFormatProperties2 vkGetPhysicalDeviceImageFormatProperties2 vkGetPhysicalDeviceQueueFamilyProperties2
 19: vkGetPhysicalDeviceMemoryProperties2 vkGetPhysicalDeviceSparseImageFormatProperties2 vkGetPhysicalDeviceExternalBufferProperties vkGetPhysicalDeviceExternalFenceProperties vkGetPhysicalDeviceExternalSemaphoreProperties vkGetPhysicalDeviceToolProperties
 25: vkDestroySurfaceKHR vkGetPhysicalDeviceSurfaceSupportKHR vkGetPhysicalDeviceSurfaceCapabilitiesKHR vkGetPhysicalDeviceSurfaceFormatsKHR vkGetPhysicalDeviceSurfacePresentModesKHR vkGetPhysicalDevicePresentRectanglesKHR
 31: vkGetPhysicalDeviceDisplayPropertiesKHR vkGetPhysicalDeviceDisplayPlanePropertiesKHR vkGetDisplayPlaneSupportedDisplaysKHR vkGetDisplayModePropertiesKHR vkCreateDisplayModeKHR vkGetDisplayPlaneCapabilitiesKHR
 37: vkCreateDisplayPlaneSurfaceKHR vkGetPhysicalDeviceVideoCapabilitiesKHR vkGetPhysicalDeviceVideoFormatPropertiesKHR vkGetPhysicalDeviceFeatures2KHR vkGetPhysicalDeviceProperties2KHR vkGetPhysicalDeviceFormatProperties2KHR
 43: vkGetPhysicalDeviceImageFormatProperties2KHR vkGetPhysicalDeviceQueueFamilyProperties2KHR vkGetPhysicalDeviceMemoryProperties2KHR vkGetPhysicalDeviceSparseImageFormatProperties2KHR vkEnumeratePhysicalDeviceGroupsKHR vkGetPhysicalDeviceExternalBufferPropertiesKHR
 49: vkGetPhysicalDeviceExternalSemaphorePropertiesKHR vkGetPhysicalDeviceExternalFencePropertiesKHR vkEnumeratePhysicalDeviceQueueFamilyPerformanceQueryCountersKHR vkGetPhysicalDeviceQueueFamilyPerformanceQueryPassesKHR vkGetPhysicalDeviceSurfaceCapabilities2KHR vkGetPhysicalDeviceSurfaceFormats2KHR
 55: vkGetPhysicalDeviceDisplayProperties2KHR vkGetPhysicalDeviceDisplayPlaneProperties2KHR vkGetDisplayModeProperties2KHR vkGetDisplayPlaneCapabilities2KHR vkGetPhysicalDeviceFragmentShadingRatesKHR vkGetPhysicalDeviceVideoEncodeQualityLevelPropertiesKHR
 61: vkGetPhysicalDeviceCooperativeMatrixPropertiesKHR vkGetPhysicalDeviceCalibrateableTimeDomainsKHR vkCreateDebugReportCallbackEXT vkDestroyDebugReportCallbackEXT vkDebugReportMessageEXT vkGetPhysicalDeviceExternalImageFormatPropertiesNV
 67: vkReleaseDisplayEXT vkGetPhysicalDeviceSurfaceCapabilities2EXT vkCreateDebugUtilsMessengerEXT vkDestroyDebugUtilsMessengerEXT vkSubmitDebugUtilsMessageEXT vkGetPhysicalDeviceDescriptorSizeEXT
 73: vkGetPhysicalDeviceMultisamplePropertiesEXT vkGetPhysicalDeviceCalibrateableTimeDomainsEXT vkGetPhysicalDeviceToolPropertiesEXT vkGetPhysicalDeviceCooperativeMatrixPropertiesNV vkGetPhysicalDeviceSupportedFramebufferMixedSamplesCombinationsNV vkCreateHeadlessSurfaceEXT
 79: vkAcquireDrmDisplayEXT vkGetDrmDisplayEXT vkGetPhysicalDeviceExternalTensorPropertiesARM vkGetPhysicalDeviceOpticalFlowImageFormatsNV vkGetPhysicalDeviceCooperativeVectorPropertiesNV vkGetPhysicalDeviceQueueFamilyDataGraphPropertiesARM
 85: vkGetPhysicalDeviceQueueFamilyDataGraphProcessingEnginePropertiesARM vkGetPhysicalDeviceQueueFamilyDataGraphEngineOperationPropertiesARM vkGetPhysicalDeviceCooperativeMatrixFlexibleDimensionsPropertiesNV vkEnumeratePhysicalDeviceQueueFamilyPerformanceCountersByRegionARM vkEnumeratePhysicalDeviceShaderInstrumentationMetricsARM vkGetPhysicalDeviceQueueFamilyDataGraphOpticalFlowImageFormatsARM
 91: vkCreateWin32SurfaceKHR vkGetPhysicalDeviceWin32PresentationSupportKHR vkGetPhysicalDeviceSurfacePresentModes2EXT vkAcquireWinrtDisplayNV vkGetWinrtDisplayNV vkCreateXlibSurfaceKHR
 97: vkGetPhysicalDeviceXlibPresentationSupportKHR vkAcquireXlibDisplayEXT vkGetRandROutputDisplayEXT vkCreateXcbSurfaceKHR vkGetPhysicalDeviceXcbPresentationSupportKHR vkCreateWaylandSurfaceKHR
103: vkGetPhysicalDeviceWaylandPresentationSupportKHR vkCreateMetalSurfaceEXT vkCreateAndroidSurfaceKHR```

Then `Device` ctor: `106: vkGetDeviceProcAddr` (via `vkGetInstanceProcAddr`).

# Device-level names, in the order `VkDevice.FunctionTable` requests them

Extracted from `VkDevice.gen.cs:738ff`.[^vkdevice-gen][^proc-lists] The field `vkGetDeviceProcAddr` is assigned from the incoming `getProc` and is not looked up.

```text
  1: vkDestroyDevice vkGetDeviceQueue vkQueueSubmit vkQueueWaitIdle vkDeviceWaitIdle vkAllocateMemory
  7: vkFreeMemory vkMapMemory vkUnmapMemory vkFlushMappedMemoryRanges vkInvalidateMappedMemoryRanges vkGetDeviceMemoryCommitment
 13: vkBindBufferMemory vkBindImageMemory vkGetBufferMemoryRequirements vkGetImageMemoryRequirements vkGetImageSparseMemoryRequirements vkQueueBindSparse
 19: vkCreateFence vkDestroyFence vkResetFences vkGetFenceStatus vkWaitForFences vkCreateSemaphore
 25: vkDestroySemaphore vkCreateQueryPool vkDestroyQueryPool vkGetQueryPoolResults vkCreateBuffer vkDestroyBuffer
 31: vkCreateImage vkDestroyImage vkGetImageSubresourceLayout vkCreateImageView vkDestroyImageView vkCreateCommandPool
 37: vkDestroyCommandPool vkResetCommandPool vkAllocateCommandBuffers vkFreeCommandBuffers vkBeginCommandBuffer vkEndCommandBuffer
 43: vkResetCommandBuffer vkCmdCopyBuffer vkCmdCopyImage vkCmdCopyBufferToImage vkCmdCopyImageToBuffer vkCmdUpdateBuffer
 49: vkCmdFillBuffer vkCmdPipelineBarrier vkCmdBeginQuery vkCmdEndQuery vkCmdResetQueryPool vkCmdWriteTimestamp
 55: vkCmdCopyQueryPoolResults vkCmdExecuteCommands vkCreateEvent vkDestroyEvent vkGetEventStatus vkSetEvent
 61: vkResetEvent vkCreateBufferView vkDestroyBufferView vkCreateShaderModule vkDestroyShaderModule vkCreatePipelineCache
 67: vkDestroyPipelineCache vkGetPipelineCacheData vkMergePipelineCaches vkCreateComputePipelines vkDestroyPipeline vkCreatePipelineLayout
 73: vkDestroyPipelineLayout vkCreateSampler vkDestroySampler vkCreateDescriptorSetLayout vkDestroyDescriptorSetLayout vkCreateDescriptorPool
 79: vkDestroyDescriptorPool vkResetDescriptorPool vkAllocateDescriptorSets vkFreeDescriptorSets vkUpdateDescriptorSets vkCmdBindPipeline
 85: vkCmdBindDescriptorSets vkCmdClearColorImage vkCmdDispatch vkCmdDispatchIndirect vkCmdSetEvent vkCmdResetEvent
 91: vkCmdWaitEvents vkCmdPushConstants vkCreateGraphicsPipelines vkCreateFramebuffer vkDestroyFramebuffer vkCreateRenderPass
 97: vkDestroyRenderPass vkGetRenderAreaGranularity vkCmdSetViewport vkCmdSetScissor vkCmdSetLineWidth vkCmdSetDepthBias
103: vkCmdSetBlendConstants vkCmdSetDepthBounds vkCmdSetStencilCompareMask vkCmdSetStencilWriteMask vkCmdSetStencilReference vkCmdBindIndexBuffer
109: vkCmdBindVertexBuffers vkCmdDraw vkCmdDrawIndexed vkCmdDrawIndirect vkCmdDrawIndexedIndirect vkCmdBlitImage
115: vkCmdClearDepthStencilImage vkCmdClearAttachments vkCmdResolveImage vkCmdBeginRenderPass vkCmdNextSubpass vkCmdEndRenderPass
121: vkBindBufferMemory2 vkBindImageMemory2 vkGetDeviceGroupPeerMemoryFeatures vkCmdSetDeviceMask vkGetImageMemoryRequirements2 vkGetBufferMemoryRequirements2
127: vkGetImageSparseMemoryRequirements2 vkTrimCommandPool vkGetDeviceQueue2 vkCmdDispatchBase vkCreateDescriptorUpdateTemplate vkDestroyDescriptorUpdateTemplate
133: vkUpdateDescriptorSetWithTemplate vkGetDescriptorSetLayoutSupport vkCreateSamplerYcbcrConversion vkDestroySamplerYcbcrConversion vkResetQueryPool vkGetSemaphoreCounterValue
139: vkWaitSemaphores vkSignalSemaphore vkGetBufferDeviceAddress vkGetBufferOpaqueCaptureAddress vkGetDeviceMemoryOpaqueCaptureAddress vkCmdDrawIndirectCount
145: vkCmdDrawIndexedIndirectCount vkCreateRenderPass2 vkCmdBeginRenderPass2 vkCmdNextSubpass2 vkCmdEndRenderPass2 vkCreatePrivateDataSlot
151: vkDestroyPrivateDataSlot vkSetPrivateData vkGetPrivateData vkCmdPipelineBarrier2 vkCmdWriteTimestamp2 vkQueueSubmit2
157: vkCmdCopyBuffer2 vkCmdCopyImage2 vkCmdCopyBufferToImage2 vkCmdCopyImageToBuffer2 vkGetDeviceBufferMemoryRequirements vkGetDeviceImageMemoryRequirements
163: vkGetDeviceImageSparseMemoryRequirements vkCmdSetEvent2 vkCmdResetEvent2 vkCmdWaitEvents2 vkCmdBlitImage2 vkCmdResolveImage2
169: vkCmdBeginRendering vkCmdEndRendering vkCmdSetCullMode vkCmdSetFrontFace vkCmdSetPrimitiveTopology vkCmdSetViewportWithCount
175: vkCmdSetScissorWithCount vkCmdBindVertexBuffers2 vkCmdSetDepthTestEnable vkCmdSetDepthWriteEnable vkCmdSetDepthCompareOp vkCmdSetDepthBoundsTestEnable
181: vkCmdSetStencilTestEnable vkCmdSetStencilOp vkCmdSetRasterizerDiscardEnable vkCmdSetDepthBiasEnable vkCmdSetPrimitiveRestartEnable vkMapMemory2
187: vkUnmapMemory2 vkGetDeviceImageSubresourceLayout vkGetImageSubresourceLayout2 vkCopyMemoryToImage vkCopyImageToMemory vkCopyImageToImage
193: vkTransitionImageLayout vkCmdPushDescriptorSet vkCmdPushDescriptorSetWithTemplate vkCmdBindDescriptorSets2 vkCmdPushConstants2 vkCmdPushDescriptorSet2
199: vkCmdPushDescriptorSetWithTemplate2 vkCmdSetLineStipple vkCmdBindIndexBuffer2 vkGetRenderingAreaGranularity vkCmdSetRenderingAttachmentLocations vkCmdSetRenderingInputAttachmentIndices
205: vkCreateSwapchainKHR vkDestroySwapchainKHR vkGetSwapchainImagesKHR vkAcquireNextImageKHR vkQueuePresentKHR vkGetDeviceGroupPresentCapabilitiesKHR
211: vkGetDeviceGroupSurfacePresentModesKHR vkAcquireNextImage2KHR vkCreateSharedSwapchainsKHR vkCreateVideoSessionKHR vkDestroyVideoSessionKHR vkGetVideoSessionMemoryRequirementsKHR
217: vkBindVideoSessionMemoryKHR vkCreateVideoSessionParametersKHR vkUpdateVideoSessionParametersKHR vkDestroyVideoSessionParametersKHR vkCmdBeginVideoCodingKHR vkCmdEndVideoCodingKHR
223: vkCmdControlVideoCodingKHR vkCmdDecodeVideoKHR vkCmdBeginRenderingKHR vkCmdEndRenderingKHR vkGetDeviceGroupPeerMemoryFeaturesKHR vkCmdSetDeviceMaskKHR
229: vkCmdDispatchBaseKHR vkTrimCommandPoolKHR vkGetMemoryFdKHR vkGetMemoryFdPropertiesKHR vkImportSemaphoreFdKHR vkGetSemaphoreFdKHR
235: vkCmdPushDescriptorSetKHR vkCmdPushDescriptorSetWithTemplateKHR vkCreateDescriptorUpdateTemplateKHR vkDestroyDescriptorUpdateTemplateKHR vkUpdateDescriptorSetWithTemplateKHR vkCreateRenderPass2KHR
241: vkCmdBeginRenderPass2KHR vkCmdNextSubpass2KHR vkCmdEndRenderPass2KHR vkGetSwapchainStatusKHR vkImportFenceFdKHR vkGetFenceFdKHR
247: vkAcquireProfilingLockKHR vkReleaseProfilingLockKHR vkGetImageMemoryRequirements2KHR vkGetBufferMemoryRequirements2KHR vkGetImageSparseMemoryRequirements2KHR vkCreateSamplerYcbcrConversionKHR
253: vkDestroySamplerYcbcrConversionKHR vkBindBufferMemory2KHR vkBindImageMemory2KHR vkGetDescriptorSetLayoutSupportKHR vkCmdDrawIndirectCountKHR vkCmdDrawIndexedIndirectCountKHR
259: vkGetSemaphoreCounterValueKHR vkWaitSemaphoresKHR vkSignalSemaphoreKHR vkCmdSetFragmentShadingRateKHR vkCmdSetRenderingAttachmentLocationsKHR vkCmdSetRenderingInputAttachmentIndicesKHR
265: vkWaitForPresentKHR vkGetBufferDeviceAddressKHR vkGetBufferOpaqueCaptureAddressKHR vkGetDeviceMemoryOpaqueCaptureAddressKHR vkCreateDeferredOperationKHR vkDestroyDeferredOperationKHR
271: vkGetDeferredOperationMaxConcurrencyKHR vkGetDeferredOperationResultKHR vkDeferredOperationJoinKHR vkGetPipelineExecutablePropertiesKHR vkGetPipelineExecutableStatisticsKHR vkGetPipelineExecutableInternalRepresentationsKHR
277: vkMapMemory2KHR vkUnmapMemory2KHR vkGetEncodedVideoSessionParametersKHR vkCmdEncodeVideoKHR vkCmdSetEvent2KHR vkCmdResetEvent2KHR
283: vkCmdWaitEvents2KHR vkCmdPipelineBarrier2KHR vkCmdWriteTimestamp2KHR vkQueueSubmit2KHR vkCmdBindIndexBuffer3KHR vkCmdBindVertexBuffers3KHR
289: vkCmdDrawIndirect2KHR vkCmdDrawIndexedIndirect2KHR vkCmdDispatchIndirect2KHR vkCmdCopyMemoryKHR vkCmdCopyMemoryToImageKHR vkCmdCopyImageToMemoryKHR
295: vkCmdUpdateMemoryKHR vkCmdFillMemoryKHR vkCmdCopyQueryPoolResultsToMemoryKHR vkCmdDrawIndirectCount2KHR vkCmdDrawIndexedIndirectCount2KHR vkCmdBeginConditionalRendering2EXT
301: vkCmdBindTransformFeedbackBuffers2EXT vkCmdBeginTransformFeedback2EXT vkCmdEndTransformFeedback2EXT vkCmdDrawIndirectByteCount2EXT vkCmdDrawMeshTasksIndirect2EXT vkCmdDrawMeshTasksIndirectCount2EXT
307: vkCmdWriteMarkerToMemoryAMD vkCreateAccelerationStructure2KHR vkCmdCopyBuffer2KHR vkCmdCopyImage2KHR vkCmdCopyBufferToImage2KHR vkCmdCopyImageToBuffer2KHR
313: vkCmdBlitImage2KHR vkCmdResolveImage2KHR vkCmdTraceRaysIndirect2KHR vkGetDeviceBufferMemoryRequirementsKHR vkGetDeviceImageMemoryRequirementsKHR vkGetDeviceImageSparseMemoryRequirementsKHR
319: vkCmdBindIndexBuffer2KHR vkGetRenderingAreaGranularityKHR vkGetDeviceImageSubresourceLayoutKHR vkGetImageSubresourceLayout2KHR vkWaitForPresent2KHR vkCreatePipelineBinariesKHR
325: vkDestroyPipelineBinaryKHR vkGetPipelineKeyKHR vkGetPipelineBinaryDataKHR vkReleaseCapturedPipelineDataKHR vkReleaseSwapchainImagesKHR vkCmdSetLineStippleKHR
331: vkGetCalibratedTimestampsKHR vkCmdBindDescriptorSets2KHR vkCmdPushConstants2KHR vkCmdPushDescriptorSet2KHR vkCmdPushDescriptorSetWithTemplate2KHR vkCmdSetDescriptorBufferOffsets2EXT
337: vkCmdBindDescriptorBufferEmbeddedSamplers2EXT vkCmdCopyMemoryIndirectKHR vkCmdCopyMemoryToImageIndirectKHR vkGetDeviceFaultReportsKHR vkGetDeviceFaultDebugInfoKHR vkCmdEndRendering2KHR
343: vkDebugMarkerSetObjectTagEXT vkDebugMarkerSetObjectNameEXT vkCmdDebugMarkerBeginEXT vkCmdDebugMarkerEndEXT vkCmdDebugMarkerInsertEXT vkCmdBindTransformFeedbackBuffersEXT
349: vkCmdBeginTransformFeedbackEXT vkCmdEndTransformFeedbackEXT vkCmdBeginQueryIndexedEXT vkCmdEndQueryIndexedEXT vkCmdDrawIndirectByteCountEXT vkCreateCuModuleNVX
355: vkCreateCuFunctionNVX vkDestroyCuModuleNVX vkDestroyCuFunctionNVX vkCmdCuLaunchKernelNVX vkGetImageViewHandleNVX vkGetImageViewHandle64NVX
361: vkGetImageViewAddressNVX vkGetDeviceCombinedImageSamplerIndexNVX vkCmdDrawIndirectCountAMD vkCmdDrawIndexedIndirectCountAMD vkGetShaderInfoAMD vkCmdBeginConditionalRenderingEXT
367: vkCmdEndConditionalRenderingEXT vkCmdSetViewportWScalingNV vkDisplayPowerControlEXT vkRegisterDeviceEventEXT vkRegisterDisplayEventEXT vkGetSwapchainCounterEXT
373: vkGetRefreshCycleDurationGOOGLE vkGetPastPresentationTimingGOOGLE vkCmdSetDiscardRectangleEXT vkCmdSetDiscardRectangleEnableEXT vkCmdSetDiscardRectangleModeEXT vkSetHdrMetadataEXT
379: vkSetDebugUtilsObjectNameEXT vkSetDebugUtilsObjectTagEXT vkQueueBeginDebugUtilsLabelEXT vkQueueEndDebugUtilsLabelEXT vkQueueInsertDebugUtilsLabelEXT vkCmdBeginDebugUtilsLabelEXT
385: vkCmdEndDebugUtilsLabelEXT vkCmdInsertDebugUtilsLabelEXT vkCreateGpaSessionAMD vkDestroyGpaSessionAMD vkSetGpaDeviceClockModeAMD vkGetGpaDeviceClockInfoAMD
391: vkCmdBeginGpaSessionAMD vkCmdEndGpaSessionAMD vkCmdBeginGpaSampleAMD vkCmdEndGpaSampleAMD vkGetGpaSessionStatusAMD vkGetGpaSessionResultsAMD
397: vkResetGpaSessionAMD vkCmdCopyGpaSessionResultsAMD vkWriteSamplerDescriptorsEXT vkWriteResourceDescriptorsEXT vkCmdBindSamplerHeapEXT vkCmdBindResourceHeapEXT
403: vkCmdPushDataEXT vkGetImageOpaqueCaptureDataEXT vkRegisterCustomBorderColorEXT vkUnregisterCustomBorderColorEXT vkGetTensorOpaqueCaptureDataARM vkCmdSetSampleLocationsEXT
409: vkGetImageDrmFormatModifierPropertiesEXT vkCreateValidationCacheEXT vkDestroyValidationCacheEXT vkMergeValidationCachesEXT vkGetValidationCacheDataEXT vkCmdBindShadingRateImageNV
415: vkCmdSetViewportShadingRatePaletteNV vkCmdSetCoarseSampleOrderNV vkCreateAccelerationStructureNV vkDestroyAccelerationStructureNV vkGetAccelerationStructureMemoryRequirementsNV vkBindAccelerationStructureMemoryNV
421: vkCmdBuildAccelerationStructureNV vkCmdCopyAccelerationStructureNV vkCmdTraceRaysNV vkCreateRayTracingPipelinesNV vkGetRayTracingShaderGroupHandlesKHR vkGetRayTracingShaderGroupHandlesNV
427: vkGetAccelerationStructureHandleNV vkCmdWriteAccelerationStructuresPropertiesNV vkCompileDeferredNV vkGetMemoryHostPointerPropertiesEXT vkCmdWriteBufferMarkerAMD vkCmdWriteBufferMarker2AMD
433: vkGetCalibratedTimestampsEXT vkCmdDrawMeshTasksNV vkCmdDrawMeshTasksIndirectNV vkCmdDrawMeshTasksIndirectCountNV vkCmdSetExclusiveScissorEnableNV vkCmdSetExclusiveScissorNV
439: vkCmdSetCheckpointNV vkGetQueueCheckpointDataNV vkGetQueueCheckpointData2NV vkSetSwapchainPresentTimingQueueSizeEXT vkGetSwapchainTimingPropertiesEXT vkGetSwapchainTimeDomainPropertiesEXT
445: vkGetPastPresentationTimingEXT vkInitializePerformanceApiINTEL vkUninitializePerformanceApiINTEL vkCmdSetPerformanceMarkerINTEL vkCmdSetPerformanceStreamMarkerINTEL vkCmdSetPerformanceOverrideINTEL
451: vkAcquirePerformanceConfigurationINTEL vkReleasePerformanceConfigurationINTEL vkQueueSetPerformanceConfigurationINTEL vkGetPerformanceParameterINTEL vkSetLocalDimmingAMD vkGetBufferDeviceAddressEXT
457: vkCmdSetLineStippleEXT vkResetQueryPoolEXT vkCmdSetCullModeEXT vkCmdSetFrontFaceEXT vkCmdSetPrimitiveTopologyEXT vkCmdSetViewportWithCountEXT
463: vkCmdSetScissorWithCountEXT vkCmdBindVertexBuffers2EXT vkCmdSetDepthTestEnableEXT vkCmdSetDepthWriteEnableEXT vkCmdSetDepthCompareOpEXT vkCmdSetDepthBoundsTestEnableEXT
469: vkCmdSetStencilTestEnableEXT vkCmdSetStencilOpEXT vkCopyMemoryToImageEXT vkCopyImageToMemoryEXT vkCopyImageToImageEXT vkTransitionImageLayoutEXT
475: vkGetImageSubresourceLayout2EXT vkReleaseSwapchainImagesEXT vkGetGeneratedCommandsMemoryRequirementsNV vkCmdPreprocessGeneratedCommandsNV vkCmdExecuteGeneratedCommandsNV vkCmdBindPipelineShaderGroupNV
481: vkCreateIndirectCommandsLayoutNV vkDestroyIndirectCommandsLayoutNV vkCmdSetDepthBias2EXT vkCreatePrivateDataSlotEXT vkDestroyPrivateDataSlotEXT vkSetPrivateDataEXT
487: vkGetPrivateDataEXT vkQueueSetPerfHintQCOM vkCmdDispatchTileQCOM vkCmdBeginPerTileExecutionQCOM vkCmdEndPerTileExecutionQCOM vkSetLatencySleepModeLegacyNV
493: vkLatencySleepLegacyNV vkSetLatencyMarkerLegacyNV vkGetLatencyTimingsLegacyNV vkQueueNotifyOutOfBandLegacyNV vkGetSleepStatusLegacyNV vkShutdownLatencyDeviceLegacyNV
499: vkGetDescriptorSetLayoutSizeEXT vkGetDescriptorSetLayoutBindingOffsetEXT vkGetDescriptorEXT vkCmdBindDescriptorBuffersEXT vkCmdSetDescriptorBufferOffsetsEXT vkCmdBindDescriptorBufferEmbeddedSamplersEXT
505: vkGetBufferOpaqueCaptureDescriptorDataEXT vkGetImageOpaqueCaptureDescriptorDataEXT vkGetImageViewOpaqueCaptureDescriptorDataEXT vkGetSamplerOpaqueCaptureDescriptorDataEXT vkGetAccelerationStructureOpaqueCaptureDescriptorDataEXT vkCmdSetFragmentShadingRateEnumNV
511: vkGetDeviceFaultInfoEXT vkCmdSetVertexInputEXT vkGetDeviceSubpassShadingMaxWorkgroupSizeHUAWEI vkCmdSubpassShadingHUAWEI vkCmdBindInvocationMaskHUAWEI vkGetMemoryRemoteAddressNV
517: vkGetPipelinePropertiesEXT vkCmdSetPatchControlPointsEXT vkCmdSetRasterizerDiscardEnableEXT vkCmdSetDepthBiasEnableEXT vkCmdSetLogicOpEXT vkCmdSetPrimitiveRestartEnableEXT
523: vkCmdSetColorWriteEnableEXT vkCmdDrawMultiEXT vkCmdDrawMultiIndexedEXT vkCreateMicromapEXT vkDestroyMicromapEXT vkCmdBuildMicromapsEXT
529: vkBuildMicromapsEXT vkCopyMicromapEXT vkCopyMicromapToMemoryEXT vkCopyMemoryToMicromapEXT vkWriteMicromapsPropertiesEXT vkCmdCopyMicromapEXT
535: vkCmdCopyMicromapToMemoryEXT vkCmdCopyMemoryToMicromapEXT vkCmdWriteMicromapsPropertiesEXT vkGetDeviceMicromapCompatibilityEXT vkGetMicromapBuildSizesEXT vkCmdDrawClusterHUAWEI
541: vkCmdDrawClusterIndirectHUAWEI vkSetDeviceMemoryPriorityEXT vkCmdSetDispatchParametersARM vkGetDescriptorSetLayoutHostMappingInfoVALVE vkGetDescriptorSetHostMappingVALVE vkCmdCopyMemoryIndirectNV
547: vkCmdCopyMemoryToImageIndirectNV vkCmdDecompressMemoryNV vkCmdDecompressMemoryIndirectCountNV vkGetPipelineIndirectMemoryRequirementsNV vkCmdUpdatePipelineIndirectBufferNV vkGetPipelineIndirectDeviceAddressNV
553: vkCmdSetDepthClampEnableEXT vkCmdSetPolygonModeEXT vkCmdSetRasterizationSamplesEXT vkCmdSetSampleMaskEXT vkCmdSetAlphaToCoverageEnableEXT vkCmdSetAlphaToOneEnableEXT
559: vkCmdSetLogicOpEnableEXT vkCmdSetColorBlendEnableEXT vkCmdSetColorBlendEquationEXT vkCmdSetColorWriteMaskEXT vkCmdSetTessellationDomainOriginEXT vkCmdSetRasterizationStreamEXT
565: vkCmdSetConservativeRasterizationModeEXT vkCmdSetExtraPrimitiveOverestimationSizeEXT vkCmdSetDepthClipEnableEXT vkCmdSetSampleLocationsEnableEXT vkCmdSetColorBlendAdvancedEXT vkCmdSetProvokingVertexModeEXT
571: vkCmdSetLineRasterizationModeEXT vkCmdSetLineStippleEnableEXT vkCmdSetDepthClipNegativeOneToOneEXT vkCmdSetViewportWScalingEnableNV vkCmdSetViewportSwizzleNV vkCmdSetCoverageToColorEnableNV
577: vkCmdSetCoverageToColorLocationNV vkCmdSetCoverageModulationModeNV vkCmdSetCoverageModulationTableEnableNV vkCmdSetCoverageModulationTableNV vkCmdSetShadingRateImageEnableNV vkCmdSetRepresentativeFragmentTestEnableNV
583: vkCmdSetCoverageReductionModeNV vkCreateTensorARM vkDestroyTensorARM vkCreateTensorViewARM vkDestroyTensorViewARM vkGetTensorMemoryRequirementsARM
589: vkBindTensorMemoryARM vkGetDeviceTensorMemoryRequirementsARM vkCmdCopyTensorARM vkGetTensorOpaqueCaptureDescriptorDataARM vkGetTensorViewOpaqueCaptureDescriptorDataARM vkGetShaderModuleIdentifierEXT
595: vkGetShaderModuleCreateInfoIdentifierEXT vkCreateOpticalFlowSessionNV vkDestroyOpticalFlowSessionNV vkBindOpticalFlowSessionImageNV vkCmdOpticalFlowExecuteNV vkAntiLagUpdateAMD
601: vkCreateShadersEXT vkDestroyShaderEXT vkGetShaderBinaryDataEXT vkCmdBindShadersEXT vkCmdSetDepthClampRangeEXT vkGetFramebufferTilePropertiesQCOM
607: vkGetDynamicRenderingTilePropertiesQCOM vkConvertCooperativeVectorMatrixNV vkCmdConvertCooperativeVectorMatrixNV vkSetLatencySleepModeNV vkLatencySleepNV vkSetLatencyMarkerNV
613: vkGetLatencyTimingsNV vkQueueNotifyOutOfBandNV vkCreateDataGraphPipelinesARM vkCreateDataGraphPipelineSessionARM vkGetDataGraphPipelineSessionBindPointRequirementsARM vkGetDataGraphPipelineSessionMemoryRequirementsARM
619: vkBindDataGraphPipelineSessionMemoryARM vkDestroyDataGraphPipelineSessionARM vkCmdDispatchDataGraphARM vkGetDataGraphPipelineAvailablePropertiesARM vkGetDataGraphPipelinePropertiesARM vkCmdSetAttachmentFeedbackLoopEnableEXT
625: vkCmdBindTileMemoryQCOM vkCmdDecompressMemoryEXT vkCmdDecompressMemoryIndirectCountEXT vkCreateExternalComputeQueueNV vkDestroyExternalComputeQueueNV vkGetExternalComputeQueueDataNV
631: vkGetClusterAccelerationStructureBuildSizesNV vkCmdBuildClusterAccelerationStructureIndirectNV vkGetPartitionedAccelerationStructuresBuildSizesNV vkCmdBuildPartitionedAccelerationStructuresNV vkGetGeneratedCommandsMemoryRequirementsEXT vkCmdPreprocessGeneratedCommandsEXT
637: vkCmdExecuteGeneratedCommandsEXT vkCreateIndirectCommandsLayoutEXT vkDestroyIndirectCommandsLayoutEXT vkCreateIndirectExecutionSetEXT vkDestroyIndirectExecutionSetEXT vkUpdateIndirectExecutionSetPipelineEXT
643: vkUpdateIndirectExecutionSetShaderEXT vkCreateShaderInstrumentationARM vkDestroyShaderInstrumentationARM vkCmdBeginShaderInstrumentationARM vkCmdEndShaderInstrumentationARM vkGetShaderInstrumentationValuesARM
649: vkClearShaderInstrumentationMetricsARM vkCmdEndRendering2EXT vkCmdBeginCustomResolveEXT vkCmdSetComputeOccupancyPriorityNV vkCmdSetPrimitiveRestartIndexEXT vkCreateAccelerationStructureKHR
655: vkDestroyAccelerationStructureKHR vkCmdBuildAccelerationStructuresKHR vkCmdBuildAccelerationStructuresIndirectKHR vkBuildAccelerationStructuresKHR vkCopyAccelerationStructureKHR vkCopyAccelerationStructureToMemoryKHR
661: vkCopyMemoryToAccelerationStructureKHR vkWriteAccelerationStructuresPropertiesKHR vkCmdCopyAccelerationStructureKHR vkCmdCopyAccelerationStructureToMemoryKHR vkCmdCopyMemoryToAccelerationStructureKHR vkGetAccelerationStructureDeviceAddressKHR
667: vkCmdWriteAccelerationStructuresPropertiesKHR vkGetDeviceAccelerationStructureCompatibilityKHR vkGetAccelerationStructureBuildSizesKHR vkCmdTraceRaysKHR vkCreateRayTracingPipelinesKHR vkGetRayTracingCaptureReplayShaderGroupHandlesKHR
673: vkCmdTraceRaysIndirectKHR vkGetRayTracingShaderGroupStackSizeKHR vkCmdSetRayTracingPipelineStackSizeKHR vkCmdDrawMeshTasksEXT vkCmdDrawMeshTasksIndirectEXT vkCmdDrawMeshTasksIndirectCountEXT
679: vkGetMemoryWin32HandleKHR vkGetMemoryWin32HandlePropertiesKHR vkImportSemaphoreWin32HandleKHR vkGetSemaphoreWin32HandleKHR vkImportFenceWin32HandleKHR vkGetFenceWin32HandleKHR
685: vkGetMemoryWin32HandleNV vkAcquireFullScreenExclusiveModeEXT vkReleaseFullScreenExclusiveModeEXT vkGetDeviceGroupSurfacePresentModes2EXT vkExportMetalObjectsEXT vkGetMemoryMetalHandleEXT
691: vkGetMemoryMetalHandlePropertiesEXT vkGetAndroidHardwareBufferPropertiesANDROID vkGetMemoryAndroidHardwareBufferANDROID vxDestroyImages vxDestroyImageViews vxDestroyBuffers
697: vxDestroyBufferViews```

The KSA game's build of the same table requests 634 names, all a subset of this list; the open-source tree adds 63 newer registry entries.[^ksa-procs] An implementation built against the open-source tree therefore also satisfies KSA-era code.

# Open questions

* UNVERIFIED: whether the desktop Khronos loader or any implicit layer ever resolves the `vx*` names through `vkGetDeviceProcAddr`. On desktop it should return NULL, so the generated `DestroyImages`/`DestroyBuffers` helpers would crash if called. Nothing in the open tree calls them.
* Design (for `/webgpu/`): how the browser implementation exposes a canvas-backed `VkSurfaceKHR`. Options are `vkCreateHeadlessSurfaceEXT` plus a custom `pNext`, reusing `VkMetalSurfaceCreateInfoEXT`, or a C#-only API used by the GLFW replacement.

[^import-resolver]: Brutal.Vulkan/ImportResolver.cs
[^instance-cs]: Manual/Classes/Instance.cs
[^physdev-cs]: Manual/Classes/PhysicalDevice.cs
[^device-cs]: Manual/Classes/Device.cs
[^queue-cs]: Manual/Classes/Queue.cs
[^vkinstance-gen]: VkInstance.FunctionTable constructor
[^vkdevice-gen]: VkDevice.FunctionTable constructor
[^instext-gen]: Generated instance extensions (GetProcAddr)
[^devext-gen]: Generated device extensions (vx* batch destroy)
[^pinvoke-gen]: Global LibraryImport externs
[^helpers-cs]: Brutal.Vulkan.Abstractions/VulkanHelpers.cs
[^proc-lists]: procnames.py extraction script
[^vulkanex]: libVulkanEx.dylib from Ahwoo.BRUTAL.Vulkan
[^ksa-procs]: KSA decompiled VkDevice.FunctionTable
[^fakeicd]: Fake C# ICD test program
