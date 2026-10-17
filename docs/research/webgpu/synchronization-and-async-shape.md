---
type: Research Note
title: Synchronization and the async API shape — fences, semaphores, waits and the browser frame loop
description: Which WebGPU operations are asynchronous, how a synchronous Vulkan façade handles each (pre-initialized device, early-signalled fences, frame-lagged readback, non-blocking waits), how binary/timeline semaphores, events and pipeline compilation map, and what the single-threaded requestAnimationFrame loop means for BRUTAL-style render loops.
tags: [vulkan, webgpu, synchronization, fences, async, promises, game-loop, wasm]
sources:
  - id: webgpu-spec
    resource: https://gpuweb.github.io/gpuweb/
    title: WebGPU W3C Editor's Draft, 8 October 2026 (§3.2.2 Promise Ordering, §3.4.4 Synchronization, §21 Canvas)
  - id: vk-sync
    resource: https://docs.vulkan.org/spec/latest/chapters/synchronization.html
    title: Vulkan specification, Synchronization chapter
  - id: brutal-stagingpool
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/StagingPool.cs
    title: Brutal.Vulkan.Abstractions StagingPool.cs (WaitForFences with infinite timeout)
  - id: brutal-syncobjects
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/SyncObjects.cs
    title: Brutal.Vulkan.Abstractions SyncObjects.cs (per-frame semaphores + fence)
  - id: brutal-imgui-vk
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ImGui.Backends/RendererBackends/VulkanBackend.Helpers.cs
    title: BRUTAL ImGui Vulkan backend helpers (DeviceWaitIdle, one-shot fence waits)
  - id: moltenvk-config
    resource: https://github.com/KhronosGroup/MoltenVK/blob/main/Docs/MoltenVK_Configuration_Parameters.md
    title: MoltenVK configuration parameters (MVK_CONFIG_VK_SEMAPHORE_SUPPORT_STYLE)
  - id: vkwebgpu-icd
    resource: https://github.com/Dankular/VkWebGPU-ICD
    title: Dankular/VkWebGPU-ICD (no-op barriers, CPU fences, ordering-based semaphores)
  - id: nikki
    resource: https://github.com/nihui/nikki
    title: nihui/nikki (asynchronous replay and readback processing on Dawn)
  - id: chrome-145
    resource: https://developer.chrome.com/blog/new-in-webgpu-145
    title: What's New in WebGPU (Chrome 145) — experimental mapSync() in workers
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:13:57Z }
status: draft
---

# Which WebGPU operations are asynchronous

