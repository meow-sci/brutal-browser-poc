---
type: Recipe
title: Desktop "hello mesh" in pure BRUTAL (GLFW + Brutal.Vulkan + Abstractions) — verified call sequence
description: A complete, compiled-and-run (macOS/MoltenVK, validation-clean) BRUTAL program that opens a GLFW window, creates instance/surface/device/swapchain, uploads an indexed cube via StagingPool, binds a per-frame UBO through DescriptorPoolEx/DescriptorSetLayoutEx, draws with Vulkan 1.3 dynamic rendering and presents — plus the exact list of Vulkan commands it needs, i.e. what the browser Vulkan-on-WebGPU layer must support for the PoC.
tags: [brutal, recipe, vulkan, glfw, dynamic-rendering, swapchain, poc, desktop-reference]
sources:
  - id: program
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/build/hellomesh/Program.cs
    title: hellomesh Program.cs (built against NuGet Ahwoo.BRUTAL.Vulkan.Abstractions 2026.10.2 + Ahwoo.BRUTAL.Glfw 2026.10.4)
  - id: csproj
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/build/hellomesh/hellomesh.csproj
    title: hellomesh.csproj (net10.0, osx-arm64, AllowUnsafeBlocks)
  - id: shaders
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/build/hellomesh/shaders/
    title: mesh.vert / mesh.frag GLSL, compiled to SPIR-V with glslc from Vulkan SDK 1.4.328.1
  - id: abstractions
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/research/brutal/vulkan-abstractions-catalogue-and-icd-surface.md
    title: Abstractions catalogue (helper → vk command mapping)
  - id: contract
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/research/brutal/vulkan-loader-and-proc-addr-contract.md
    title: Loader / proc-addr contract
  - id: ksa
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/research/brutal/ksa-bootstrap-reference.md
    title: KSA bootstrap reference (how a shipped game does the same steps)
  - id: brutal-readme
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/README.md
    title: BRUTAL README (first-window sample, GLFW + no client API)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:16:59Z }
status: draft
---

# Status

The program below was **compiled and run** on 2026-10-16 with .NET SDK 10.0.100 against the NuGet packages `Ahwoo.BRUTAL.Vulkan.Abstractions` 2026.10.2 (which pulls Vulkan, Core, Memory, Collections, Strings, Logging and Numerics) and `Ahwoo.BRUTAL.Glfw` 2026.10.4. It ran on an Apple M4 Pro through MoltenVK (Vulkan 1.3.323, LunarG SDK 1.4.328.1 in `/usr/local/lib`).[^program][^csproj] It renders 180 frames of a rotating vertex-coloured cube and exits cleanly. Under `VK_INSTANCE_LAYERS=VK_LAYER_KHRONOS_validation` it is **validation-clean**. The first run flagged one error, an HDR10 colour space picked with the wrong format, and the surface-format choice was fixed. Shaders are GLSL 450, precompiled to SPIR-V with `glslc`.[^shaders]

The same program is the **target workload** for the browser Vulkan implementation. Only steps 1 (window), 3 (surface) and the SPIR-V source change in the browser.

# Call sequence (BRUTAL API → Vulkan command)

