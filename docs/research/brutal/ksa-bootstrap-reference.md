---
type: Code Analysis
title: How Kitten Space Agency bootstraps BRUTAL (reference only)
description: Step-by-step trace, with decompiled file/line references, of how the shipped KSA game (build 2026.10.10.5554, BRUTAL 2026.9.0) uses BRUTAL for window, instance, device, swapchain, frame loop, shaders, pipelines, meshes, glTF, audio and threading — and what that implies for a browser port.
tags: [brutal, ksa, reference, bootstrap, vulkan, glfw, fmod, shaderc, vma, threading]
sources:
  - id: ksa-report
    resource: /Users/asherwin/.claude/projects/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/tool-results/toolu_01V1TVt1Q2356b9vZnL8xhPt.txt
    title: Full sub-agent trace of the KSA decompile (55 KB, every step with file:line)
  - id: ksa-scan
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/ksa/
    title: Scan scripts and outputs (scan.py, report.txt, refine.txt, IL dumps)
  - id: program
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA/Program.cs
    title: KSA/Program.cs (Main, ctor order, OnFrame, RenderGame)
  - id: app
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA/App.cs
    title: KSA/App.cs (main loop)
  - id: devctx
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/Core/KSADeviceContextEx.cs
    title: Core/KSADeviceContextEx.cs (instance, physical device, device, VMA)
  - id: renderer
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/Core/Renderer.cs
    title: Core/Renderer.cs (surface, swapchain, frames, acquire/submit/present)
  - id: physext
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/Core/PhysicalDeviceExtensions.cs
    title: Core/PhysicalDeviceExtensions.cs (CreateDevice wrapper)
  - id: shadermod
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/RenderCore/ShaderModuleUtils.cs
    title: RenderCore/ShaderModuleUtils.cs (runtime GLSL→SPIR-V via shaderc)
  - id: pipelines
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/RenderCore.Pipelines/SimplePipelineCreator.cs
    title: RenderCore.Pipelines/SimplePipelineCreator.cs
  - id: rendertarget
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA.Rendering/RenderTarget.cs
    title: KSA.Rendering/RenderTarget.cs (dynamic rendering)
  - id: mesh
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/RenderCore.Mesh/SimpleVkMesh.cs
    title: RenderCore.Mesh/SimpleVkMesh.cs
  - id: vkutils
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/RenderCore/VkUtils.cs
    title: RenderCore/VkUtils.cs (StageAndUploadToBuffer)
  - id: gltfutils
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/RenderCore.Gltf/GltfUtils.cs
    title: RenderCore.Gltf/GltfUtils.cs
  - id: audio
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA/GameAudio.cs
    title: KSA/GameAudio.cs (FMOD Core)
  - id: jobs
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA/JobSystems.cs
    title: KSA/JobSystems.cs
  - id: vma
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA/KsaVmaAllocator.cs
    title: KSA/KsaVmaAllocator.cs
  - id: ksa-linux
    resource: /Users/asherwin/repos/meow-sci/ksa-linux/
    title: KSA Linux build (KSA.deps.json, runtimeconfig, bundled .so files, Content/)
  - id: version
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/version.json
    title: KSA version.json (build 2026.10.10.5554)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:16:59Z }
status: draft
---

> **Reference only.** The PoC must not link KSA binaries. This trace shows how a shipped game uses BRUTAL, which tells us which BRUTAL and Vulkan features a "full game" path eventually needs. Paths below use the prefix `{D}` = `/Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/`. The full trace with every line reference is the sub-agent report.[^ksa-report]

# Build identity

* Game build **2026.10.10.5554** (2026-10-07).[^version] Its BRUTAL assemblies are KSA's private split (`Brutal.Core.Common/.Memory/.Strings/.Logging/.Numerics/.Collections/.Maths/.Package`) at **2026.9.0**. Exceptions are Brutal.Concurrency 2026.10.1 and Brutal.Monitor.* 2026.9.1, per `ksa-linux/KSA.deps.json`.[^ksa-linux]
* The Linux build is **self-contained .NET 10.0.10**. It bundles `libglfw.so.3.5`, `libfmod*`/`libfmodstudio*` (FMOD 2.03.08), `libshaderc_shared` + `libSPIRV-Tools-shared`, `libvma.so`, `libVulkanEx.so`, `libgli`, `libKtx`, `libstb`, `libimgui`, `libimplot` and `libRakNetDLL`. It does **not** bundle a Vulkan loader (the system `libvulkan` is used through `NativeLibrary.Load("vulkan")`) or OpenAL. `Content/` ships **GLSL sources only** (vert 51, frag 68, comp 139, …, 0 `.spv` files).[^ksa-linux][^ksa-report]
* Code is split across `Planet.Core`, `Planet.Render.Core` (namespaces `Core`, `Render`, `RenderCore*`) and `KSA`.

