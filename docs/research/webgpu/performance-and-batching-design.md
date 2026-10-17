---
type: Proposal
title: Performance and batching design for a C# Vulkan-on-WebGPU ICD
description: Cost model of crossing the .NET-wasm/JS boundary, a command-stream design that records vkCmd* and even object creation into wasm memory and replays them in one JS call per vkQueueSubmit, handle tables, zero-allocation WebGPU call patterns, caching keys for pipelines and bind groups, upload/readback strategies, draw-call budgets, and the native-ICD alternative.
tags: [performance, batching, interop, jsimport, command-stream, caching, webgpu, wasm]
sources:
  - id: webgpu-spec
    resource: https://gpuweb.github.io/gpuweb/
    title: WebGPU W3C Editor's Draft, 8 October 2026 (setBindGroup Uint32Array overload, setImmediates, writeBuffer AllowSharedBufferSource)
  - id: blazor-interop
    resource: https://learn.microsoft.com/en-us/aspnet/core/blazor/performance/javascript-interoperability?view=aspnetcore-8.0
    title: Microsoft Learn — Blazor JavaScript interop performance guidance (batch many small calls)
  - id: toji-bundles
    resource: https://toji.dev/webgpu-best-practices/render-bundles
    title: Brandon Jones, WebGPU best practices — render bundles
  - id: toji-uploads
    resource: https://toji.dev/webgpu-best-practices/buffer-uploads
    title: Brandon Jones, WebGPU best practices — buffer uploads
  - id: ics-media
    resource: https://ics.media/en/entry/230426/
    title: ICS MEDIA — WebGPU vs WebGL draw-call comparison (10,000 draws, M1, Chrome 113)
  - id: babylon-forum
    resource: https://forum.babylonjs.com/t/why-webgpu-backend-is-slower/24091
    title: Babylon.js forum — WebGPU backend performance and snapshot rendering
  - id: babylon-native-forum
    resource: https://forum.babylonjs.com/t/solving-cpu-bottleneck-in-nativexr-hill-valley-ar-portal-scene/63558
    title: Babylon.js forum — CPU bottleneck in encoder calls on older iPhones
  - id: wgpusharp
    resource: https://www.nuget.org/packages/WgpuSharp
    title: WgpuSharp — Blazor WebGPU with integer handles and a batched per-frame bridge
  - id: nikki
    resource: https://github.com/nihui/nikki
    title: nihui/nikki — static-linked Vulkan-over-WebGPU in the browser
  - id: emdawnwebgpu
    resource: https://dawn.googlesource.com/dawn/+/refs/heads/main/src/emdawnwebgpu/pkg/README.md
    title: Dawn emdawnwebgpu package README
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:13:57Z }
status: draft
---

# Cost model

| Layer | Approximate cost per call | Basis |
| --- | --- | --- |
| C# → JS via `[JSImport]` (primitive args) | hundreds of ns to a few µs (UNVERIFIED; measure in `/experiments/`) | Microsoft's interop guidance: each .NET↔JS call has overhead, batch small calls[^blazor-interop] |
| JS → WebGPU method (encoder state/draw) | ~µs; serialized to the GPU process (Dawn wire in Chrome) | encoder calls were the CPU ceiling for a Babylon scene on older iPhones[^babylon-native-forum] |
| WebGPU object creation (bind group, pipeline) | µs (bind group) to ms (pipeline compile) | — |
| wasm → JS import from C (Emscripten) | tens of ns (UNVERIFIED) | raw wasm import, no managed marshalling |

Consequence: a naive ICD doing one `[JSImport]` per `vkCmd*` roughly doubles the per-draw CPU cost of WebGPU itself. **Batch so that the .NET→JS boundary is crossed O(1) times per submit, not O(draws).** WgpuSharp, a Blazor WebGPU binding, arrived at the same "integer handles + batched per-frame bridge" design.[^wgpusharp]

# Command-stream architecture