| # | Step | BRUTAL API used (exact) | Vulkan / native calls underneath |
|---|---|---|---|
| 1 | Window, no GL context | `Glfw.Init()`; `Glfw.WindowHint(GlfwWindowHint.ClientApi, 0)`; `Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title, Size = new int2(1280, 720) })`; events `window.OnKey`, `OnMouseButton`, `OnCursorPos`, `OnScroll`[^brutal-readme] | `glfwInit`, `glfwWindowHint`, `glfwCreateWindow`, `glfwSet*Callback` |
| 2 | Instance | `VulkanHelpers.AddPlatformRequiredExtensions(set)`; `VulkanHelpers.AddSurfaceExtensions(set)`; `Glfw.GetRequiredInstanceExtensions(out uint)` returns `byte**` (marshal with `Marshal.PtrToStringUTF8`); `Vulkan.CreateInstance(new Instance.CreateInfo { AppInfo = new VkApplicationInfo { ApiVersion = (int)VulkanHelpers.Api.VERSION_1_3 }, Flags = VulkanHelpers.GetPlatformRequiredInstanceCreateFlags(), EnabledExtensions, EnabledLayers = new() })` | `vkGetInstanceProcAddr` (static ctor), `vkCreateInstance`, 105 instance proc lookups, `vkEnumeratePhysicalDevices` |
| 3 | Surface | `Glfw.CreateWindowSurface(instance.VkHandle, window, out ulong raw)` returns 0 on success; `new VkSurfaceKHR((nint)raw)` | inside GLFW: `vkCreate{Metal,Win32,Xlib,Wayland}SurfaceEXT/KHR` |
| 4 | Physical device + queue family | `instance.PhysicalDevices.First(p => p.SupportsExtension(VK.KHR_SWAPCHAIN_EXTENSION_NAME))`; `physicalDevice.FindQueueFamily(VkQueueFlags.GraphicsBit)` (Abstractions); `physicalDevice.GetSurfaceSupportKHR(family, surface)`; `physicalDevice.GetDeviceName()` | `vkEnumerateDeviceExtensionProperties`, `vkGetPhysicalDeviceQueueFamilyProperties`, `vkGetPhysicalDeviceSurfaceSupportKHR`, `vkGetPhysicalDeviceProperties` |
| 5 | Device | `VulkanHelpers.AddMandatoryIfSupportedDeviceExtensions(set, pd)`; `physicalDevice.CreateDevice(new DeviceEx.CreateInfo { Extensions = { VK.KHR_SWAPCHAIN_EXTENSION_NAME }, QueueCreateInfos = …, Next = &new VkPhysicalDeviceVulkan13Features { DynamicRendering = true, Synchronization2 = true } })` returns **`DeviceEx`**; `device.GetQueue(family, 0)` | `vkCreateDevice`, `vkGetInstanceProcAddr("vkGetDeviceProcAddr")`, 697 device lookups, `vkGetDeviceQueue` |
| 6 | Swapchain (raw; Abstractions has no helper) | `pd.GetSurfaceCapabilitiesKHR(surface)`; `pd.GetSurfaceFormatsKHR_Count/_Write`; `device.CreateSwapchainKHR(new VkSwapchainCreateInfoKHR{…}, null)`; `device.GetSwapchainImagesKHR_Count/_Write`; `device.CreateImageView(new VkImageViewCreateInfo{ SubresourceRange = Subresources.Full(ColorBit) }, null)`; `device.CreateSemaphore(new VkSemaphoreCreateInfo(), null)` per image | `vkGetPhysicalDeviceSurfaceCapabilitiesKHR`, `vkGetPhysicalDeviceSurfaceFormatsKHR`, `vkCreateSwapchainKHR`, `vkGetSwapchainImagesKHR`, `vkCreateImageView`, `vkCreateSemaphore` |
| 7 | Depth image | `device.CreateImage(new ImageEx.CreateInfo { Name, ImageFormat = D32SFloat, …, AllocPreference = PreferGpu, AllocRequiredProperties = DeviceLocalBit })` returns `ImageEx`; `depth.CreateImageView(VkImageViewType._2D, Subresources.Full(DepthBit), null)` returns `ImageViewEx` | `vkCreateImage`, `vkGetImageMemoryRequirements`, `vkGetPhysicalDeviceMemoryProperties`, `vkAllocateMemory`, `vkBindImageMemory`, `vkCreateImageView` |
| 8 | Mesh upload | `device.CreateBuffer(new BufferEx.CreateInfo { Name, BufferSize = ByteSize.Of<Vertex>(n), BufferUsage = VertexBufferBit \| TransferDstBit, AllocRequiredProperties = DeviceLocalBit })` returns `BufferEx`; `device.CreateStagingPool(queue, 1)`; `staging.AddStagingBuffer<T>(span)` (map, copy, unmap); `staging.NextCommandBuffer()`; `cmd.Begin(OneTimeSubmitBit)`; `cmd.CopyBuffer(src, dst, ByteSize)`; `cmd.End()`; `staging.Submit().Wait()` | `vkCreateBuffer`, `vkGetBufferMemoryRequirements`, `vkAllocateMemory`, `vkBindBufferMemory`, `vkCreateCommandPool`, `vkAllocateCommandBuffers`, `vkMapMemory`, `vkUnmapMemory`, `vkBeginCommandBuffer`, `vkCmdCopyBuffer`, `vkEndCommandBuffer`, `vkCreateFence`, `vkQueueSubmit`, `vkWaitForFences(timeout = -1)`, `vkResetFences`, `vkResetCommandPool`, `vkDestroyCommandPool`, `vkDestroyFence`, `vkDestroyBuffer`, `vkFreeMemory` |
| 9 | Uniforms + descriptors | per frame: host-visible/coherent `BufferEx` + `buffer.Map()` returns **persistently mapped** `MappedMemory`; `mapped.As<float4x4>() = mvp` each frame; `device.CreateDescriptorSetLayout(new DescriptorSetLayoutEx.CreateInfo { Bindings }, null)`; `device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo { MaxSets, PoolSizes }, null)`; `device.AllocateDescriptorSet(pool, layout)`; `device.UpdateDescriptorSets([write], [])` | `vkMapMemory` (kept mapped), `vkCreateDescriptorSetLayout`, `vkCreateDescriptorPool`, `vkAllocateDescriptorSets`, `vkUpdateDescriptorSets` |
| 10 | Shaders + pipeline | `ShaderModuleExtensions.FromSpirv(device, bytes, null)`; `new ShaderStages(device, [Presets.ShaderStage.Vertex(vs), Presets.ShaderStage.Fragment(fs)], null)`; `device.CreatePipelineLayout([setLayout.Handle], [], null)` (Abstractions); `VertexInput.For<Vertex>()`; `device.CreateGraphicsPipeline(default, new VkGraphicsPipelineCreateInfo { Next = &VkPipelineRenderingCreateInfo{…}, Stages = stages, VertexInputState = vertexInput, InputAssemblyState = Presets.InputAssembly.TriangleList, RasterizationState = Presets.Rasterization.Fill.CullNone, MultisampleState = Presets.Multisample.None, DepthStencilState = Presets.DepthStencil.DepthTestWrite, ColorBlendState = Presets.BlendState.BlendNone, DynamicState = {Viewport, Scissor}, Layout }, null)` | `vkCreateShaderModule`, `vkCreatePipelineLayout`, `vkCreateGraphicsPipelines` (no render pass; dynamic rendering) |
| 11 | Frame sync | `device.CreateCommandPool(... ResetCommandBufferBit)`; `new SyncObjects(device) { ImageReadySemaphore, RenderingFinishedSemaphore, Fence = device.CreateFence(SignaledBit, null), CommandBuffer = device.AllocateCommandBuffer(...), CommandPool }` ×2 | `vkCreateCommandPool`, `vkCreateSemaphore`, `vkCreateFence`, `vkAllocateCommandBuffers` |
| 12 | Per frame | `Glfw.PollEvents()`; `device.WaitForFence(fence, -1)`; `device.ResetFences([fence])`; `device.AcquireNextImageKHR(swapchain, -1, imageReady, default, out idx)`; `cmd.Reset()`; `cmd.Begin(OneTimeSubmitBit)`; `cmd.PipelineBarrier(…, [], [], [VkImageMemoryBarrier…])`; `cmd.BeginRendering(new VkRenderingInfo{…})`; `cmd.BindPipeline`; `cmd.SetViewport`; `cmd.SetScissor`; `cmd.BindVertexBuffer(0, vertexBuffer)` (IBindBuffer); `cmd.BindIndexBuffer(indexBuffer, UInt16)`; `cmd.BindDescriptorSets(Graphics, layout, 0, [set], [])`; `cmd.DrawIndexed(n,1,0,0,0)`; `cmd.EndRendering()`; barrier to `PresentSrcKHR`; `cmd.End()`; `queue.Submit([imageReady], [ColorAttachmentOutputBit], [cmd], [presentReady[idx]], fence)` (Abstractions `QueueExtensions`); `queue.PresentKHR(new VkPresentInfoKHR{…})` | `vkWaitForFences`, `vkResetFences`, `vkAcquireNextImageKHR`, `vkResetCommandBuffer`, `vkBeginCommandBuffer`, `vkCmdPipelineBarrier` ×2, `vkCmdBeginRendering`, `vkCmdBindPipeline`, `vkCmdSetViewport`, `vkCmdSetScissor`, `vkCmdBindVertexBuffers`, `vkCmdBindIndexBuffer`, `vkCmdBindDescriptorSets`, `vkCmdDrawIndexed`, `vkCmdEndRendering`, `vkEndCommandBuffer`, `vkQueueSubmit`, `vkQueuePresentKHR` |
| 13 | Teardown | `device.WaitIdle()`; `SyncObjects.Dispose()`; `Destroy*`; `using` disposal of `BufferEx`/`ImageEx`/`ImageViewEx`/`DescriptorPoolEx`/`DescriptorSetLayoutEx`/`ShaderStages`/`DeviceEx`/`Instance`; `instance.DestroySurfaceKHR`; `Glfw.Terminate()` | `vkDeviceWaitIdle`, `vkFreeCommandBuffers`, `vkDestroy{Fence,Semaphore,CommandPool,Pipeline,PipelineLayout,ImageView,Image,Buffer,ShaderModule,DescriptorPool,DescriptorSetLayout,SwapchainKHR,SurfaceKHR,Device,Instance}`, `vkUnmapMemory`, `vkFreeMemory` |