# Bootstrap sequence

| Step | KSA code | BRUTAL APIs used |
|---|---|---|
| Entry and loop | `Main` (`{D}KSA/Program.cs:1693–1703`) → `new Program(args).Run()`; `KSA.App.Run()` is `while (!_exitCode.HasValue)` with Stopwatch timing, **no sleep** (pacing comes from FIFO present + `vkWaitForPresentKHR`), calling `OnFrame` (`{D}KSA/App.cs:39–74`)[^app][^program] | — |
| Logging | `LogSystem.Initialize(b => b.AddConsole(...).AddConsoleFormatter<KSALogFormatter,…>())` (`Program.cs:712–723`); `Glfw.Log.IsCategoryEnabled = false` (`:724`), which silences GLFW's per-call `Log.Trace()`[^program] | Brutal.Logging |
| Vulkan loader first | touches `Vulkan.GetInstanceProcAddr` before GLFW and throws if null (`Program.cs:725–728`) | `static Vulkan()` |
| Window | `Glfw.Init()` (`:747`); hints `ClientApi = 0`, `AutoIconify = 0`, `FocusOnShow = 1`, `Visible = !Platform.IsWindows` (`:765–768`); `Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title, Size = primary monitor video mode })` (`:769–773`); events `OnClose`, `OnKey`, `OnMouseButton`, `OnCursorPos`, `OnScroll`, `OnFocus` (`:787–792`); ImGui backend also uses `OnChar` and `OnCursorEnter`. **`OnFramebufferSize` is never subscribed**: resize is detected from swapchain results plus `glfwGetFramebufferSize`. `Glfw.PollEvents()` runs once per frame (`:2179`). `Glfw.Time` is used for UI timing; gamepad via `Glfw.GetGamepadState`; raw mouse via `SetInputMode(RawMouseMotion)`[^program] | Brutal.Glfw, `Brutal.Platform` |
| Instance | `KSADeviceContextEx` ctor (`{D}Core/KSADeviceContextEx.cs:103–125`): `ApiVersion = VERSION_1_4`, `ApplicationName = "KittenSpaceAgency"`. Extensions: `AddPlatformRequiredExtensions` + `AddSurfaceExtensions` + `Glfw.GetRequiredInstanceExtensions` + (Windows) `VK_KHR_get_surface_capabilities2`. **Empty layers, no debug messenger, no `VK_EXT_debug_utils`**. `Vulkan.CreateInstance(new Instance.CreateInfo{…})`[^devctx] | `Vulkan.CreateInstance`, `VulkanHelpers` |
| Surface | `Glfw.CreateWindowSurface(Instance.VkHandle, window, out var s)` (return value ignored); `new VkSurfaceKHR((nint)s)` (`{D}Core/Renderer.cs:148–149`)[^renderer] | Brutal.Glfw |
| Physical device | keep devices that support `VK_KHR_swapchain`; prefer `DiscreteGpu`; separate compute and transfer families if available (`KSADeviceContextEx.cs:126–176`). **No `GetSurfaceSupportKHR` for the main surface** (present = graphics). Queries: `GetProperties`, `GetFeatures`, `GetFeatures2` (full chain), `GetProperties2` (AS, descriptor-indexing limits), `GetMemoryProperties2` (+ memory budget)[^devctx] | `PhysicalDevice.*` |
| Device | `PhysicalDevice.CreateDevice(ref features2, queues, extensions)` → `DeviceEx.CreateInfo { Next = &VkPhysicalDeviceFeatures2 }` (`{D}Core/PhysicalDeviceExtensions.cs:10–21`; `KSADeviceContextEx.cs:390`). Extensions: `VK_KHR_swapchain`, `VK_KHR_multiview`, optional portability subset, present_id/wait, memory budget, ray tracing (AS, ray query, RT pipeline, BDA, …) and Windows FSE. Features chain: Features2 → DescriptorIndexing → **Vulkan13 (DynamicRendering, Synchronization2, Maintenance4, DemoteToHelper)** → ShaderDrawParameters → Multiview → BufferDeviceAddress → AS → RT → RayQuery. The chain is **overwritten by `GetFeatures2` before create**, so KSA enables every supported feature. All queue priorities are 0.0[^devctx][^physext] | `DeviceEx`, `Device.GetQueue` |
| Allocator | **VMA** via Brutal.Vulkan.Vma: `vmaCreateAllocator` with BDA and memory budget (`KSADeviceContextEx.cs:399–424`), wrapped as `KsaVmaAllocator : IBufferAllocator, IImageAllocator` (`{D}KSA/KsaVmaAllocator.cs`)[^vma] | `Brutal.VulkanApi.Vma`, Abstractions allocator interfaces |
| Swapchain | `Renderer.CreateSwapchain` (`Renderer.cs:670–785`): caps (or `GetSurfaceCapabilities2KHR` for FSE), formats and present modes; FIFO or IMMEDIATE (vsync setting); `B8G8R8A8UNorm`+`SRGBNonlinear`; 3 images; usage `TransferSrc\|ColorAttachment`; `device.CreateSwapchainKHR`; views wrapped in Brutal `FrameResources { OwnsColorImage = false }`; main composite **legacy render pass** (Undefined → PresentSrc). Rebuild on OutOfDate/Suboptimal[^renderer] | raw wrappers, `FrameResources` |
| Frames | `MaxFramesInFlight = 2`; Brutal `SyncObjects` per frame (+ a per-image present semaphore); `WaitForFences(-1)`, `ResetFences`, `AcquireNextImageKHR(-1)`; async compute submitted separately; record: offscreen passes with **dynamic rendering** (19 `BeginRendering` sites through `RenderTarget`), then composite + ImGui in the legacy render pass (6 sites); barriers: 110 legacy `PipelineBarrier` + `PipelineBarrier2` via `BarrierBatch`/`CommandBufferEx`; **submit is always `vkQueueSubmit` (v1)** via `QueueExtensions.Submit`; present with optional `VkPresentIdKHR` and `WaitForPresentKHR` throttling[^renderer][^rendertarget][^program] | `SyncObjects`, `QueueExtensions.Submit`, `Queue.PresentKHR` |
| Shaders | **runtime GLSL → SPIR-V with shaderc**: `ShaderC.CreateCompiler()`, target Vulkan 1.4 / SPIR-V 1.6, `CompileIntoSpv`, then `device.CreateShaderModule` (`{D}RenderCore/ShaderModuleUtils.cs:16–170`). ImGui shaders are embedded SPIR-V[^shadermod] | Brutal.ShaderC |
| Pipelines | `SimplePipelineCreator.CreatePipeline` uses Brutal `VertexInput`, `Presets.*` and `device.CreateGraphicsPipeline(PipelineCache, info, null)` (47 sites). Dynamic-rendering pipelines chain `VkPipelineRenderingCreateInfo` (38 refs). Dynamic states: Viewport, Scissor, DepthBias. Reverse-Z (`GreaterOrEqual`). Shared pipeline cache persisted to disk. `CreatePipelineLayout(spans)` for all 64 layouts; push constants ≤128 bytes[^pipelines] | Abstractions helpers |
| Descriptors | all pools via `DescriptorPoolEx` (50), all layouts via `DescriptorSetLayoutEx` (87), `AllocateDescriptorSet` (93), raw `UpdateDescriptorSets` (98), `BindDescriptorSets` (107 + 41 helper). **Bindless** texture library (update-after-bind, partially bound, 1024 sampled images)[^ksa-report] | Abstractions |
| Meshes | `SimpleVkMesh` creates device-local `BufferEx` on **`DeviceEx` by default** (one `vkAllocateMemory` per buffer), uploads via Brutal `StagingPool` + `MappedMemory` + `cmd.CopyBuffer` (`{D}RenderCore/VkUtils.cs:82–99`); indices UInt32 (UInt16 when stride 2). Most other buffers (about 155) go through VMA. 11 `GetBufferDeviceAddress` sites[^mesh][^vkutils] | `BufferEx`, `StagingPool`, `MappedMemory` |
| glTF | `new GltfLoader(path)` in 9 places; `loader.GetAccessorBufferSpan(i)` for POSITION/NORMAL/TANGENT/TEXCOORD_0/1/COLOR_0/WEIGHTS_0/JOINTS_0; indices widened to uint32; embedded textures via `GetOrLoadImage` → Stb; other textures KTX2 (`{D}RenderCore.Gltf/GltfUtils.cs`)[^gltfutils] | Brutal.Gltf, Brutal.Texture |
| Audio | **FMOD Core only**: `Fmod.SystemCreate`, `System.Init(1024, _3dRightHanded)`, custom file system, per-frame `System.Update()`. **No OpenAL, no FMOD Studio** (`{D}KSA/GameAudio.cs:104–152`)[^audio] | Brutal.Fmod |
| Threading | all rendering on the main thread. Brutal `JobScheduler`s (OrbitSolvers, VehicleSolver, ClothSolvers, …) run on the ThreadPool; `JobSystems.WaitAll()` waits on them. KSA's own `DynamicWorkerPool` creates threads. Separate "TextureStreamingUploader" thread submits to queues (`{D}KSA/JobSystems.cs:32–67`)[^jobs] | Brutal.Concurrency |