| WebGPU operation | Async? | Vulkan counterpart | Façade strategy |
| --- | --- | --- | --- |
| `navigator.gpu.requestAdapter()` | promise | `vkEnumeratePhysicalDevices` | **pre-initialize** in the JS bootstrap (or in an `async Task Main` before any Vulkan call) and store the adapter in the handle table |
| `adapter.requestDevice()` | promise | `vkCreateDevice` | pre-create with all useful features/limits; `vkCreateDevice` only validates |
| `buffer.mapAsync()` | promise | `vkMapMemory` (sync) | never used for app mapping (shadow memory); used internally for readback, completed on a later task |
| `queue.onSubmittedWorkDone()` | promise | fence/semaphore completion | optional precise fence tracking; ordered: earlier calls settle first, and a `mapAsync` issued before it settles before it[^webgpu-spec] |
| `createRenderPipelineAsync()` / `createComputePipelineAsync()` | promise | `vkCreate*Pipelines` (sync) | default to the **synchronous** `createRenderPipeline()` (returns a usable handle at once; compilation may stall first use); async only for pre-warming |
| `shaderModule.getCompilationInfo()` | promise | `vkCreateShaderModule` result | debug only; WGSL should be validated at build time |
| `device.popErrorScope()` / `uncapturederror` | promise / event | `VkResult` + debug messenger | errors reported after the fact through the messenger; Vulkan calls return `VK_SUCCESS` |
| `device.lost` | promise | `VK_ERROR_DEVICE_LOST` | latch a flag; later calls return the error |
| canvas presentation | implicit, at "update the rendering"[^webgpu-spec] | `vkQueuePresentKHR` | happens only after the JS task (the C# frame) returns |
| everything else (`createBuffer/Texture/Sampler/BindGroup*`, `createRenderPipeline`, encoders, `queue.submit`, `writeBuffer`, `writeTexture`, `getCurrentTexture`, `configure`) | synchronous from the caller's view | — | direct |

Promise ordering is otherwise unspecified: apps (and the ICD) must not rely on any other settlement order.[^webgpu-spec]

# The core constraint: nothing can block

On the browser main thread (and in a worker that owns the WebGPU device) a promise only resolves after the current task returns to the event loop. A C# call that spins waiting for GPU completion therefore **deadlocks the tab**: the completion it waits for can never be delivered. Consequences:

- `vkWaitForFences`, `vkWaitSemaphores`, `vkQueueWaitIdle`, `vkDeviceWaitIdle`, `vkGetQueryPoolResults(WAIT_BIT)` must return without waiting.
- Any data flowing GPU → CPU arrives on a **later frame**.
- Experimental relief exists only for workers: Chrome 145's `GPUBuffer.mapSync()` behind `--enable-features=WebGPUMapSyncOnWorkers`.[^chrome-145] (JSPI-style stack switching for .NET is a `/dotnet-wasm/` question; UNVERIFIED that .NET supports it.)

# Fences: "signalled at submit" vs "signalled on completion"

Key observation: the ICD copies all host data into WebGPU **at submit time** (`writeBuffer` copies immediately, command buffers are re-encoded from the ICD's own stream), and WebGPU executes later submissions after earlier ones. Therefore, from the CPU's point of view, a submission that does not *produce data for the CPU* is "done with host memory" the moment `vkQueueSubmit` returns.

| Fence class | Signalled when | Correct for | Wrong for |
| --- | --- | --- | --- |
| Upload/render-only submission (no host-visible writes by the GPU, no query results) | **immediately at `vkQueueSubmit`** | reusing staging buffers (BRUTAL `StagingPool.WaitFor`), rewriting per-frame uniform memory, re-recording/resetting command buffers, destroying resources, frames-in-flight loops | GPU timing / throttling (the app no longer sees GPU latency) |
| Submission with readback (GPU writes host-visible memory, `vkCmdCopyQueryPoolResults` to host-visible, occlusion/timestamp results) | when every readback `mapAsync` has resolved and the shadow is updated | CPU reading results | — (latency ≥ 1 frame) |
| Optional precise mode | `queue.onSubmittedWorkDone()` resolution | GPU-latency-aware throttling, profiling | adds one promise per submit |

Return values:

| Call | Fence already signalled | Fence pending (readback) |
| --- | --- | --- |
| `vkGetFenceStatus` | `VK_SUCCESS` | `VK_NOT_READY` |
| `vkWaitForFences(timeout = 0)` | `VK_SUCCESS` | `VK_TIMEOUT` |
| `vkWaitForFences(timeout > 0, incl. UINT64_MAX)` | `VK_SUCCESS` | `VK_TIMEOUT` immediately + debug-messenger warning (blocking is impossible; returning `VK_SUCCESS` would hand the app stale data). **Profile rule: never wait on a readback fence with a non-zero timeout; poll across frames.** |
| `vkResetFences` | back to unsignalled | allowed only when not pending |

BRUTAL implications: `StagingPool.Submit()` + `Wait()` (infinite timeout) only carries uploads, so it completes instantly under this scheme;[^brutal-stagingpool] `SyncObjects` (image-ready semaphore, render-finished semaphore, per-frame fence) maps to no-op semaphores and a fence signalled at submit;[^brutal-syncobjects] the ImGui backend's `DeviceWaitIdle` on swapchain recreation and its one-shot font-upload fence waits also complete immediately.[^brutal-imgui-vk]

# Semaphores and events

| Object | Mapping | Notes |
| --- | --- | --- |
| Binary semaphore (wait/signal in submit, acquire, present) | no-op | one queue + total order already gives the guarantee; MoltenVK's "single queue, implicit ordering" semaphore style is the same idea;[^moltenvk-config] VkWebGPU-ICD relies on in-order submissions as well.[^vkwebgpu-icd] |
| Timeline semaphore | CPU counter; signal values applied at submit (or on readback completion for readback submissions); `vkSignalSemaphore` sets it from the host | **wait-before-signal**: a submit that waits on a value not yet reached is parked in an ICD queue and replayed when the host signals it — implementable because everything is CPU-side. `vkWaitSemaphores` behaves like fence waits (no blocking). |
| `VkEvent` | CPU flag; `vkCmdSetEvent`/`ResetEvent` update it at submit; `vkCmdWaitEvents` no-op | host-set events waited on by the GPU work trivially (the GPU work is replayed after the host already set them, or parked like timeline waits). |
| Pipeline barriers | no-op | WebGPU inserts barriers from usage scopes; image layouts don't exist.[^webgpu-spec][^vk-sync] |

# Pipeline compilation without stalls

`vkCreateGraphicsPipelines` is synchronous and Vulkan apps expect compilation to be done when it returns. WebGPU's synchronous `createRenderPipeline()` returns immediately but may stall the first draw. A Vulkan-native escape hatch exists: when the app passes `VK_PIPELINE_CREATE_FAIL_ON_PIPELINE_COMPILE_REQUIRED_BIT` (`pipelineCreationCacheControl`, core 1.3), the ICD can start `createRenderPipelineAsync()` and return `VK_PIPELINE_COMPILE_REQUIRED` until the promise has resolved, letting the app keep drawing with a fallback. Pipeline *variants* created lazily for dynamic state are a separate stall source — pre-warm them at load time.

# The frame loop

A desktop BRUTAL app owns its loop (`while (!window.ShouldClose) { poll; update; render; present; }`). In the browser:

1. The JS bootstrap acquires adapter + device, configures nothing else, starts .NET.
2. C# initialization (instance, device, swapchain, pipelines, uploads) runs synchronously — all of it is legal because nothing waits on the GPU.
3. JS drives `requestAnimationFrame(t => exports.Frame(t))`; the C# `Frame` does input → update → `vkAcquireNextImageKHR` (gets `getCurrentTexture()`) → record → `vkQueueSubmit` (one JS replay call) → `vkQueuePresentKHR` (marker) → **returns**. The browser presents after the callback returns.[^webgpu-spec]
4. Async completions (readbacks, device loss, errors) are delivered between frames and become visible to C# at the next `Frame` (fence status flips, shadow memory updated).
5. Frames-in-flight: the browser composits at most one new canvas image per rAF; additional CPU-side throttling is optional (precise-fence mode can cap GPU queue depth at 2 frames).

Running the renderer in a dedicated worker with `OffscreenCanvas` keeps the same shape (worker rAF) and frees the main thread for input/DOM; it is required if the .NET runtime itself runs on a worker (see `/dotnet-wasm/`). Prior art confirms the approach: nikki processes submissions and readbacks asynchronously on Dawn rather than blocking.[^nikki]

# Open questions

- UNVERIFIED: whether .NET 10 browser-wasm offers any stack-switching (JSPI) integration that would allow a C# call to await a promise synchronously; if yes, blocking `vkWaitForFences` on readback fences becomes possible at a cost.
- UNVERIFIED: whether `requestAnimationFrame` is available in dedicated workers in Safari 26/27 (needed for a worker-hosted render loop).
- Decide whether "signal at submit" should be the default or opt-in (it changes observable timing for apps that measure GPU time with fences).

[^webgpu-spec]: WebGPU W3C Editor's Draft, 8 October 2026 (§3.2.2 Promise Ordering, §3.4.4 Synchronization, §21 Canvas)
[^vk-sync]: Vulkan specification, Synchronization chapter
[^brutal-stagingpool]: Brutal.Vulkan.Abstractions StagingPool.cs (WaitForFences with infinite timeout)
[^brutal-syncobjects]: Brutal.Vulkan.Abstractions SyncObjects.cs (per-frame semaphores + fence)
[^brutal-imgui-vk]: BRUTAL ImGui Vulkan backend helpers (DeviceWaitIdle, one-shot fence waits)
[^moltenvk-config]: MoltenVK configuration parameters (MVK_CONFIG_VK_SEMAPHORE_SUPPORT_STYLE)
[^vkwebgpu-icd]: Dankular/VkWebGPU-ICD (no-op barriers, CPU fences, ordering-based semaphores)
[^nikki]: nihui/nikki (asynchronous replay and readback processing on Dawn)
[^chrome-145]: What's New in WebGPU (Chrome 145) — experimental mapSync() in workers