Convenience helpers (Abstractions) vs raw generated wrappers in this recipe:

* **Abstractions:** `DeviceEx`, `PhysicalDevice.CreateDevice(DeviceEx.CreateInfo)`, `FindQueueFamily`, `GetDeviceName`, `BufferEx`/`ImageEx`/`ImageViewEx`, `IMappable.Map()`/`MappedMemory.As<T>()`, `StagingPool` (`CreateStagingPool`, `AddStagingBuffer<T>`, `NextCommandBuffer`, `Submit`, `Wait`), `CommandBuffer.Begin(usage)`, `CopyBuffer(src, dst, size)`, `BindVertexBuffer(binding, IBindBuffer)`, `BindIndexBuffer(IBindBuffer, type)`, `DescriptorSetLayoutEx`/`DescriptorPoolEx`/`AllocateDescriptorSet`, `ShaderModuleExtensions.FromSpirv`, `ShaderStages`, `Presets.*`, `VertexInput.For<T>()`, `CreatePipelineLayout(spans)`, `CreateGraphicsPipeline(cache, info, alloc)`, `CreateFence(flags, alloc)`, `WaitForFence(fence, nint)`, `AllocateCommandBuffer(info)`, `QueueExtensions.Submit(waits, stages, cmds, signals, fence)`, `SyncObjects`, `Subresources.Full`, and `VulkanHelpers.*`.[^abstractions]
* **Raw generated wrappers:** surface queries, swapchain create, images and acquire, `CreateImageView`, `CreateSemaphore`, `CreateCommandPool`, `UpdateDescriptorSets`, `ResetFences`, every `cmd.*` recording call (`PipelineBarrier`, `BeginRendering`, `BindPipeline`, `SetViewport/Scissor`, `BindDescriptorSets`, `DrawIndexed`, `EndRendering`, `End`, `Reset`), `Queue.PresentKHR`, `WaitIdle`, `Destroy*`.

# Vulkan command set this workload needs (browser implementation checklist)

Instance level: `vkGetInstanceProcAddr`, `vkCreateInstance`, `vkDestroyInstance`, `vkEnumeratePhysicalDevices`, `vkGetPhysicalDeviceProperties`, `vkGetPhysicalDeviceQueueFamilyProperties`, `vkGetPhysicalDeviceMemoryProperties`, `vkEnumerateDeviceExtensionProperties`, `vkGetPhysicalDeviceSurfaceSupportKHR`, `vkGetPhysicalDeviceSurfaceCapabilitiesKHR`, `vkGetPhysicalDeviceSurfaceFormatsKHR`, `vkDestroySurfaceKHR`, `vkCreateDevice`, plus a surface-creation entry point that the browser GLFW replacement calls.

