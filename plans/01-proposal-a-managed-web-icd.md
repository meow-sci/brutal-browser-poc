# Proposal A — Managed Vulkan-on-WebGPU ICD for BRUTAL (recommended)

Status: proposed 2026-10-16. Evidence and rationale: [architecture decision record](../docs/research/decisions/architecture-options.md).

## 1. Summary

The game is ordinary BRUTAL C# code written against `Brutal.Vulkan` + `Brutal.Vulkan.Abstractions` (plus Numerics, Memory, Strings, Gltf, …). For the browser we build:

| Component | Purpose | Seed in this repo |
| --- | --- | --- |
| `Brutal.Vulkan` / `Brutal.Vulkan.Abstractions` **browser flavor** | the open-source bindings compiled from the pinned BRUTAL tree with (a) the pluggable-loader patch and (b) managed function pointers (`delegate*<…>`) so a C# implementation can sit behind them without a native relink (exp-06: 52/52 checks, 31–41 ns per call in the browser interpreter) | `projects/exp-04-brutal-vulkan-source-build`, `projects/exp-06-managed-fnptr-flavor` (its `tools/transform.py`, `src/Directory.Build.targets` and patched files are the seed) |
| `Brutal.Vulkan.WebGpu` (the **web ICD**) | C# implementation of the Vulkan subset `VP_BRUTAL_web_poc_2026` on top of WebGPU: object tables, command recording, replay on submit, memory shadowing, shader manifest lookup, canvas swapchain | `projects/exp-03-webgpu-jsimport` (bridge + command stream), research `/webgpu/*` |
| `Brutal.Browser` | host runtime: canvas and DPR, requestAnimationFrame tick, input queue, time, Web Audio, logging provider | research `/io-audio/*` |
| `Brutal.Glfw` **browser flavor** | source-compatible `Brutal.GlfwApi` subset so GLFW-shaped game code (and BRUTAL's ImGui GLFW backend) compiles unchanged | research `/io-audio/recommendation-io-audio.md`, `/brutal/windowing-input-audio-apis.md` |
| Shader build step | GLSL (later Slang) → SPIR-V (desktop and web) + WGSL + manifest keyed by SPIR-V hash | `projects/exp-02-shader-xcompile` |
| Vite 8 app + plugin | serves `_framework/` unbundled, reloads on .NET rebuild, builds/previews with Brotli | `projects/exp-05-vite-packaging` |
| `VP_BRUTAL_web_*.json` | Vulkan Profiles describing the subset; desktop validation with the Khronos profiles layer | research `/webgpu/web-vulkan-profile-proposal.md` |

The same `HelloMesh` sample compiles for desktop (NuGet packages, MoltenVK/Vulkan) and for the browser (flavor projects + ICD) from one source tree. That is the acceptance test for "the game author writes BRUTAL code and it runs in a browser".

## 2. Principles

1. **Do not fork BRUTAL semantics.** Patches to the bindings are additive and API-identical (exp-04 verified 0 public-signature differences); the browser flavor is a build configuration of the pinned upstream tree, applied by a script, never a diverging copy.
2. **The ICD is plain C#.** No `[UnmanagedCallersOnly]` or native code on the default path; unit-testable on desktop against a fake bridge; the same code can later be hosted over a native WebGPU implementation for headless CI.
3. **Batch the boundary.** One JS call per `vkQueueSubmit` (binary command stream), integer handles, pointer + length for bulk data, JSON only for rare descriptor-heavy calls. Measured: 0.228 ms vs 0.60 ms per 1000 draws (exp-03).
4. **Never block.** Vulkan's blocking calls follow the conventions in `/webgpu/synchronization-and-async-shape.md`: upload-only fences signal at submit, readback fences are polled across frames, the frame function returns after present.
5. **Prefer build-time work.** Shaders are translated and validated at build time; the runtime only hashes and looks up (naga-wasm fallback exists for unknown SPIR-V).
6. **Keep desktop parity testable.** The web profile JSON plus the Khronos profiles/validation layers tell a desktop developer whether their BRUTAL code will run in the browser before they open one.

## 3. Repository layout (target)

```
brutal-browser/
  Directory.Build.props          net10.0, LangVersion latest, AllowUnsafeBlocks, Nullable
  nuget.config                   nuget.org only (the user-level config references a private feed)
  global.json                    SDK 10.0.100
  patches/brutal/                exp-04 patch set (.patch) + transform.py (managed fnptr) + pin.txt (BRUTAL commit a25cdf8, package versions 2026.10.x)
  profiles/VP_BRUTAL_web.json    Vulkan Profiles JSON (poc + game tiers)
  src/
    Brutal.Vulkan.Browser/       csproj compiling <BRUTAL>/Brutal.Vulkan/** via transformed copies; AssemblyName Brutal.Vulkan
    Brutal.Vulkan.Abstractions.Browser/   same for Abstractions; AssemblyName Brutal.Vulkan.Abstractions
    Brutal.Vulkan.WebGpu/        the ICD (+ WebGpu bridge [JSImport] declarations + wwwroot/webgpu-glue.js)
    Brutal.Browser/              host, canvas, input queue, audio, logging (+ wwwroot/brutal-browser*.js)
    Brutal.Glfw.Browser/         Brutal.GlfwApi drop-in; AssemblyName Brutal.Glfw
  samples/
    HelloMesh/                   the PoC game (one project, two targets: net10.0 desktop and browser-wasm)
    HelloMesh.Web/               Vite 8 app (TypeScript) + vite-plugin-dotnet-framework.ts + test/headless.mjs
  tools/
    build-shaders.mjs, spv-prepare.mjs, patch-wgsl.mjs, gen-icd-stubs.py, gen-glfw-surface.py
  tests/
    Brutal.Vulkan.WebGpu.Tests/  xunit: ICD against a recording fake bridge; signature probes; profile reporting
  projects/                      the experiments (kept as evidence; not built by default)
  docs/research/, plans/
```

## 4. Phases

Effort figures are rough, for one engineer working with agentic help; they assume the experiments' code is reused rather than rewritten.

### Phase 0 — Prerequisites and scaffolding (≈ 0.5 day)

| ID | Task | Inputs | Output / acceptance |
| --- | --- | --- | --- |
| 0.1 | User installs the net10 `wasm-tools` workload (`dotnet workload install wasm-tools`). Not required until Phase 7, but it unblocks AOT and the A2 variant and removes the SDK's publish warning. | `/overview/toolchain-inventory-2026-10-16.md` | `dotnet workload list` shows `wasm-tools` |
| 0.2 | Create the layout above; `Directory.Build.props` (net10.0, `LangVersion` latest, unsafe, nullable, `ImplicitUsings` enable, `TreatWarningsAsErrors` off initially); `nuget.config` with nuget.org only; `global.json` pinning 10.0.100. | exp-04 `Directory.Build.props`, `nuget.config` | `dotnet build` of an empty solution succeeds |
| 0.3 | Pin BRUTAL: record commit `a25cdf8` and package versions (`Ahwoo.BRUTAL.Core 2026.10.1`, `Vulkan 2026.10.2`, `Vulkan.Abstractions 2026.10.2`, `Glfw 2026.10.4`, `OpenAl 2026.10.2`, `Gltf 2026.10.2`, `Numerics/Maths/Memory/Collections/Strings 2026.10.1`, `Logging 2026.10.2`) in `patches/brutal/pin.txt` and a `Directory.Packages.props` (central package management). | `/brutal/nuget-package-inventory.md` | versions resolve; prerelease `Microsoft.Extensions.ObjectPool` restores (exp-01: no opt-in needed) |
| 0.4 | Assets: download a Khronos glTF sample (e.g. `Duck.glb` or `Avocado.glb`) with its licence file into `samples/HelloMesh/Assets/`; add a short Ogg Vorbis test sound there. | brief | files + LICENSE present |
| 0.5 | Headless test driver: lift `projects/exp-03-webgpu-jsimport/test/run.mjs` + `serve.mjs` into `samples/HelloMesh.Web/test/`; parametrise browser path (Chrome for Testing 151 in the Playwright cache, or Edge 155). | `/web-packaging/headless-webgpu-testing.md` | `node test/headless.mjs --probe` prints the Metal adapter |

### Phase 1 — Desktop reference app `HelloMesh` (≈ 1–2 days)

Purpose: the exact BRUTAL code the browser must run, built only from NuGet packages, validated on MoltenVK, and constrained to the web profile from day one. Recipe: `/brutal/desktop-hello-mesh-recipe.md` (already compiled and run 180 frames clean under the validation layer by the research agent).

| ID | Task | Inputs | Output / acceptance |
| --- | --- | --- | --- |
| 1.1 | Project `samples/HelloMesh` (Exe, net10.0). Window via `Brutal.Glfw` (`ClientApi=0`); `Vulkan.CreateInstance` with `Glfw.GetRequiredInstanceExtensions` + `VK_KHR_portability_enumeration` + debug utils; pick a physical device; `PhysicalDevice.CreateDevice(DeviceEx.CreateInfo{ Next = Vulkan13Features(dynamicRendering, synchronization2) })`; raw `CreateSwapchainKHR` (Abstractions has no swapchain helper); `ImageEx` depth; per-frame `SyncObjects`. | recipe; `/brutal/vulkan-abstractions-catalogue-and-icd-surface.md` | window clears to a colour; validation layer clean |
| 1.2 | Mesh: load the GLB with `Brutal.Gltf` (`new GltfLoader(path)` + `GetAccessorBufferSpan`; positions, normals, uv, indices of primitive 0). Keep a 200-line managed GLB reader as fallback if trimming or the file-path API misbehaves in the browser (Phase 3). Upload with `BufferEx` + `StagingPool`. | `/brutal/asset-libraries.md` | mesh draws; index/vertex counts logged |
| 1.3 | Shaders `Shaders/mesh.vert`, `Shaders/mesh.frag` (GLSL 450, Vulkan dialect): set 0 binding 0 UBO `{ mat4 model, view, proj; vec4 lightDir; }`, binding 1 `texture2D`, binding 2 `sampler` (separate), locations 0/1/2 in; no push constants (or ≤ 64 B), std140 vec4-granular arrays only. Compile with `glslc --target-env=vulkan1.1 -O` at build time (MSBuild `Exec`), ship `.spv` as content; load with `ShaderModuleExtensions.FromSpirv`. | `/shaders/wgsl-conventions-for-vulkan-layer.md`, `/webgpu/web-vulkan-profile-proposal.md` | lit, textured (or vertex-coloured) model |
| 1.4 | Pipeline: `DescriptorSetLayoutEx`/`DescriptorPoolEx`, `MappedMemory` uniform buffer (persistently mapped is allowed), `VertexInput.For<T>`, `Presets.BlendState.BlendNone`, dynamic rendering, depth test, front face CCW, cull back. | recipe | model renders correctly oriented |
| 1.5 | Camera & input: orbit on mouse drag (yaw/pitch), wheel zoom, arrow keys pan/zoom; implemented against `GlfwWindow` events/polling only (no platform-specific calls). | `/io-audio/brutal-glfw-and-openal-api-surface.md` | interactive on macOS |
| 1.6 | Audio (desktop): decode the OGG with `NVorbis` (managed) → `Brutal.OpenAl` buffer/source; play on Space. Wrap behind a sample-local `IAudioOutput { Task LoadAsync(string); void Play(); }` so the browser build swaps implementations without touching game logic. | `/io-audio/browser-audio-options.md` | sound plays on key press |
| 1.7 | Profile conformance: `profiles/VP_BRUTAL_web.json` (from the proposal's sketch; validate against the schema with the SDK tooling); run HelloMesh with `VK_LAYER_KHRONOS_validation` + `VK_LAYER_KHRONOS_profiles` (`profile_name=VP_BRUTAL_web_poc_2026`, `emulate_portability=true`); fix anything flagged. Document the exact layer settings. | `/webgpu/web-vulkan-profile-proposal.md` | zero validation/profile errors over 300 frames |
| 1.8 | Record the ICD checklist: run with `VK_LAYER_LUNARG_api_dump` (or the exp-04 logging loader) and save the distinct list of Vulkan commands HelloMesh calls to `docs/research/experiments/hellomesh-vulkan-surface.md`. Expected ≈ 13 instance + 50 device + 11 command-buffer functions. | recipe § "functions needed" | checklist file; becomes Phase 3's definition of done |

### Phase 2 — Browser flavor of the bindings (≈ 1 day)

| ID | Task | Inputs | Output / acceptance |
| --- | --- | --- | --- |
| 2.1 | `src/Brutal.Vulkan.Browser`: csproj compiling transformed copies of `<BRUTAL>/Brutal.Vulkan/**/*.cs` (transform script from exp-06: `delegate* unmanaged[Cdecl]<` → `delegate*<`; `#if BRUTAL_MANAGED_FNPTR` in the patched loader), with the exp-04 patch files (`ImportResolver.cs`, `VulkanApiEx.gen.cs` call-site change), `ImplicitUsings=enable`, `DisableRuntimeMarshalling` assembly attribute, `AssemblyName=Brutal.Vulkan`, `AssemblyVersion 1.0.0.0`. Property `BrutalManagedFnptr` (default true for browser, false for desktop builds of the same project). | exp-04 README + patches; exp-06 | builds for net10.0 and browser-wasm with 0 warnings; API diff vs NuGet = 0 public changes (reuse exp-04 `tools/ApiDump`) |
| 2.2 | `src/Brutal.Vulkan.Abstractions.Browser`: same approach (the NuGet DLL cannot be used with managed pointers because its IL does `calli unmanaged`). Handle `VulkanHelpers.cs`'s three `[UnmanagedCallersOnly]` debug-messenger callbacks under the flavor (plain static methods; the ICD calls them as managed pointers). | exp-06 findings | builds; `DeviceEx.AllocateBuffer` works against the fake ICD |
| 2.3 | Package conflict policy: consumers reference the flavor projects and add `<PackageReference Include="Ahwoo.BRUTAL.Vulkan" ExcludeAssets="compile;runtime" />` (and the same for Abstractions) to neutralise the transitive NuGet assemblies; encode this once in `Directory.Build.targets` keyed on `RuntimeIdentifier == browser-wasm`. | exp-04 (silent overwrite reproduced) | a browser build never ships the NuGet `Brutal.Vulkan.dll` (assert in a test by checking the published `_framework` list) |
| 2.4 | Fake ICD for tests (`tests/Brutal.Vulkan.WebGpu.Tests/FakeIcd.cs`, from exp-04/06): logs requested names, implements instance/device/buffer minimal flow, signature probes (8-arg, floats, 64-bit enum, 64-bit return). | exp-06 | xunit on desktop passes; the same assembly runs in headless Chromium via the exp-06 driver |
| 2.5 | Flavor policy (decided by exp-06): default A1 (managed pointers) for browser builds; A2 stays a Phase 7 trial. Rules: never mix the managed flavor with NuGet-built dependents (`Abstractions` must be source-built; `Brutal.Vulkan.Vma` is excluded from browser builds because native VMA cannot call managed pointers — use the `DeviceEx` allocator); `Brutal.ImGui.Backends` against the flavor is untested. | exp-06 report | policy recorded in `Directory.Build.targets` comments and `plans/README.md` |

### Phase 3 — `Brutal.Vulkan.WebGpu`, the ICD (≈ 5–8 days)

Ordered to get pixels early. Definition of done: the Phase 1.8 checklist is implemented and HelloMesh renders in headless Chromium with a pixel check. Conventions: `/webgpu/vulkan-to-webgpu-object-mapping.md`, `/webgpu/vulkan-to-webgpu-command-mapping.md`, `/webgpu/memory-model-and-mapping-emulation.md`, `/webgpu/synchronization-and-async-shape.md`, `/webgpu/format-tables.md`, `/webgpu/coordinate-systems-and-conventions.md`.

| ID | Task | Notes | Acceptance |
| --- | --- | --- | --- |
| 3.1 | **Skeleton and dispatch.** `Icd` static class; `GetInstanceProcAddr(VkInstance, byte*)` and `GetDeviceProcAddr` backed by a frozen dictionary of UTF-8 names → managed function pointers; handle encoding = 32-bit index + generation packed into `nint` (wasm32) via typed object pools (`Pool<Instance>`, `Pool<Buffer>`, …); null handles = 0. `tools/gen-icd-stubs.py` parses `Functions.gen.cs` and emits `Unsupported.g.cs`: one correctly typed stub per command (logs the name once, returns `VK_ERROR_FEATURE_NOT_PRESENT`/void) so an unimplemented call is a logged error, never a wasm trap. | exp-04: calling a null entry is fatal; stubs must match the exact signature | fake-free unit test: every one of the 110 instance and 697 device names resolves to a non-null pointer |
| 3.2 | **Instance / physical device / device / queue.** `vkEnumerateInstanceVersion` (1.2), extension/layer enumeration (profile lists), `vkCreateInstance`, one physical device whose properties/limits/features/memory types (type 0 DEVICE_LOCAL, type 1 HOST_VISIBLE\|HOST_COHERENT)/queue family (graphics+compute+transfer, count 1)/format properties come from the profile tables; `vkCreateDevice` (reject unsupported features/extensions), `vkGetDeviceQueue`, `vkDestroy*`. | `/webgpu/web-vulkan-profile-proposal.md`; adapter limits read from JS at init and clamped | xunit: HelloMesh's device creation path succeeds against the fake bridge |
| 3.3 | **Bridge.** `WebGpu.cs` `[JSImport]` surface and `webgpu-glue.js` lifted from exp-03: handle table, `localHeapViewU8()` re-fetched per call, `createRenderPipelineJson`, `replayCommandStream`, error scopes → console, `onSubmittedWorkDone` bookkeeping, `mapAsync` readback helpers. JS pre-initialises adapter + device + canvas context before .NET starts and exposes `getDevice()`/`getLimits()`/`getFeatures()`. A **recording fake bridge** (C#) implements the same interface for desktop tests. | exp-03 code | bridge unit tests; exp-03's triangle still renders through the lifted glue |
| 3.4 | **Surface and swapchain.** Instance extension `VK_BRUTAL_web_surface` (`vkCreateWebSurfaceBRUTAL(instance, canvasHandle)`), `vkGetPhysicalDeviceSurfaceSupport/Capabilities/Formats/PresentModesKHR` (preferred canvas format ± sRGB, FIFO, extent = canvas pixel size), `vkCreateSwapchainKHR` (configure context, `usage: RENDER_ATTACHMENT | COPY_SRC`), `vkGetSwapchainImagesKHR` (N virtual images), `vkAcquireNextImageKHR` (binds the current image slot to `getCurrentTexture()` for this frame), `vkQueuePresentKHR` (marks end of frame; returns `VK_ERROR_OUT_OF_DATE_KHR` after a resize). | `/webgpu/vulkan-to-webgpu-object-mapping.md` § swapchain | M1: HelloMesh's loop clears the canvas to the chosen colour (pixel check) |
| 3.5 | **Memory and buffers.** `vkCreateBuffer` creates the `GPUBuffer` (usage mapping; size rounded up to 4; `mappedAtCreation` never used), `vkGetBufferMemoryRequirements` (size, alignment 256, both memory types), `vkAllocateMemory` (host-visible → `NativeMemory.AllocZeroed` shadow; device-local → bookkeeping only), `vkBindBufferMemory` (record range → buffer), `vkMapMemory` (pointer into shadow), `vkFlushMappedMemoryRanges`/`vkUnmapMemory` (dirty ranges → `queue.writeBuffer` immediately, which WebGPU orders before later submits), `vkInvalidateMappedMemoryRanges` (no-op; readback is Phase 7), `vkCmdCopyBuffer`, `vkFreeMemory`, `vkDestroyBuffer`. Persistent mapping (BRUTAL `MappedMemory`) works because the shadow is always valid. | `/webgpu/memory-model-and-mapping-emulation.md` | xunit: `DeviceEx.AllocateBuffer` + `StagingPool` sequence produces the expected bridge calls |
| 3.6 | **Images, views, samplers.** `vkCreateImage` (`GPUTexture`; format table; usage; mips; layers; sample count 1/4), `vkGetImageMemoryRequirements` (synthetic), `vkBindImageMemory` (no-op), `vkCreateImageView` (aspect, mip/layer ranges, view format must equal image format ± sRGB), `vkCreateSampler` (filters, address modes, anisotropy, compare, LOD clamps; reject border colours in PoC), `vkCmdCopyBufferToImage` with the 256-byte `bytesPerRow` repack path, depth formats `D32_SFLOAT`, `D24_UNORM_S8_UINT`. | `/webgpu/format-tables.md` | xunit coverage; a texture uploads and samples in M3 |
| 3.7 | **Shaders, layouts, descriptors, pipelines.** `vkCreateShaderModule`: `SHA256.HashData(code)` → manifest entry (WGSL text, entry points, binding map incl. split samplers at +500, push mode, overrides); on miss → naga-wasm via `[JSImport]` + the same patch rules (fallback). `vkCreateDescriptorSetLayout` → `GPUBindGroupLayout` (visibility from stage flags; buffer/texture/sampler/storage types; `hasDynamicOffset`); `vkCreatePipelineLayout`; `vkCreateDescriptorPool`/`vkAllocateDescriptorSets`/`vkUpdateDescriptorSets` (bind group rebuilt lazily on first use after a write; immutable thereafter until the next write); `vkCreateGraphicsPipelines` (vertex buffers/attributes from `VkFormat → GPUVertexFormat`, topology, cull/front face 1:1, depth/stencil, blend, multisample; colour/depth formats from `VkPipelineRenderingCreateInfo` or the render pass; sync `createRenderPipeline` first, async variant behind a "pending" state in Phase 7), `vkCreateComputePipelines`, `vkCreatePipelineCache` (no-op). | `/shaders/wgsl-conventions-for-vulkan-layer.md`; exp-03 pipeline JSON | M2: a triangle through the full Brutal.Vulkan API (shader module → layout → pipeline → draw) |
| 3.8 | **Command recording and submission.** `vkCreateCommandPool`, `vkAllocateCommandBuffers`, `vkBegin/End/ResetCommandBuffer`; each command buffer owns a growable unmanaged byte stream of records (exp-03 opcode format extended: beginRendering/endRendering with attachments, load/store/clear values, resolve targets; bindPipeline; bindDescriptorSets with dynamic offsets; bindVertexBuffers; bindIndexBuffer; setViewport (reject negative height); setScissor; setBlendConstants; setStencilReference; draw/drawIndexed/drawIndirect/drawIndexedIndirect; dispatch/dispatchIndirect; copyBuffer; copyBufferToImage; copyImageToBuffer; copyImage; fillBuffer(0); pushConstants (write into the per-submit uniform ring at group 3 or immediates when available); pipelineBarrier/pipelineBarrier2/setEvent/waitEvents (no-op); beginDebugUtilsLabel/end (push/popDebugGroup); executeCommands (inline the secondary's stream)). `vkQueueSubmit`: concatenate streams → one `replayCommandStream`, then handle fences: signal immediately if the submit contains no readback, else attach to `onSubmittedWorkDone`. `vkWaitForFences` (returns `VK_SUCCESS` for signalled, `VK_TIMEOUT` otherwise — never blocks), `vkGetFenceStatus`, `vkResetFences`, `vkCreate/DestroySemaphore` (no-op objects), `vkQueueWaitIdle`/`vkDeviceWaitIdle` (no-op + one-time warning). | `/webgpu/vulkan-to-webgpu-command-mapping.md`, `/webgpu/synchronization-and-async-shape.md`, exp-03 `CommandStream.cs` | M3: HelloMesh renders the model in headless Chromium; pixel check at centre and corner; 120 frames without validation errors |
| 3.9 | **Legacy render passes** used by `FrameResources`/BRUTAL helpers: `vkCreateRenderPass` (single subpass; attachments → render-pass descriptor template; `storeOp DONT_CARE` warns), `vkCreateFramebuffer` (view list), `vkCmdBeginRenderPass/EndRenderPass` (same record as dynamic rendering), `vkCmdNextSubpass` unsupported in PoC. | `/webgpu/vulkan-to-webgpu-object-mapping.md` | HelloMesh variant using `FrameResources` renders identically |
| 3.10 | **Diagnostics.** `VK_EXT_debug_utils` object names → WebGPU labels; `WebProfileLint` (debug builds): asserts 256-byte dynamic offsets, 4-byte sizes/offsets, ≤ 8 vertex buffers, ≤ 4 bind groups, `STORE` for later-read attachments, positive viewport heights, no blocking waits on readback fences; messages name the Vulkan call and object. | `/webgpu/web-vulkan-profile-proposal.md` § desktop validation | lint catches each rule in a unit test |
| 3.11 | **Tests.** xunit on desktop with the recording fake bridge: golden call sequences for HelloMesh's frame; property tests for format mapping; the `gen-icd-stubs` coverage test. Browser integration: the exp-03 driver runs HelloMesh.Web and checks pixels. | — | CI script `scripts/test.sh` runs both |

### Phase 4 — `Brutal.Browser` host and `Brutal.Glfw` drop-in (≈ 2–3 days)

Design: `/io-audio/recommendation-io-audio.md` (API sketch), `/io-audio/managed-browser-input-shim-design.md` (tables), `/io-audio/main-loop-patterns-in-dotnet-wasm.md`.

| ID | Task | Acceptance |
| --- | --- | --- |
| 4.1 | `BrowserHost`: `StartAsync(options)`, `RunLoop(Func<double,bool>)`, `NextFrame()`, `Exit`, `Now` (performance.now), visibility events; `[JSExport] Tick(double)` driven by requestAnimationFrame from `main.ts`; the runtime is started with `dotnet.create()` + `getAssemblyExports` (never `dotnet.run()`). | tick cadence logged; loop stops on `Exit` |
| 4.2 | `BrowserCanvas`: CSS/pixel size, `devicePixelRatio`, ResizeObserver → resize events; `SurfaceHandle` used by `vkCreateWebSurfaceBRUTAL`; fullscreen request. | swapchain recreates on resize (OUT_OF_DATE path) |
| 4.3 | Input queue in JS (`brutal-browser.js`): keydown/keyup (`KeyboardEvent.code` → GLFW key table, left/right modifiers), mouse move/buttons/wheel (deltaMode normalisation), pointer lock (`unadjustedMovement`), focus/blur (release all keys), preventDefault policy for game keys; drained by `Glfw.PollEvents()` into callbacks + polled state. | synthetic events from puppeteer rotate/pan/zoom the model |
| 4.4 | `Brutal.Glfw` browser flavor (`AssemblyName=Brutal.Glfw`, namespace `Brutal.GlfwApi`): `Glfw.Init/Terminate/WindowHint/CreateWindow/PollEvents/Time/GetRequiredInstanceExtensions ("VK_KHR_surface","VK_BRUTAL_web_surface")/CreateWindowSurface`, `GlfwWindow` (Size, FramebufferSize, ContentScale, CursorPos, ShouldClose, GetKey, GetMouseButton, SetInputMode, events); enums/constants/structs generated verbatim from the pinned BRUTAL tree by `tools/gen-glfw-surface.py`; unsupported members throw `NotSupportedException` with the member name. Same `ExcludeAssets` policy as 2.3 for the NuGet `Ahwoo.BRUTAL.Glfw`. | HelloMesh's input code compiles unchanged for the browser |
| 4.5 | `Brutal.Browser.Audio`: `AudioEngine.Create()` from a user-gesture handler (overlay "click to start"), `ResumeAsync`, `LoadAsync(url)` → `decodeAudioData`; `Decode(bytes)` via NVorbis fallback when decode fails (Safari); `Sound.Play(gain, loop, position?)`; `IAudioOutput` implementation for the sample. | sound plays after the click and on Space; AudioContext state `running` asserted in the headless test |
| 4.6 | `BrowserConsoleLoggerProvider` for `Brutal.Logging` (`AddConsole` throws on wasm); wire in `BrowserHost.StartAsync`. | BRUTAL log lines appear in the browser console |
| 4.7 | Guard rails: `Brutal.Concurrency` excluded from browser builds (thread pool + busy-wait); `Platform` untouched (not on the PoC path); document for game ports. | build fails with a clear message if Concurrency is referenced for browser-wasm |

### Phase 5 — Shader build pipeline (≈ 1–2 days)

Design and prototype: `/shaders/build-time-vs-runtime-cross-compilation.md`, `projects/exp-02-shader-xcompile/src/build-shaders.mjs`.

| ID | Task | Acceptance |
| --- | --- | --- |
| 5.1 | `tools/build-shaders.mjs`: BOM strip → `glslc --target-env=vulkan1.1 -O -I` → `spirv-val` → `spv-prepare.mjs` (`spirv-opt --split-combined-image-sampler`, sampler binding +500, strip `NonReadable`) → layout lint (std140 scalar/vec2 arrays, 2-row matrices) → naga-wasm (Y flip on) → `patch-wgsl.mjs` (push constants → `@group(3) @binding(0) var<uniform>` or storage; diagnostics) → naga WGSL re-parse → emit `<name>.spv` (original bytes), `<name>.wgsl`, `manifest.json { sha256 → { wgsl, entryPoints, bindings, push, overrides } }`. Pin `naga-wasm` and record the glslc version in the manifest. | HelloMesh's shaders build; manifest lookup test passes |
| 5.2 | MSBuild target `BuildShaders` in `samples/HelloMesh` (both targets): `Inputs/Outputs` for incremental builds; `.spv` as `Content` for desktop, `.spv`+`.wgsl`+`manifest.json` into `wwwroot/shaders` for the browser. Desktop and browser load the **same** `.spv` bytes so hashes match. | `dotnet build` regenerates only on change |
| 5.3 | Runtime: `ShaderManifest` loader in the ICD (fetched at start by `main.ts`, handed to C# as bytes); `SHA256.HashData` on wasm (verify availability/speed; fallback `crypto.subtle.digest` via JS); naga-wasm `[JSImport]` fallback for unknown SPIR-V, applying the same C# patch rules. | unknown `.spv` still renders in dev; a missing manifest entry logs the shader name |
| 5.4 | Vite dev hook: re-run `build-shaders.mjs` on `.vert/.frag/.comp/.glsl` change (`handleHotUpdate`) and full-reload. | editing a shader updates the page |
| 5.5 | CI validation: headless Tint check (`createShaderModule` + `getCompilationInfo`) of every WGSL in the manifest. | script exits non-zero on any error |

### Phase 6 — Vite 8 web app and packaging (≈ 1 day)

Design: `/web-packaging/serving-dotnet-wasm-output-with-vite.md`, `projects/exp-05-vite-packaging/app/plugins/dotnet-framework.ts`.

| ID | Task | Acceptance |
| --- | --- | --- |
| 6.1 | `samples/HelloMesh.Web`: `npm create vite@latest` vanilla-ts (Vite 8.3.4, TypeScript 6), `@webgpu/types`, `naga-wasm` (dev), `puppeteer-core` (dev). | `npm run dev` serves a page |
| 6.2 | `vite-plugin-dotnet-framework.ts` (from exp-05): serve `bin/<cfg>/net10.0/wwwroot/_framework` (resolve via `*.staticwebassets.runtime.json`) with MIME types in dev; copy the `publish/wwwroot/_framework` into `dist/` on build; `.br`/`.gz` negotiation in preview; debounced full reload when the .NET output changes; `base` support. | dev/build/preview all pass the headless test |
| 6.3 | `main.ts`: overlay (click to start → AudioContext + optional pointer lock), WebGPU adapter/device/canvas pre-init, fetch `shaders/manifest.json`, `import(/* @vite-ignore */ BASE_URL + '_framework/dotnet.js')`, `dotnet.create()`, `setModuleImports` for `webgpu`, `brutal-browser`, `brutal-browser-audio`, `getAssemblyExports` → `Tick` on requestAnimationFrame, error overlay on uncaught exceptions. HelloMesh csproj for browser: `WasmEnableHotReload=false`, `InvariantGlobalization=true`, `WasmMainJSPath` unused (Vite owns the page), publish with Brotli. | HelloMesh renders in `npm run dev` |
| 6.4 | Headless test `test/headless.mjs`: launch Chrome for Testing/Edge headless, wait for `FRAME 120`, pixel assertions (GPU readback + screenshot), synthetic input, audio state; `npm run test:headless` in CI. | exit code 0 locally |
| 6.5 | Size report: raw/Brotli sizes of `_framework` for Debug and Release-trimmed; target ≤ 4 MB Brotli for the PoC (exp-01: 2.44 MB Brotli for the smoke app with BRUTAL packages). | numbers in `samples/HelloMesh.Web/README.md` |
| 6.6 | Licence compliance for a distributed build: BRUTAL `NOTICE.txt` in `dist/`, credits text, logo splash before the overlay dismisses. | present in `dist/` |

### Phase 7 — Hardening and the "viable path for a full game" (≈ 2–4 days; items are independent)

| ID | Task | Acceptance |
| --- | --- | --- |
| 7.1 | AOT (`wasm-tools`): `dotnet publish -c Release -p:RunAOTCompilation=true -p:WasmStripILAfterAOT=true`; measure frame time, command-encode cost and size vs interpreter; document in `docs/research/experiments/`. | numbers recorded; AOT build passes the headless test |
| 7.2 | A2 variant trial (`wasm-tools` present): build the bindings with `BrutalManagedFnptr=false` + `[UnmanagedCallersOnly]` ICD methods + anchor delegate declarations for the 29 missing signatures; confirm it runs; keep A1 as default unless A2 is faster under AOT. | report + decision note |
| 7.3 | Async pipeline creation (`createRenderPipelineAsync`) with a "pending" state and warm-up pass; pipeline/bind-group caches keyed by descriptor hash. | no multi-ms stalls on first draw after warm-up |
| 7.4 | Game-tier emulations as needed: push-constant ring (> 64 B), runtime combined-sampler split, readback (`mapAsync` with frame lag, `vkInvalidateMappedMemoryRanges`), occlusion/timestamp queries, `vkCmdBlitImage`/mip generation via compute, triangle fans, `vkCmdUpdateBuffer`, non-zero `vkCmdFillBuffer`. | each with a unit test and a profile JSON update |
| 7.5 | Desktop profile validation in CI (`VK_LAYER_KHRONOS_profiles` + validation) for `samples/HelloMesh`; `WebProfileLint` on by default in Debug. | CI job green |
| 7.6 | Browser matrix: Firefox 147+/Safari 26 manual runs; feature detection for immediates, swizzle, compressed formats; NVorbis fallback verified on Safari. | matrix table in the research bundle |
| 7.7 | Upstream engagement with Ahwoo/RocketWerkz: issue + PR for the loader hook (`AppContext` key), a generator flag for managed function pointers, `OperatingSystem.IsBrowser()` handling in `Platform`, an inline job mode in `Brutal.Concurrency`, and a browser logger; discuss a `Brutal.Browser` package. | issues filed; patch set shrinks as items land |
| 7.8 | Developer guide `docs/porting-guide.md`: the web profile checklist, the async-frame rules, shader conventions, what to replace (Concurrency, Fmod/OpenAl, ShaderC at runtime, Texture natives). | reviewed by the user |

## 5. Milestones and demo criteria

| Milestone | Demo | Phase |
| --- | --- | --- |
| M0 | Desktop HelloMesh renders the glTF model on MoltenVK with orbit/pan/zoom and sound, clean under validation + web profile | 1 |
| M1 | Browser: canvas cleared through `Brutal.Vulkan` calls (swapchain + command stream) | 3.4 |
| M2 | Browser: triangle through shader module → pipeline → draw | 3.7 |
| M3 | Browser: HelloMesh (same code as M0) renders the model with a pixel check in headless Chromium | 3.8 |
| M4 | Mouse/keyboard interaction and OGG playback in the browser; Vite dev server and production build | 4, 6 |
| M5 | AOT build measured; profile validation in CI; porting guide | 7 |

## 6. Risks specific to this plan

| Risk | Signal | Response |
| --- | --- | --- |
| A dependent assembly built against unmanaged pointers sneaks into a browser build | `MissingFieldException: … .Invoke` at run time (exp-06) | build check in 2.3; source-build every BRUTAL dependent that touches `Brutal.Vulkan` |
| Interpreter frame time too high for the sample | > 16 ms per frame at M3 | reduce per-frame calls (dirty-range flushes, cached bind groups), then Phase 7.1 AOT |
| `Brutal.Gltf` file-path API or reflection JSON fails under trimming in the browser | exception at load | virtual FS write + `TrimmerRootAssembly`, or the managed GLB reader fallback (1.2) |
| `SHA256.HashData` slow/unavailable on wasm | measured at 5.3 | hash in JS with `crypto.subtle.digest` once per module |
| Safari cannot decode the OGG | decode error | NVorbis fallback (4.5) |
| WebGPU immediates missing in Firefox/Safari | feature probe false | uniform ring at group 3 (5.1 patch mode) |

## 7. Out of scope for the PoC (explicitly)

Multithreading (`WasmEnableThreads`), ImGui in the browser (needs native imgui compiled to wasm — a later project), FMOD, networking, compute-heavy KSA features (bindless, BDA, ray tracing), and bundling `dotnet.js` into the Vite entry chunk.
