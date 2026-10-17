---
type: Research Note
title: Memory model and mapping emulation — VkDeviceMemory, host-visible memory and readback on WebGPU
description: How a Vulkan-on-WebGPU ICD emulates VkDeviceMemory, memory types, persistent and coherent mapping, flush/invalidate and GPU→CPU readback using wasm-heap shadow copies, queue.writeBuffer and mapAsync, with the heap/type layout to advertise, the hazards (aliasing, GPU-written host-visible buffers, wasm heap budget) and costs, grounded in what BRUTAL's DeviceEx and StagingPool actually do.
tags: [vulkan, webgpu, memory, mapping, shadow-memory, readback, wasm, brutal]
sources:
  - id: webgpu-spec
    resource: https://gpuweb.github.io/gpuweb/
    title: WebGPU W3C Editor's Draft, 8 October 2026 (§5 Buffers, §5.2 Buffer Mapping, writeBuffer)
  - id: vk-memory
    resource: https://docs.vulkan.org/spec/latest/chapters/memory.html
    title: Vulkan specification, Memory Allocation chapter
  - id: vk-sync
    resource: https://docs.vulkan.org/spec/latest/chapters/synchronization.html
    title: Vulkan specification, Synchronization chapter (Host Write Ordering Guarantees)
  - id: brutal-deviceex
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/Classes/DeviceEx.cs
    title: Brutal.Vulkan.Abstractions DeviceEx.cs (per-resource allocation, ref-counted mapping)
  - id: brutal-stagingpool
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/StagingPool.cs
    title: Brutal.Vulkan.Abstractions StagingPool.cs (HOST_VISIBLE|HOST_COHERENT staging, fence waits)
  - id: brutal-physdev
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Manual/Classes/PhysicalDevice.cs
    title: Brutal.Vulkan PhysicalDevice.FindMemoryType (required + preferred flags)
  - id: toji-uploads
    resource: https://toji.dev/webgpu-best-practices/buffer-uploads
    title: Brandon Jones, WebGPU best practices — buffer uploads
  - id: moltenvk-guide
    resource: https://github.com/KhronosGroup/MoltenVK/blob/main/Docs/MoltenVK_Runtime_UserGuide.md
    title: MoltenVK Runtime User Guide (memory types Private/Shared/Managed)
  - id: vkwebgpu-icd
    resource: https://github.com/Dankular/VkWebGPU-ICD
    title: Dankular/VkWebGPU-ICD (host-visible memory via staging, coherent flush at submit)
  - id: chrome-145
    resource: https://developer.chrome.com/blog/new-in-webgpu-145
    title: What's New in WebGPU (Chrome 145) — experimental GPUBuffer.mapSync() in workers
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:13:57Z }
status: draft
---

# The mismatch

| Vulkan | WebGPU |
| --- | --- |
| `VkDeviceMemory` objects, sub-allocated by offset; resources bound later; aliasing allowed | no memory objects; each `GPUBuffer`/`GPUTexture` owns its storage; no aliasing[^webgpu-spec] |
| Memory types with `DEVICE_LOCAL`, `HOST_VISIBLE`, `HOST_COHERENT`, `HOST_CACHED` | none; host access only through mapping or `queue.writeBuffer/writeTexture`[^webgpu-spec] |
| `vkMapMemory` returns a stable host pointer; may stay mapped while the GPU uses it (persistent mapping) | `mapAsync` is asynchronous; a mapped buffer **cannot be used by the GPU** and must be unmapped before submit; `MAP_READ` only combinable with `COPY_DST`, `MAP_WRITE` only with `COPY_SRC`[^webgpu-spec] |
| Coherent memory: host writes visible to the device at the next `vkQueueSubmit` without a flush[^vk-sync] | n/a |
| A mapped pointer is ordinary process memory | a mapped range is a JS `ArrayBuffer` **outside** the wasm linear memory — .NET code cannot hold a pointer to it at all |

The last row decides the design: even a perfect WebGPU mapping could not be handed to C# as a `void*`. The only pointer a C# app can write through is wasm linear memory, so **host-visible Vulkan memory must be a shadow allocation in the wasm heap**, and every host→device transfer is a copy from that heap (`queue.writeBuffer(buffer, offset, HEAPU8, ptr, size)` — one copy, the cheapest path for wasm apps according to WebGPU best-practice guidance).[^toji-uploads]

# Heaps and types to advertise

| Index | Property flags | Heap | Used by | Backing |
| --- | --- | --- | --- | --- |
| type 0 | `DEVICE_LOCAL` | heap 0 (`DEVICE_LOCAL`) | images, static vertex/index/storage buffers | WebGPU resources only |
| type 1 | `HOST_VISIBLE \| HOST_COHERENT` | heap 1 (host) | staging, per-frame uploads | wasm-heap shadow + `writeBuffer` |
| type 2 | `HOST_VISIBLE \| HOST_COHERENT \| HOST_CACHED` | heap 1 | readback | wasm-heap shadow + async readback |
| type 3 (optional) | `DEVICE_LOCAL \| HOST_VISIBLE \| HOST_COHERENT` | heap 0 | uniform rings, "ReBAR-style" dynamic buffers | same as type 1 |