Device level, 50 commands: `vkGetDeviceProcAddr`, `vkGetDeviceQueue`, `vkDestroyDevice`, `vkDeviceWaitIdle`; `vkCreateSwapchainKHR`, `vkGetSwapchainImagesKHR`, `vkAcquireNextImageKHR`, `vkQueuePresentKHR`, `vkDestroySwapchainKHR`; `vkCreateImage`, `vkDestroyImage`, `vkGetImageMemoryRequirements`, `vkBindImageMemory`, `vkCreateImageView`, `vkDestroyImageView`; `vkCreateBuffer`, `vkDestroyBuffer`, `vkGetBufferMemoryRequirements`, `vkBindBufferMemory`, `vkAllocateMemory`, `vkFreeMemory`, `vkMapMemory`, `vkUnmapMemory`; `vkCreateDescriptorSetLayout`, `vkDestroyDescriptorSetLayout`, `vkCreateDescriptorPool`, `vkDestroyDescriptorPool`, `vkAllocateDescriptorSets`, `vkUpdateDescriptorSets`; `vkCreateShaderModule`, `vkDestroyShaderModule`, `vkCreatePipelineLayout`, `vkDestroyPipelineLayout`, `vkCreateGraphicsPipelines`, `vkDestroyPipeline`; `vkCreateCommandPool`, `vkDestroyCommandPool`, `vkResetCommandPool`, `vkAllocateCommandBuffers`, `vkFreeCommandBuffers`, `vkBeginCommandBuffer`, `vkEndCommandBuffer`, `vkResetCommandBuffer`; `vkCreateFence`, `vkDestroyFence`, `vkWaitForFences`, `vkResetFences`, `vkCreateSemaphore`, `vkDestroySemaphore`; `vkQueueSubmit`.

Commands, 11: `vkCmdPipelineBarrier`, `vkCmdBeginRendering`, `vkCmdEndRendering`, `vkCmdBindPipeline`, `vkCmdSetViewport`, `vkCmdSetScissor`, `vkCmdBindVertexBuffers`, `vkCmdBindIndexBuffer`, `vkCmdBindDescriptorSets`, `vkCmdDrawIndexed`, `vkCmdCopyBuffer`.

Everything else in the 697-entry device table may return NULL ([/brutal/vulkan-loader-and-proc-addr-contract.md](/brutal/vulkan-loader-and-proc-addr-contract.md)).[^contract] For the PoC's mouse/keyboard interaction, the recipe already updates a UBO from `OnCursorPos`/`OnMouseButton`/`OnScroll`/`OnKey`. Loading a glTF model instead of the cube changes only step 8; see [/brutal/asset-libraries.md](/brutal/asset-libraries.md).

# Browser deltas

| Step | Desktop | Browser PoC |
|---|---|---|
| 1 Window/input | native GLFW | browser `Brutal.Glfw` replacement on canvas/DOM ([/brutal/windowing-input-audio-apis.md](/brutal/windowing-input-audio-apis.md)) |
| 2 Instance | `vkGetInstanceProcAddr` from `libvulkan`/`VulkanEx` | patched `Brutal.Vulkan` loader returns the C# implementation's entry point ([/brutal/vulkan-binding-architecture.md](/brutal/vulkan-binding-architecture.md)) |
| 3 Surface | `glfwCreateWindowSurface` | the GLFW replacement calls the implementation's canvas-surface factory |
| 10 Shaders | SPIR-V from `glslc` | SPIR-V → WGSL at `vkCreateShaderModule`, or pre-translated WGSL (see `/shaders/`) |
| 12 Loop | `for (;;) { PollEvents(); … }` | per-frame callback driven by `requestAnimationFrame`. A blocking loop never yields to the browser (see `/dotnet-wasm/`) |
| 12 Fences | real GPU waits | `vkWaitForFences` must not block the main thread; signal at submit |
| 9 Mapped UBO | host-coherent pointer | shadow memory in wasm linear memory, flushed with `writeBuffer` at `vkQueueSubmit` |

# Full source (as compiled and run)

`hellomesh.csproj`: `net10.0`, `OutputType Exe`, `AllowUnsafeBlocks`, `RuntimeIdentifier osx-arm64`; `PackageReference Ahwoo.BRUTAL.Vulkan.Abstractions 2026.10.2` and `Ahwoo.BRUTAL.Glfw 2026.10.4`; `shaders/*.spv` copied to the output. Shaders:[^shaders]

```glsl
// mesh.vert
#version 450
layout(set = 0, binding = 0) uniform Ubo { mat4 mvp; } ubo;
layout(location = 0) in vec3 inPos;
layout(location = 1) in vec3 inColor;
layout(location = 0) out vec3 vColor;
void main() { vColor = inColor; gl_Position = ubo.mvp * vec4(inPos, 1.0); }
// mesh.frag
#version 450
layout(location = 0) in vec3 vColor;
layout(location = 0) out vec4 outColor;
void main() { outColor = vec4(vColor, 1.0); }
```

`Program.cs`:[^program]