# Vulkan feature usage summary

| Feature | KSA | Browser (WebGPU) implication |
|---|---|---|
| Dynamic rendering | yes (all offscreen passes) | maps naturally to `beginRenderPass` |
| Legacy render pass + framebuffer | yes (swapchain composite + ImGui) | also maps (render-pass begin = WebGPU pass) |
| Synchronization2 barriers | yes (`BarrierBatch`) | barriers are no-ops or layout bookkeeping in WebGPU |
| `vkQueueSubmit2` | **no** | — |
| Timeline semaphores | no | — |
| Descriptor indexing / bindless (update-after-bind, 1024 textures) | yes | WebGPU has no bindless; texture arrays are limited; a large gap for a full KSA-class game |
| Buffer device address | yes (11 sites + ray tracing) | **no WebGPU equivalent** |
| Ray tracing (AS, ray query, RT pipeline) | optional | none |
| Mesh shading | no | — |
| Multiview | enabled | not needed for the PoC |
| Geometry/tessellation shaders, wide lines, fill-mode non-solid | enabled as core features (some content uses tess: 2 tesc/tese shaders) | not in WebGPU |
| Push constants | yes (≤128 B) | emulate |
| Pipeline cache on disk | yes | no-op |
| VMA | yes (most buffers) | needs a C# VMA facade, or route through `DeviceEx` |
| Runtime GLSL compile (shaderc) | yes (all game shaders) | no native shaderc in browser; need a wasm compiler or offline compile |