- Ordering satisfies Vulkan's rule that a type whose flags are a strict subset of another's comes first, and the mandatory "one `HOST_VISIBLE|HOST_COHERENT` type" and "one `DEVICE_LOCAL` type" exist.[^vk-memory]
- `memoryTypeBits`: buffers → all four types; images → type 0 only (no linear images).
- Heap sizes are fiction (browsers expose no VRAM size): heap 0 e.g. 2 GiB, heap 1 = the wasm heap budget the app is allowed to shadow (e.g. 512 MiB). Report `VK_EXT_memory_budget` as unsupported.
- No non-coherent host-visible type: BRUTAL's `StagingPool` *requires* `HOST_VISIBLE|HOST_COHERENT`,[^brutal-stagingpool] and coherency is what makes "upload at submit" well defined. (MoltenVK made the same choice for its Shared type; its non-coherent Managed type exists only for discrete Macs.)[^moltenvk-guide]
- BRUTAL's `PhysicalDevice.FindMemoryType(filter, required, preferred)` picks the first type matching all required flags and the most preferred flags,[^brutal-physdev] so `PreferGpu` → type 0 (or 3 when host-visible is required), `PreferCpu` → type 1.

# Object lifecycle

| Vulkan call | ICD action | Cost |
| --- | --- | --- |
| `vkAllocateMemory(type 0)` | CPU record only | ~0 |
| `vkAllocateMemory(type 1/2/3)` | CPU record + `NativeMemory.AlignedAlloc(size, 64)` zero-filled shadow | wasm heap = allocation size |
| `vkCreateBuffer` | `device.createBuffer({size: align4(size), usage: translated \| COPY_DST \| COPY_SRC})` | one JS call |
| `vkBindBufferMemory(buf, mem, off)` | add (off, size, buf) to the memory's interval list | ~0 |
| `vkCreateImage` / `vkBindImageMemory` | `createTexture` at create; bind is bookkeeping | one JS call |
| `vkMapMemory(mem, off, size)` | return `shadow + off` (mapping never fails for host-visible types; persistent mapping free) | ~0 |
| host writes through the pointer | nothing (no page protection in wasm) | — |
| `vkFlushMappedMemoryRanges` | for each bound buffer overlapping the range: `writeBuffer` of the overlap (4-byte padded) | one JS call per range, bandwidth = range |
| `vkUnmapMemory` | same as flushing the whole mapped range (catches BRUTAL's map-write-unmap staging pattern) | bandwidth = mapped range |
| `vkQueueSubmit` | **coherency flush** (below) | see below |
| `vkInvalidateMappedMemoryRanges` | copy the latest completed readback snapshot into the shadow | memcpy |
| `vkFreeMemory` | free shadow; buffers keep working until destroyed | ~0 |

BRUTAL's `DeviceEx` creates **one `VkDeviceMemory` per buffer/image at offset 0** and maps the whole allocation with a reference count,[^brutal-deviceex] so the interval list always has one entry — the simplest possible case. Sub-allocation inside a `VkBuffer` (BRUTAL's buffer partitioners) needs nothing special because it stays within one `GPUBuffer`.

# Coherency flush at submit (host → device)

Vulkan guarantees that host writes to coherent memory made before `vkQueueSubmit` are visible to the submitted work.[^vk-sync] Without write tracking the ICD must decide what to upload:

| Strategy | What is uploaded at submit | Memory | CPU/bandwidth | Verdict |
| --- | --- | --- | --- | --- |
| S1 whole mapped | every currently-mapped coherent range bound to a buffer | 1× | proportional to all mapped memory every submit | simple, wasteful for big persistent maps |
| S2 referenced-only | mapped ranges of buffers **referenced by this submission** (from the command stream: vertex/index/indirect binds, copies, and buffers inside bound descriptor sets) that were mapped or written since their last upload | 1× | proportional to buffers in use | **recommended default** |
| S3 diffed | S2, but compare against a last-uploaded snapshot in 4 KiB pages and upload only changed runs | 2× | memcmp + only changed bytes | add for large persistent rings if profiling shows waste |
| S4 explicit | only explicit `Flush`/unmap ranges (treat memory as if non-coherent) | 1× | minimal | violates `HOST_COHERENT`; acceptable as an opt-in "web profile" convention |

VkWebGPU-ICD flushes coherent memory at `vkQueueSubmit`, i.e. S1/S2-like.[^vkwebgpu-icd] `writeBuffer` copies the bytes at call time, so **the shadow may be overwritten immediately after `vkQueueSubmit` returns** — this is what lets upload-only fences be signalled early (see [sync](/webgpu/synchronization-and-async-shape.md)).

# GPU-written host-visible memory (device → host)