```
C# (wasm)                                         JS (same thread)
---------------------------------------------     ---------------------------------------------
vkCreateBuffer/Texture/Sampler/... ─┐              handle table: objs[id] (dense array + freelist)
  ids allocated in C#; op appended  │
  to the DEVICE op stream           │
vkCmd* → append to the COMMAND      │
  BUFFER stream (per VkCommandBuffer)│
vkQueueSubmit ──────────────────────┴──► replay(devOpsPtr, devOpsLen, cbListPtr, n)
                                          1. execute device ops (create/destroy/writeBuffer/configure)
                                          2. for each cmd buffer: encoder = device.createCommandEncoder()
                                             decode ops, open/close passes, call WebGPU
                                          3. queue.submit(buffers)
                                          4. write results (e.g. readback ticket ids) into a wasm-side
                                             status block; schedule mapAsync callbacks
```

- **Ids are assigned by C#**, so object creation never needs a synchronous answer from JS and can itself be deferred into the device stream. Only genuinely synchronous queries (canvas size, adapter info) are read from a status block that JS keeps up to date (ResizeObserver writes the size into wasm memory).
- **Record format**: `u16 opcode, u16 byteLength, payload` with little-endian ints/floats and u32 object ids; variable arrays inline. Typical draw sequence (pipeline + 2 bind groups + vertex/index buffers + drawIndexed) ≈ 100 bytes; 5,000 draws ≈ 0.5 MB of stream per frame.
- **Decoding in JS**: a `DataView`/`Uint32Array` over the wasm memory buffer; re-create the views if `memory.buffer` changed (heap growth detaches old views). A single `switch` loop.
- **Recording is thread-agnostic**: since recording touches no WebGPU object, Vulkan's free-threaded command-buffer recording model survives even if all WebGPU calls happen on one thread — useful if the .NET runtime ever runs multithreaded.
- Command buffers are **replayed on every submit**, so re-submitting a `VkCommandBuffer` (without `ONE_TIME_SUBMIT`) works and costs one decode.
- nikki (C++) uses the same record-then-replay idea and links statically into the browser app.[^nikki]

# Zero-allocation WebGPU call patterns

| Call | Allocation-free form |
| --- | --- |
| `setBindGroup` with dynamic offsets | `setBindGroup(index, group, dynamicOffsetsData: Uint32Array, start, length)` over the wasm heap view — no JS array per draw[^webgpu-spec] |
| push constants | `setImmediates(rangeOffset, heapU8, dataOffset, size)` (`AllowSharedBufferSource`)[^webgpu-spec] |
| uploads | `queue.writeBuffer(buffer, offset, heapU8, ptr, size)` — one copy, straight from the shadow;[^webgpu-spec][^toji-uploads] `writeTexture` likewise |
| draws/state | primitive arguments only |
| pass begin, bind-group/pipeline creation | descriptor objects unavoidable → cache descriptor templates per VkRenderPass/VkFramebuffer and mutate only `view`/`clearValue` fields; create bind groups/pipelines rarely (caches below) |

On the C# side: no managed allocations per call (structs, `stackalloc`, `NativeMemory` arenas for streams, pooled lists); Vulkan handles = `(generation << 32) | index` into struct arrays so use-after-free is detectable in debug builds.

# Caches

| Cache | Key | Eviction | Notes |
| --- | --- | --- | --- |
| `GPUShaderModule` | SPIR-V hash + entry point + remap/Y-flip variant | never (small) | WGSL ideally precompiled at build time (`/shaders/`) |
| `GPUBindGroupLayout` | hash of the fully resolved WebGPU entry list | never | group-equivalent layouts are interchangeable[^webgpu-spec] |
| `GPUPipelineLayout` | ordered BGL ids + immediate size | never | |
| `GPURenderPipeline` variant | Vk pipeline id + dynamic-state values (cull, front face, topology, depth/stencil, bias, strip index format, vertex strides) + attachment formats/sample count | LRU, cap ~1–4k | first use may stall; pre-warm known variants |
| `GPUBindGroup` | (BGL id, descriptor-set id, set version) | drop on set update/free; LRU cap | per-frame transient sets (push descriptors) keyed by contents hash |
| `GPUSampler` | descriptor hash | never | dedupe identical Vulkan samplers |
| Render-pass descriptor template | (VkRenderPass, VkFramebuffer) | on framebuffer destroy | swapchain views patched per frame |
| `GPURenderBundle` | secondary command buffer id + version | on reset | only for secondaries without dynamic viewport/scissor commands |

# Upload and readback strategy