# Direct Vulkan surface used by KSA (counts of call sites)

Instance/physical device: CreateInstance 1, SupportsExtension 13, GetProperties 13, GetProperties2 4, GetFeatures 3, GetFeatures2 1, GetMemoryProperties(2) 5, queue-family queries, CreateDevice 1, GetQueue 4, GetProcAddr 2. Surface/swapchain: CreateWindowSurface 2, GetSurface{Support,Capabilities(2),Formats,PresentModes}, CreateSwapchainKHR 2, AcquireNextImageKHR 2, PresentKHR 2, WaitForPresentKHR 1. Resources: BufferEx 161, raw CreateBuffer 3, CreateImageView 21, CreateSampler 55, CreateShaderModule 3, CreatePipelineLayout 64, CreateGraphicsPipeline(s) 48, CreateComputePipelines 5, RT pipelines 1, CreateRenderPass 2, CreateFramebuffer 2, DescriptorPoolEx 50, DescriptorSetLayoutEx 87, AllocateDescriptorSet 93, UpdateDescriptorSets 98, CreateCommandPool 8, AllocateCommandBuffer(s) 28, CreateQueryPool 3, acceleration-structure calls 11, DeviceWaitIdle 25. Commands: Begin 76, End 77, BeginRenderPass 6, **BeginRendering 19**, BindPipeline 98, BindDescriptorSets 107 (+41 helpers, 1 direct `PInvoke`), PushConstants 65, BindVertexBuffer(s) 38, BindIndexBuffer 22, Draw 19, DrawIndexed 12, DrawIndirect 5, DrawIndexedIndirect 27, **Dispatch 134**, DispatchIndirect 11, PipelineBarrier 110, PipelineBarrier2 4 (+39 helpers), CopyBuffer 37, CopyBufferToImage 7, CopyImage 5, CopyImageToBuffer 4, BlitImage 2, FillBuffer 27, ClearColorImage 13, SetViewport 8, SetScissor 11, SetDepthBias 4, WriteTimestamp 5, BuildAccelerationStructures 5, TraceRays 1. Sync: CreateFence 26, WaitForFence(s) 30, CreateSemaphore 9, QueueExtensions.Submit 26, StagingPool.Submit 11, Queue.Submit 2.[^ksa-report][^ksa-scan]