```csharp
// BRUTAL desktop "hello mesh": GLFW window + Vulkan 1.3 dynamic rendering + indexed cube + UBO.
// Uses only Ahwoo.BRUTAL.Glfw + Ahwoo.BRUTAL.Vulkan(.Abstractions) public APIs.
using System.Runtime.InteropServices;
using Brutal;
using Brutal.GlfwApi;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

int maxFrames = args.Length > 0 ? int.Parse(args[0]) : int.MaxValue;
const int FramesInFlight = 2;

// ---- 1. Window (no client API) -------------------------------------------------------------
if (!Glfw.Init()) throw new Exception("glfwInit failed");
Glfw.WindowHint(GlfwWindowHint.ClientApi, 0);
var window = Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title = "BRUTAL hello mesh", Size = new int2(1280, 720) });
float yaw = 0, pitch = 0.4f, distance = 4f; double2 lastCursor = default; bool dragging = false;
window.OnKey += (w, key, sc, action, mods) =>
{
    if (key == GlfwKey.Escape) w.ShouldClose = true;
    if (action != GlfwKeyAction.Release && key == GlfwKey.Up) distance = MathF.Max(1.5f, distance - 0.2f);
    if (action != GlfwKeyAction.Release && key == GlfwKey.Down) distance += 0.2f;
};
window.OnMouseButton += (w, button, action, mods) => dragging = button == GlfwMouseButton.Left && action == GlfwButtonAction.Press;
window.OnCursorPos += (w, pos) => { if (dragging) { yaw += (float)(pos.X - lastCursor.X) * 0.01f; pitch += (float)(pos.Y - lastCursor.Y) * 0.01f; } lastCursor = pos; };
window.OnScroll += (w, offset) => distance = MathF.Max(1.5f, distance - (float)offset.Y * 0.2f);

unsafe
{
    // ---- 2. Instance ------------------------------------------------------------------------
    var instanceExtensions = new HashSet<string>();
    VulkanHelpers.AddPlatformRequiredExtensions(instanceExtensions);   // macOS: portability enumeration
    VulkanHelpers.AddSurfaceExtensions(instanceExtensions);            // VK_KHR_surface
    byte** required = Glfw.GetRequiredInstanceExtensions(out uint requiredCount);
    for (var i = 0; i < requiredCount; i++) instanceExtensions.Add(Marshal.PtrToStringUTF8((nint)required[i])!);
    using var instance = Vulkan.CreateInstance(new Instance.CreateInfo
    {
        AppInfo = new VkApplicationInfo { ApiVersion = (int)VulkanHelpers.Api.VERSION_1_3 },
        Flags = VulkanHelpers.GetPlatformRequiredInstanceCreateFlags(),
        EnabledExtensions = instanceExtensions,
        EnabledLayers = new HashSet<string>(),
    });

    // ---- 3. Surface (created inside native GLFW) -------------------------------------------
    if (Glfw.CreateWindowSurface(instance.VkHandle, window, out ulong rawSurface) != 0) throw new Exception("surface");
    var surface = new VkSurfaceKHR((nint)rawSurface);

    // ---- 4. Physical device + queue family ---------------------------------------------------
    var physicalDevice = instance.PhysicalDevices.First(p => p.SupportsExtension(VK.KHR_SWAPCHAIN_EXTENSION_NAME));
    var family = physicalDevice.FindQueueFamily(VkQueueFlags.GraphicsBit);
    if (!physicalDevice.GetSurfaceSupportKHR(family, surface)) throw new Exception("no present support");
    Console.WriteLine($"GPU: {physicalDevice.GetDeviceName()}");

    // ---- 5. Logical device (DeviceEx = allocator too) ---------------------------------------
    var deviceExtensions = new HashSet<string> { VK.KHR_SWAPCHAIN_EXTENSION_NAME };
    VulkanHelpers.AddMandatoryIfSupportedDeviceExtensions(deviceExtensions, physicalDevice); // VK_KHR_portability_subset
    var features13 = new VkPhysicalDeviceVulkan13Features { DynamicRendering = true, Synchronization2 = true };
    var priority = 1f;
    var queueInfo = new VkDeviceQueueCreateInfo { QueueFamilyIndex = family, QueueCount = 1, QueuePriorities = &priority };
    using var device = physicalDevice.CreateDevice(new DeviceEx.CreateInfo
    {
        Extensions = deviceExtensions,
        QueueCreateInfos = new ReadOnlySpan<VkDeviceQueueCreateInfo>(&queueInfo, 1),
        Next = &features13,
    });
    var queue = device.GetQueue(family, 0);

    // ---- 6. Swapchain (raw Brutal.Vulkan; Abstractions has no helper) -----------------------
    var caps = physicalDevice.GetSurfaceCapabilitiesKHR(surface);
    Span<VkSurfaceFormatKHR> formats = stackalloc VkSurfaceFormatKHR[physicalDevice.GetSurfaceFormatsKHR_Count(surface)];
    physicalDevice.GetSurfaceFormatsKHR_Write(surface, formats);
    var surfaceFormat = formats[0];
    foreach (var f in formats) if (f.Format == VkFormat.B8G8R8A8UNorm && f.ColorSpace == VkColorSpaceKHR.SRGBNonlinearKHR) { surfaceFormat = f; break; }
    var extent = caps.CurrentExtent.Width == -1 ? new VkExtent2D(window.FramebufferSize.X, window.FramebufferSize.Y) : caps.CurrentExtent;
    var swapchain = device.CreateSwapchainKHR(new VkSwapchainCreateInfoKHR
    {
        Surface = surface,
        MinImageCount = Math.Max(caps.MinImageCount, FramesInFlight + 1),
        ImageFormat = surfaceFormat.Format,
        ImageColorSpace = surfaceFormat.ColorSpace,
        ImageExtent = extent,
        ImageArrayLayers = 1,
        ImageUsage = VkImageUsageFlags.ColorAttachmentBit,
        ImageSharingMode = VkSharingMode.Exclusive,
        PreTransform = caps.CurrentTransform,
        CompositeAlpha = VkCompositeAlphaFlagsKHR.OpaqueBitKHR,
        PresentMode = VkPresentModeKHR.FifoKHR,
        Clipped = true,
    }, null);
    var swapImages = new VkImage[device.GetSwapchainImagesKHR_Count(swapchain)];
    device.GetSwapchainImagesKHR_Write(swapchain, swapImages);
    var swapViews = new VkImageView[swapImages.Length];
    var presentReady = new VkSemaphore[swapImages.Length];
    for (var i = 0; i < swapImages.Length; i++)
    {
        swapViews[i] = device.CreateImageView(new VkImageViewCreateInfo
        {
            Image = swapImages[i], ViewType = VkImageViewType._2D, Format = surfaceFormat.Format,
            SubresourceRange = Subresources.Full(VkImageAspectFlags.ColorBit),
        }, null);
        presentReady[i] = device.CreateSemaphore(new VkSemaphoreCreateInfo(), null);
    }

    // ---- 7. Depth buffer via ImageEx (DeviceEx allocates + binds memory) --------------------
    using var depth = device.CreateImage(new ImageEx.CreateInfo
    {
        Name = "depth", ImageType = VkImageType._2D, ImageFormat = VkFormat.D32SFloat,
        ImageExtent = new VkExtent3D(extent.Width, extent.Height, 1), ImageMipLevels = 1, ImageArrayLayers = 1,
        ImageSamples = VkSampleCountFlags._1Bit, ImageTiling = VkImageTiling.Optimal,
        ImageUsage = VkImageUsageFlags.DepthStencilAttachmentBit, ImageSharingMode = VkSharingMode.Exclusive,
        AllocPreference = MemoryPreference.PreferGpu, AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
    });
    using var depthView = depth.CreateImageView(VkImageViewType._2D, Subresources.Full(VkImageAspectFlags.DepthBit), null);

    // ---- 8. Mesh: device-local vertex/index buffers uploaded through StagingPool ------------
    Vertex[] vertices =
    [
        new(new(-1,-1,-1), new(0,0,0)), new(new( 1,-1,-1), new(1,0,0)), new(new( 1, 1,-1), new(1,1,0)), new(new(-1, 1,-1), new(0,1,0)),
        new(new(-1,-1, 1), new(0,0,1)), new(new( 1,-1, 1), new(1,0,1)), new(new( 1, 1, 1), new(1,1,1)), new(new(-1, 1, 1), new(0,1,1)),
    ];
    ushort[] indices = [0,1,2, 2,3,0, 4,6,5, 6,4,7, 0,3,7, 7,4,0, 1,5,6, 6,2,1, 3,2,6, 6,7,3, 0,4,5, 5,1,0];
    using var vertexBuffer = device.CreateBuffer(new BufferEx.CreateInfo
    {
        Name = "cube.vb", BufferSize = ByteSize.Of<Vertex>(vertices.Length),
        BufferUsage = VkBufferUsageFlags.VertexBufferBit | VkBufferUsageFlags.TransferDstBit,
        AllocPreference = MemoryPreference.PreferGpu, AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
    });
    using var indexBuffer = device.CreateBuffer(new BufferEx.CreateInfo
    {
        Name = "cube.ib", BufferSize = ByteSize.Of<ushort>(indices.Length),
        BufferUsage = VkBufferUsageFlags.IndexBufferBit | VkBufferUsageFlags.TransferDstBit,
        AllocPreference = MemoryPreference.PreferGpu, AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
    });
    using (var staging = device.CreateStagingPool(queue, 1))
    {
        var stagedVertices = staging.AddStagingBuffer<Vertex>(vertices);   // map + copy + unmap (host-coherent)
        var stagedIndices = staging.AddStagingBuffer<ushort>(indices);
        var upload = staging.NextCommandBuffer();
        upload.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
        upload.CopyBuffer(stagedVertices.VkBuffer, vertexBuffer.VkBuffer, vertexBuffer.BufferSize);
        upload.CopyBuffer(stagedIndices.VkBuffer, indexBuffer.VkBuffer, indexBuffer.BufferSize);
        upload.End();
        staging.Submit().Wait();                                            // vkQueueSubmit + vkWaitForFences(-1)
    }

    // ---- 9. Per-frame uniform buffers (persistently mapped) + descriptors --------------------
    var uniformBuffers = new BufferEx[FramesInFlight];
    var uniformMaps = new MappedMemory[FramesInFlight];
    for (var i = 0; i < FramesInFlight; i++)
    {
        uniformBuffers[i] = device.CreateBuffer(new BufferEx.CreateInfo
        {
            Name = $"ubo{i}", BufferSize = ByteSize.Of<float4x4>(), BufferUsage = VkBufferUsageFlags.UniformBufferBit,
            AllocPreference = MemoryPreference.PreferCpu,
            AllocRequiredProperties = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,
        });
        uniformMaps[i] = uniformBuffers[i].Map();
    }
    var uboBinding = new VkDescriptorSetLayoutBinding
    {
        Binding = 0, DescriptorType = VkDescriptorType.UniformBuffer, DescriptorCount = 1, StageFlags = VkShaderStageFlags.VertexBit,
    };
    using var setLayout = device.CreateDescriptorSetLayout(new DescriptorSetLayoutEx.CreateInfo { Bindings = new Span<VkDescriptorSetLayoutBinding>(&uboBinding, 1) }, null);
    var poolSize = new VkDescriptorPoolSize { Type = VkDescriptorType.UniformBuffer, DescriptorCount = FramesInFlight };
    using var descriptorPool = device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo { MaxSets = FramesInFlight, PoolSizes = new Span<VkDescriptorPoolSize>(&poolSize, 1) }, null);
    var sets = new VkDescriptorSet[FramesInFlight];
    for (var i = 0; i < FramesInFlight; i++)
    {
        sets[i] = device.AllocateDescriptorSet(descriptorPool, setLayout);
        var bufferInfo = new VkDescriptorBufferInfo { Buffer = uniformBuffers[i].VkBuffer, Offset = ByteSize.Zero, Range = VK.WHOLE_SIZE };
        device.UpdateDescriptorSets(
            [new VkWriteDescriptorSet { DstSet = sets[i], DstBinding = 0, DescriptorCount = 1, DescriptorType = VkDescriptorType.UniformBuffer, BufferInfo = &bufferInfo }],
            []);
    }

    // ---- 10. Shaders (precompiled SPIR-V) + pipeline (dynamic rendering) --------------------
    var vertModule = ShaderModuleExtensions.FromSpirv(device, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "shaders/mesh.vert.spv")), null);
    var fragModule = ShaderModuleExtensions.FromSpirv(device, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "shaders/mesh.frag.spv")), null);
    using var stages = new ShaderStages(device, [Presets.ShaderStage.Vertex(vertModule), Presets.ShaderStage.Fragment(fragModule)], null);
    var pipelineLayout = device.CreatePipelineLayout([setLayout.Handle], ReadOnlySpan<VkPushConstantRange>.Empty, null);
    var vertexInput = VertexInput.For<Vertex>();                         // reflection: float3 Pos, float3 Color
    var colorFormat = surfaceFormat.Format;
    var renderingInfo = new VkPipelineRenderingCreateInfo { ColorAttachmentCount = 1, ColorAttachmentFormats = &colorFormat, DepthAttachmentFormat = VkFormat.D32SFloat };
    var dynamicStates = stackalloc VkDynamicState[2] { VkDynamicState.Viewport, VkDynamicState.Scissor };
    var dynamicState = new VkPipelineDynamicStateCreateInfo { DynamicStateCount = 2, DynamicStates = dynamicStates };
    var viewportState = new VkPipelineViewportStateCreateInfo { ViewportCount = 1, ScissorCount = 1 };
    var pipeline = device.CreateGraphicsPipeline(default, new VkGraphicsPipelineCreateInfo
    {
        Next = &renderingInfo,
        StageCount = stages.Count, Stages = stages,
        VertexInputState = vertexInput,
        InputAssemblyState = Presets.InputAssembly.TriangleList,
        ViewportState = &viewportState,
        RasterizationState = Presets.Rasterization.Fill.CullNone,
        MultisampleState = Presets.Multisample.None,
        DepthStencilState = Presets.DepthStencil.DepthTestWrite,
        ColorBlendState = Presets.BlendState.BlendNone,
        DynamicState = &dynamicState,
        Layout = pipelineLayout,
    }, null);

    // ---- 11. Per-frame sync objects ----------------------------------------------------------
    var commandPool = device.CreateCommandPool(new VkCommandPoolCreateInfo { QueueFamilyIndex = family, Flags = VkCommandPoolCreateFlags.ResetCommandBufferBit }, null);
    var sync = new SyncObjects[FramesInFlight];
    for (var i = 0; i < FramesInFlight; i++)
        sync[i] = new SyncObjects(device)
        {
            ImageReadySemaphore = device.CreateSemaphore(new VkSemaphoreCreateInfo(), null),
            RenderingFinishedSemaphore = device.CreateSemaphore(new VkSemaphoreCreateInfo(), null),
            Fence = device.CreateFence(VkFenceCreateFlags.SignaledBit, null),
            CommandBuffer = device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo { CommandPool = commandPool, Level = VkCommandBufferLevel.Primary }),
            CommandPool = commandPool,
            Allocator = null,
        };

    // ---- 12. Frame loop: acquire -> record -> submit -> present ------------------------------
    var start = Glfw.Time;
    for (var frameNumber = 0; !window.ShouldClose && frameNumber < maxFrames; frameNumber++)
    {
        Glfw.PollEvents();
        var frame = frameNumber % FramesInFlight;
        var s = sync[frame];
        device.WaitForFence(s.Fence, -1);
        device.ResetFences([s.Fence]);
        device.AcquireNextImageKHR(swapchain, -1, s.ImageReadySemaphore, default, out var imageIndex);

        var t = (float)(Glfw.Time - start);
        var proj = float4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, extent.Width / (float)extent.Height, 0.1f, 100f);
        proj.Y.Y = -proj.Y.Y;                                               // Vulkan clip space is Y-down
        var view = float4x4.CreateLookAt(new float3(0, 0, distance), float3.Zero, float3.UnitY);
        var model = float4x4.CreateRotationY(yaw + t * 0.5f) * float4x4.CreateRotationX(pitch);
        uniformMaps[frame].As<float4x4>() = model * view * proj;            // row-vector convention -> GLSL "mvp * v"

        var cmd = s.CommandBuffer;
        cmd.Reset();
        cmd.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
        cmd.PipelineBarrier(VkPipelineStageFlags.TopOfPipeBit,
            VkPipelineStageFlags.ColorAttachmentOutputBit | VkPipelineStageFlags.EarlyFragmentTestsBit, VkDependencyFlags.None, [], [],
            [
                new VkImageMemoryBarrier { Image = swapImages[imageIndex], OldLayout = VkImageLayout.Undefined, NewLayout = VkImageLayout.ColorAttachmentOptimal,
                    DstAccessMask = VkAccessFlags.ColorAttachmentWriteBit, SrcQueueFamilyIndex = -1, DstQueueFamilyIndex = -1,
                    SubresourceRange = Subresources.Full(VkImageAspectFlags.ColorBit) },
                new VkImageMemoryBarrier { Image = depth.VkImage, OldLayout = VkImageLayout.Undefined, NewLayout = VkImageLayout.DepthAttachmentOptimal,
                    DstAccessMask = VkAccessFlags.DepthStencilAttachmentWriteBit, SrcQueueFamilyIndex = -1, DstQueueFamilyIndex = -1,
                    SubresourceRange = Subresources.Full(VkImageAspectFlags.DepthBit) },
            ]);
        var colorAttachment = new VkRenderingAttachmentInfo
        {
            ImageView = swapViews[imageIndex], ImageLayout = VkImageLayout.ColorAttachmentOptimal,
            LoadOp = VkAttachmentLoadOp.Clear, StoreOp = VkAttachmentStoreOp.Store,
            ClearValue = new VkClearColorValue { Float32 = new float4(0.08f, 0.08f, 0.12f, 1f) },
        };
        var depthAttachment = new VkRenderingAttachmentInfo
        {
            ImageView = depthView.VkImageView, ImageLayout = VkImageLayout.DepthAttachmentOptimal,
            LoadOp = VkAttachmentLoadOp.Clear, StoreOp = VkAttachmentStoreOp.DontCare,
            ClearValue = new VkClearDepthStencilValue { Depth = 1f },
        };
        cmd.BeginRendering(new VkRenderingInfo
        {
            RenderArea = new VkRect2D(extent), LayerCount = 1,
            ColorAttachmentCount = 1, ColorAttachments = &colorAttachment, DepthAttachment = &depthAttachment,
        });
        cmd.BindPipeline(VkPipelineBindPoint.Graphics, pipeline);
        cmd.SetViewport(0, [new VkViewport { Width = extent.Width, Height = extent.Height, MaxDepth = 1f }]);
        cmd.SetScissor(0, [new VkRect2D(extent)]);
        cmd.BindVertexBuffer(0, vertexBuffer);
        cmd.BindIndexBuffer(indexBuffer, VkIndexType.UInt16);
        cmd.BindDescriptorSets(VkPipelineBindPoint.Graphics, pipelineLayout, 0, [sets[frame]], []);
        cmd.DrawIndexed(indices.Length, 1, 0, 0, 0);
        cmd.EndRendering();
        cmd.PipelineBarrier(VkPipelineStageFlags.ColorAttachmentOutputBit, VkPipelineStageFlags.BottomOfPipeBit, VkDependencyFlags.None, [], [],
            [
                new VkImageMemoryBarrier { Image = swapImages[imageIndex], OldLayout = VkImageLayout.ColorAttachmentOptimal, NewLayout = VkImageLayout.PresentSrcKHR,
                    SrcAccessMask = VkAccessFlags.ColorAttachmentWriteBit, SrcQueueFamilyIndex = -1, DstQueueFamilyIndex = -1,
                    SubresourceRange = Subresources.Full(VkImageAspectFlags.ColorBit) },
            ]);
        cmd.End();

        queue.Submit([s.ImageReadySemaphore], [VkPipelineStageFlags.ColorAttachmentOutputBit], [cmd], [presentReady[imageIndex]], s.Fence);
        var waitSemaphore = presentReady[imageIndex];
        var presentIndex = imageIndex;
        queue.PresentKHR(new VkPresentInfoKHR
        {
            WaitSemaphoreCount = 1, WaitSemaphores = &waitSemaphore,
            SwapchainCount = 1, Swapchains = &swapchain, ImageIndices = &presentIndex,
        });
        if (frameNumber % 60 == 0) Console.WriteLine($"frame {frameNumber} image {imageIndex}");
    }

    // ---- 13. Teardown -----------------------------------------------------------------------
    device.WaitIdle();
    foreach (var s in sync) s.Dispose();
    device.DestroyCommandPool(commandPool, null);
    device.DestroyPipeline(pipeline, null);
    device.DestroyPipelineLayout(pipelineLayout, null);
    for (var i = 0; i < FramesInFlight; i++) { uniformMaps[i].Dispose(); uniformBuffers[i].Dispose(); }
    foreach (var v in swapViews) device.DestroyImageView(v, null);
    foreach (var p in presentReady) device.DestroySemaphore(p, null);
    device.DestroySwapchainKHR(swapchain, null);
    instance.DestroySurfaceKHR(surface, null);
}
window.Dispose();
Glfw.Terminate();
Console.WriteLine("hello mesh OK");

public struct Vertex(float3 pos, float3 color)
{
    public float3 Pos = pos;
    public float3 Color = color;
}
```

# Open questions

* Not tested: swapchain recreation on resize (`ErrorOutOfDateKHR` makes the generated wrapper throw `VkResultError`). KSA handles it in `Renderer.Rebuild`; see [/brutal/ksa-bootstrap-reference.md](/brutal/ksa-bootstrap-reference.md).[^ksa]
* Not tested on Windows or Linux. On Linux, `NativeLibrary.Load("vulkan")` may need an unversioned `libvulkan.so` (UNVERIFIED).

[^program]: hellomesh Program.cs
[^csproj]: hellomesh.csproj
[^shaders]: hellomesh GLSL/SPIR-V shaders
[^abstractions]: Abstractions catalogue
[^contract]: Loader / proc-addr contract
[^ksa]: KSA bootstrap reference
[^brutal-readme]: BRUTAL README