Two hazards appear as soon as the GPU writes a host-visible buffer (copy destination, storage write, query resolve):

1. **Clobbering**: a later coherency flush would overwrite GPU-produced data with the stale shadow. Track a `gpuWritten` flag per buffer (set during replay when the buffer is a copy destination or a writable storage binding) and exclude such buffers from implicit uploads until the host explicitly flushes a range.
2. **Readback**: after replaying a submission that writes a host-visible buffer, append `copyBufferToBuffer(buf → readbackStaging)` (staging = `MAP_READ | COPY_DST`, pooled), submit, then `staging.mapAsync(GPUMapMode.READ)`; when the promise resolves, copy `getMappedRange()` into the shadow, `unmap()`, return the staging buffer to the pool, and mark the associated fence signalled.

Costs: one GPU copy + one JS→wasm memcpy; latency ≥ the GPU completing the submission plus one JS task, i.e. **results arrive on a later frame**. Patterns that `vkWaitForFences` with an infinite timeout right after a readback submit cannot work on the main thread (see [sync](/webgpu/synchronization-and-async-shape.md)). Chrome 145 added an experimental `GPUBuffer.mapSync()` for workers (behind `--enable-features=WebGPUMapSyncOnWorkers`), which would make blocking readback possible in a worker-hosted renderer if it ships.[^chrome-145]

# Things that cannot be emulated faithfully

| Vulkan behaviour | Why | Profile rule |
| --- | --- | --- |
| Aliasing: two resources bound to overlapping memory (render-graph transient aliasing, VMA aliasing) | each WebGPU resource has private storage | forbid overlapping binds (ICD detects via interval list and errors); memory savings from aliasing are lost |
| Reinterpreting buffer memory as an image (or vice versa) by binding both to the same memory | same | forbid |
| `vkGetBufferDeviceAddress`, pointers in shaders | WGSL has no physical pointers | forbid; emulate with "one big storage buffer + 32-bit offsets" in app code (WGSL `buffer_view` helps reinterpretation inside one binding) |
| Linear-tiled host-visible images | no host access to textures | forbid (`linearTilingFeatures = 0`) |
| `HOST_VISIBLE` memory read by the GPU while the CPU keeps writing (true zero-copy streaming) | every write is an explicit copy | allowed, but each submit costs an upload |
| Sparse binding/residency | none | forbid |
| `VK_EXT_external_memory_host`, `memory_budget`, `memory_priority` | none | not advertised |

# Budget and sizing guidance for BRUTAL apps

- GPU memory use ≈ Σ resource sizes (not Σ `VkDeviceMemory` sizes): over-allocating device-local blocks costs nothing on WebGPU.
- wasm-heap use ≈ Σ host-visible allocation sizes (×2 with S3). With wasm32 the whole .NET heap, shadows included, must fit in ≤ 4 GiB and practically much less on mobile — keep host-visible memory to staging rings and per-frame uniforms.
- Prefer device-local buffers filled through staging copies (or `writeBuffer`-style uploads) over large persistently mapped host-visible buffers.
- Uniform/storage offsets and dynamic offsets: 256-byte aligned; buffer sizes rounded to 4 bytes; `writeBuffer` offsets/sizes multiples of 4.[^webgpu-spec]

# Open questions

- Resolved by the spec IDL: `writeBuffer(buffer, bufferOffset, AllowSharedBufferSource data, dataOffset, size)` accepts views over a `SharedArrayBuffer`, so a multithreaded .NET heap can be passed directly (browser conformance UNVERIFIED in practice).[^webgpu-spec]
- Measure S2 vs S3 on a realistic BRUTAL frame (ImGui + one mesh) before adding diffing.
- Whether Chrome's `mapSync()` in workers will ship; it changes the readback story for a worker-hosted renderer.

[^webgpu-spec]: WebGPU W3C Editor's Draft, 8 October 2026 (§5 Buffers, §5.2 Buffer Mapping, writeBuffer)
[^vk-memory]: Vulkan specification, Memory Allocation chapter
[^vk-sync]: Vulkan specification, Synchronization chapter (Host Write Ordering Guarantees)
[^brutal-deviceex]: Brutal.Vulkan.Abstractions DeviceEx.cs (per-resource allocation, ref-counted mapping)
[^brutal-stagingpool]: Brutal.Vulkan.Abstractions StagingPool.cs (HOST_VISIBLE|HOST_COHERENT staging, fence waits)
[^brutal-physdev]: Brutal.Vulkan PhysicalDevice.FindMemoryType (required + preferred flags)
[^toji-uploads]: Brandon Jones, WebGPU best practices — buffer uploads
[^moltenvk-guide]: MoltenVK Runtime User Guide (memory types Private/Shared/Managed)
[^vkwebgpu-icd]: Dankular/VkWebGPU-ICD (host-visible memory via staging, coherent flush at submit)
[^chrome-145]: What's New in WebGPU (Chrome 145) — experimental GPUBuffer.mapSync() in workers