# Differences between KSA's BRUTAL and the open-source tree

* `Instance`, `Device`, `PhysicalDevice`, `Queue`, `StagingPool`, the Vulkan loader and the GLFW native loader are **logically identical** in both.
* **`DeviceEx` is newer in the open tree.** The open tree adds `MemoryPreference` → preferred flags, `AllocateFlags` (`VkMemoryAllocateFlagsInfo`) and `SetDebugName`. KSA's `DeviceEx` implements `IImageAllocator, IAllocator, IBufferAllocator` directly.
* Generated wrappers: the open tree wraps 770 distinct vk functions and KSA 703. All 67 extras are newer extensions that KSA does not use.
* Both builds request the `vx*` batch-destroy functions through `vkGetDeviceProcAddr`; KSA never calls them.
* Packages in the tree that KSA does not ship: OpenAl, Slang, Sdl*, Jolt, Box2D, MeshOptimizer, RenderDoc, ImGui.Backends (KSA has its own ImGui GLFW and Vulkan backends), FreeType, NotCurses, NativeFileDialog, OpenGl, ZLib.[^ksa-report]

# What this means for the browser plan

1. The PoC subset (window, instance, device, swapchain, dynamic rendering, `BufferEx`/`StagingPool` mesh upload, UBO descriptors, an indexed draw) is exactly the core of KSA's path. [/brutal/desktop-hello-mesh-recipe.md](/brutal/desktop-hello-mesh-recipe.md) exercises it.
2. A "full game" path at KSA scale additionally needs bindless descriptors, buffer device address, compute (134 dispatch sites), runtime GLSL compilation, VMA, FMOD, and a working job system on threads. Most of these are hard or impossible on WebGPU or single-threaded wasm, which is useful input for scoping in `/decisions/`.
3. KSA's host logging (`AddConsole`) and `JobSystems.WaitAll` would fail or hang on single-threaded browser-wasm; see [/brutal/managed-libraries-portability-audit.md](/brutal/managed-libraries-portability-audit.md).
4. KSA submits from a background "TextureStreamingUploader" thread as well as the main thread. In the open tree every `Queue.Submit` path (`QueueExtensions.Submit`, `StagingPool.Submit`) ends in the `Lock`-guarded `Queue.Submit`, so submissions are serialised on desktop. Whether KSA's 2026.9 build has the same lock is UNVERIFIED. On single-threaded browser-wasm such a thread cannot exist at all, so the uploader must become a per-frame task.

# Open questions

* UNVERIFIED: whether KSA's feature-chain practice (RT/AS feature structs chained without their extensions) works on all drivers. Irrelevant to the PoC.
* UNVERIFIED on a real Linux box: whether `NativeLibrary.Load("vulkan")` finds `libvulkan.so.1` without an unversioned `libvulkan.so` symlink.

[^ksa-report]: Full KSA bootstrap trace (sub-agent report)
[^ksa-scan]: KSA scan scripts and outputs
[^program]: KSA/Program.cs
[^app]: KSA/App.cs
[^devctx]: Core/KSADeviceContextEx.cs
[^renderer]: Core/Renderer.cs
[^physext]: Core/PhysicalDeviceExtensions.cs
[^shadermod]: RenderCore/ShaderModuleUtils.cs
[^pipelines]: RenderCore.Pipelines/SimplePipelineCreator.cs
[^rendertarget]: KSA.Rendering/RenderTarget.cs
[^mesh]: RenderCore.Mesh/SimpleVkMesh.cs
[^vkutils]: RenderCore/VkUtils.cs
[^gltfutils]: RenderCore.Gltf/GltfUtils.cs
[^audio]: KSA/GameAudio.cs
[^jobs]: KSA/JobSystems.cs
[^vma]: KSA/KsaVmaAllocator.cs
[^ksa-linux]: KSA Linux build
[^version]: KSA version.json