- Default upload path is `writeBuffer` from the wasm heap at submit (coherency flush) or flush/unmap time; it is "a safe fallback that doesn't have many downsides" and avoids the extra copy a mapped staging buffer would need from wasm.[^toji-uploads]
- Large one-off uploads (meshes at load): still `writeBuffer` (or `mappedAtCreation` + one `set()` copy) — both are one copy from wasm.
- Texture uploads with unaligned rows: CPU repack into an ICD upload ring (256-byte pitch) during the coherency flush, then `copyBufferToTexture` in stream order.
- Readback: pooled `MAP_READ` staging buffers, 2–3 frames of latency, results copied into shadows by the `mapAsync` callback; fences signal on landing ([memory model](/webgpu/memory-model-and-mapping-emulation.md)).

# Draw-call budgets and how to stay within them

- Reported data points: at 10,000 draws a simple demo ran ~50 fps on WebGPU vs ~30 fps on WebGL (MacBook Air M1, Chrome 113);[^ics-media] Babylon.js developers call tens of thousands of draws per frame unsustainable and point to JS-side object handling as the real bottleneck.[^babylon-forum]
- Planning numbers for the PoC (UNVERIFIED, to be measured): desktop ≤ 2–5k draws/frame at 60 fps through the ICD; mobile ≤ 0.5–1k.
- Levers: (1) render bundles for static content — they skip much of the per-command validation/recording cost but cannot be modified after encoding;[^toji-bundles] map reusable Vulkan secondary command buffers onto them; (2) instancing and merged buffers (fewer `setVertexBuffer`/`setBindGroup` calls); (3) dynamic-offset uniform rings instead of per-draw bind groups; (4) immediates for per-draw constants; (5) avoid pipeline-variant churn (sort by pipeline).

# Native-ICD alternative (if the managed path is too slow or blocked)

| | Managed C# ICD (planned) | Native C/C++ ICD linked into `dotnet.wasm` |
| --- | --- | --- |
| Vulkan entry from BRUTAL | `delegate* unmanaged` → `[UnmanagedCallersOnly]` C# (feasibility UNVERIFIED in Mono-wasm) | `delegate* unmanaged` → real wasm function; `[LibraryImport("vulkan")]` resolvable by static linking |
| WebGPU access | own JS replay layer | emdawnwebgpu's `webgpu.h` JS glue (tested, Dawn-maintained)[^emdawnwebgpu] or own replay layer |
| Build | pure .NET + JS | requires the .NET wasm native toolchain (Emscripten) |
| Reuse | — | could start from nikki (compute) or port VkWebGPU-ICD ideas |
| Debuggability | C# end-to-end | mixed |

Either way the batching design above applies; with emdawnwebgpu each `webgpu.h` call is one wasm→JS import (cheap), so batching matters less but replay-at-submit is still needed for Vulkan semantics.

# Open questions

- UNVERIFIED: measured cost of a `[JSImport]` call with 4–6 integer arguments in .NET 10 browser-wasm (interpreter vs AOT); this sets how aggressive batching must be.
- UNVERIFIED: whether `DataView` decoding of a 0.5 MB stream per frame is a measurable cost (expected sub-millisecond).
- Whether Chrome's WebGPU implementation batches small encoder calls internally well enough that render bundles are unnecessary for the PoC.

[^webgpu-spec]: WebGPU W3C Editor's Draft, 8 October 2026 (setBindGroup Uint32Array overload, setImmediates, writeBuffer AllowSharedBufferSource)
[^blazor-interop]: Microsoft Learn — Blazor JavaScript interop performance guidance (batch many small calls)
[^toji-bundles]: Brandon Jones, WebGPU best practices — render bundles
[^toji-uploads]: Brandon Jones, WebGPU best practices — buffer uploads
[^ics-media]: ICS MEDIA — WebGPU vs WebGL draw-call comparison (10,000 draws, M1, Chrome 113)
[^babylon-forum]: Babylon.js forum — WebGPU backend performance and snapshot rendering
[^babylon-native-forum]: Babylon.js forum — CPU bottleneck in encoder calls on older iPhones
[^wgpusharp]: WgpuSharp — Blazor WebGPU with integer handles and a batched per-frame bridge
[^nikki]: nihui/nikki — static-linked Vulkan-over-WebGPU in the browser
[^emdawnwebgpu]: Dawn emdawnwebgpu package README
