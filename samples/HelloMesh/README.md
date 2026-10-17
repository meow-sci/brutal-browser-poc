# HelloMesh

A small BRUTAL sample game: it loads a glTF model (Khronos "Sunglasses"), renders it with Vulkan 1.3 dynamic rendering, lets you orbit, pan and zoom around it, and plays a sound. The game code in `Game/` is written against the BRUTAL Vulkan, GLFW and glTF APIs only and is compiled unchanged for the desktop (`HelloMesh.Desktop`) and for the browser (`HelloMesh.Browser`: WebAssembly + WebGPU, page in `samples/HelloMesh.Web`). To build your own game on this base, read [`docs/game-guide.md`](../../docs/game-guide.md).

```
Game/                         shared game code (compiled into both hosts)
  HelloMeshApp.cs             composition root the hosts construct: RenderHost + SunglassesScene + sound; Frame(dt), ShouldClose,
                              PlaySound, RequestScreenshot/TakeScreenshot; prints READY, FRAME n, AUDIO ...
  Rendering/RenderHost.cs     reusable: VulkanContext, Swapchain, FrameResources (2 frames in flight), acquire/record/submit/present,
                              resize; IFrameRecorder (what a scene implements), RenderTarget, FrameRendered event
  Rendering/                  VulkanContext, Swapchain, FrameResources, MeshRenderer, GpuMesh, GpuTexture, Uniforms, Barriers,
                              ScreenshotCapture (desktop --screenshot only), GraphicsConfig (EnableValidation)
  Scene/SunglassesScene.cs    the game content: model, camera, input mapping, Update(dt), Record(cmd, target); prints CAMERA
  Scene/OrbitCamera.cs   Input/InputController.cs   Audio/IAudio.cs
  Assets/                     EmbeddedAssets, ModelLoader (Brutal.Gltf), GlbModel, AccessorData, PngDecoder
  Shaders/                    mesh.vert, mesh.frag (GLSL 450, Vulkan dialect)
HelloMesh.Desktop/            Program.cs (GLFW window and loop, --frames/--screenshot/--validation), OpenAlAudio.cs (NVorbis + Brutal.OpenAl)
HelloMesh.Browser/            Program.cs (web ICD, GLFW drop-in, BrowserHost.RunLoop, the "play-sound" page command), BrowserAudio.cs (Web Audio), FrameStats.cs (PERF)
Assets/                       SunglassesKhronos.glb, 874222__samrad20__generic-explosion.ogg, LICENSES.md
```

## Build and run (desktop)

Requirements: .NET SDK 10, `glslc` on the PATH (or `-p:GlslcPath=...`), a Vulkan loader and driver (on macOS the LunarG SDK with MoltenVK; no environment variables are needed).

```sh
dotnet build samples/HelloMesh/HelloMesh.Desktop -c Release
dotnet run --project samples/HelloMesh/HelloMesh.Desktop -c Release
```

Shaders are compiled by `tools/Shaders.targets` (`glslc --target-env=vulkan1.1 -O`) and embedded together with the GLB and the OGG as resources named `Shaders/<file>.spv` and `Assets/<file>`.

### Command line

| Option | Effect |
| --- | --- |
| `--frames N` | exit after N frames; the sound is also triggered once at frame 10 (exercises decode and playback) |
| `--screenshot FILE.ppm` | write the last frame as a binary PPM (readback with `vkCmdCopyImageToBuffer`); implies `--frames 120` if not given |
| `--validation` | enable `VK_LAYER_KHRONOS_validation` when the loader reports it (alternatively `VK_INSTANCE_LAYERS=VK_LAYER_KHRONOS_validation`) |

`dotnet run` keeps the current directory, so relative screenshot paths are relative to where you invoke it. Example smoke test (a window appears briefly):

```sh
dotnet run --project samples/HelloMesh/HelloMesh.Desktop -c Release -- \
  --frames 240 --validation --screenshot samples/HelloMesh/HelloMesh.Desktop/out/desktop.ppm
```

Expected: exit code 0, no `Validation Error` lines, the lines `AUDIO loaded`, `READY`, `AUDIO play`, `FRAME 60` ... `FRAME 240`, and a PPM whose background is the clear colour (0.10, 0.20, 0.30) with the glasses in the middle. If OpenAL cannot open a device the run continues and prints `AUDIO error <message>`.

## Build and run (browser)

`HelloMesh.Browser` compiles the same `Game/` sources against the browser flavor of the bindings (`src/Brutal.Vulkan.Browser`, `src/Brutal.Vulkan.Abstractions.Browser`), the web ICD (`src/Brutal.Vulkan.WebGpu`), the browser host (`src/Brutal.Browser`) and the GLFW drop-in (`src/Brutal.Glfw.Browser`). `Program.cs` installs the ICD, loads the embedded shader manifest, creates the window and the game, registers the frame delegate (`Glfw.PollEvents()` + `app.Frame(dt)`) with `BrowserHost.RunLoop` and returns; the page (`samples/HelloMesh.Web`, Vite) drives it from `requestAnimationFrame`.

Requirements: as for the desktop, plus Node 24 (the shader build translates SPIR-V to WGSL with naga and runs `npm ci` in `tools/` on first use) and a browser with WebGPU (Chrome/Edge 113+) on `http://localhost` or `https://`.

```sh
cd samples/HelloMesh.Web
npm install
npm run build:dotnet      # dotnet publish -c Release ../HelloMesh/HelloMesh.Browser (trimmed, Mono interpreter)
npm run dev               # http://localhost:5173, then click "Click to start" (the click starts the AudioContext)
npm run test:e2e          # Playwright, headless Chromium; npm run test:e2e:chrome uses Google Chrome; add --strict-autoplay (node test/e2e.mjs --strict-autoplay) to pin Chrome's autoplay policy
```

A plain `dotnet build samples/HelloMesh/HelloMesh.Browser -c Release` (or `-c Debug` with `DOTNET_CONFIGURATION=Debug npm run dev`) is enough for the dev server; `npm run build` writes a deployable `dist/`. The e2e test checks READY, FRAME 120, pixels (screenshot and GPU readback), a 200 px drag turning the camera by 80 degrees, ArrowUp zooming in, Space and the page's "Play sound" button each playing the sound with a measured output level above 0.01 within 500 ms, a sound started before the first click being audible after it, and the AudioContext state (details: `samples/HelloMesh.Web/README.md`).

### What differs between the hosts

| | Desktop | Browser |
| --- | --- | --- |
| Vulkan | NuGet `Ahwoo.BRUTAL.Vulkan` + Vulkan loader + MoltenVK/driver | bindings built from BRUTAL source (managed function pointers) + `Brutal.Vulkan.WebGpu` (non-conformant Vulkan 1.3 subset on WebGPU) |
| Shaders | `Shaders/*.spv` | the same `.spv` bytes; the ICD looks up their WGSL by SHA-256 in the embedded `Shaders/manifest.json` |
| Window, input | GLFW | `Brutal.Glfw.Browser` over the page canvas; `CreateInfo.Size` is ignored, the swapchain extent is the canvas backing-store size (fixed-size surface, recreated on resize) |
| Loop | `while` loop in `Program.cs` | `requestAnimationFrame` -> `BrowserHost.Tick` -> frame delegate; `Main` returns at once |
| Audio | NVorbis + OpenAL | Web Audio `decodeAudioData` (NVorbis fallback); decoding starts at load, playback once a click (start overlay or "Play sound") or key resumed the AudioContext; a sound started earlier is heard when it resumes |
| Page commands | none | `BrowserHost.RegisterCommand("play-sound", ...)` in `Program.cs`; the page's button calls `BrowserHost.Dispatch("play-sound")` (architecture.md section 5) |
| Waits | `vkDeviceWaitIdle` at shutdown and swapchain rebuild | the same calls; the ICD returns at once (fences signal at submit, nothing blocks) |
| Options | `--frames`, `--screenshot`, `--validation` | none; no `--screenshot` (readback fences complete a frame later), the test uses `window.__brutal.readPixel` |
| glTF | Brutal.Gltf | Brutal.Gltf: its reflection-based System.Text.Json needs `JsonSerializerIsReflectionEnabledByDefault=true` and `TrimmerRootAssembly Brutal.Gltf` in the trimmed build; the temporary file lives on the wasm virtual file system |

Limits: one canvas; the browser decides the present rate (rAF, normally the display refresh); the page never closes the window, so `HelloMeshApp.Dispose` does not run in the browser; an exception in the frame stops the browser loop for good (`ERROR <message>`). The Release publish prints one expected trim warning (`IL2104` for Brutal.Gltf, covered by the root above). Measured on an Apple M4 Pro (Chromium 156 and Chrome 155, headless, 1280x720, Mono interpreter): 0.35-0.9 ms average and 0.7-1.4 ms p95 CPU per frame, 60 fps, no garbage collections in steady state (allocations are the console lines); isolated frames up to ~16 ms are not GC (most likely jiterpreter trace compilation).

Mono AOT (`dotnet publish ... -p:RunAOTCompilation=true`, or `npm run build:dotnet -- -p:RunAOTCompilation=true`) was measured in `docs/research/experiments/exp-07-hellomesh-aot.md`: the e2e still passes 17/17; publish 11.8 s -> 55.7 s; `_framework` 6.57 -> 18.38 MB raw and 2.18 -> 4.15 MB brotli (`dotnet.native.wasm` 1.5 -> 13.3 MB); CPU per frame in steady state about 0.5 -> 0.2-0.3 ms and the 9-18 ms warm-up spikes disappear (max 1.2 ms); FRAME 120 and the 60 fps pacing are unchanged. The interpreter stays the default (this sample uses 2-5 percent of the frame); AOT and interpreter publishes share one output folder, so delete `bin/Release` and `obj/Release` when switching.

## Controls

| Input | Action |
| --- | --- |
| Left mouse drag | rotate (yaw/pitch) |
| Mouse wheel, Up / Down | zoom |
| Left / Right | pan horizontally |
| R | reset the camera |
| Space | play the sound |
| "Play sound" button (browser page, bottom right) | play the sound (page command `play-sound`; also unlocks the browser's AudioContext) |

## Diagnostics (console contract, see `docs/architecture.md` section 12)

`READY` after initialisation; `FRAME <n>` every 60 frames; `CAMERA yaw=<deg> pitch=<deg> dist=<m> pan=<x>,<y>` once per frame in which the camera changed; `AUDIO loaded`, `AUDIO play`, `AUDIO error <msg>`; `ERROR <message>` for exceptions caught by the host. The browser host also prints `PERF avg=<ms> p95=<ms> max=<ms> interval=<ms> alloc=<bytes/frame> gc=<n>` every 300 frames: CPU time of the frame delegate (input, update, recording, the submit's JS replay call), the mean time between animation frames, managed bytes allocated per frame and garbage collections in the window.

## How the sample uses the Vulkan subset

The general rules (what the browser ICD implements, which BRUTAL libraries exist in the browser) are in [`docs/game-guide.md`](../../docs/game-guide.md). The sample's own choices:

- `RenderHost`: one graphics+present queue, two frames in flight; each frame waits only on its own fence (`vkWaitForFences`, infinite timeout), acquires once, presents once and returns. Render-finished semaphores are per swapchain image (owned by `Swapchain`). `OUT_OF_DATE` and window resizes rebuild the swapchain after `vkDeviceWaitIdle` (a no-op in the browser). `GraphicsConfig.EnableValidation` is the in-process switch behind `--validation`.
- `SunglassesScene`: dynamic rendering and `vkCmdPipelineBarrier2` only (no render pass, no framebuffer); colour and depth `storeOp = STORE`; `D32_SFLOAT` depth; one sample per pixel; positive-height viewport, the Y flip is in the projection matrix (`proj[1][1] *= -1`); front face CCW, back-face culling, no blending.
- Buffer sizes and offsets are multiples of 4 (odd uint16 index counts get one unused trailing index); vertex buffers are bound at offset 0.
- One dynamic uniform buffer per frame in flight; draw offsets are multiples of `minUniformBufferOffsetAlignment` read from the device limits.
- Texture upload through a staging buffer whose rows are padded to 256 bytes (`bufferRowLength` carries the padded pitch); a 1x1 white texture serves untextured materials.
- Descriptor set 0: binding 0 dynamic UBO (`PerDraw`: model, view, proj, baseColor, lightDir), binding 1 `texture2D`, binding 2 `sampler`. No push constants, no combined samplers, no specialization constants.
- No `Brutal.Concurrency`, `Brutal.Vulkan.Vma`, `Brutal.ShaderC`, no `Brutal.Texture` decoding (the managed `PngDecoder` handles the model's PNG) and no file IO except the temporary file Brutal.Gltf needs.

### Vulkan commands used (ICD checklist)

Measured with `VK_LAYER_LUNARG_api_dump` on the desktop build; `vkGetInstanceProcAddr` and `vkGetDeviceProcAddr` come from the BRUTAL loader.

- Instance: `vkCreateInstance`, `vkDestroyInstance`, `vkEnumeratePhysicalDevices`, `vkGetPhysicalDeviceProperties`, `vkGetPhysicalDeviceQueueFamilyProperties`, `vkGetPhysicalDeviceMemoryProperties`, `vkEnumerateDeviceExtensionProperties`, `vkGetPhysicalDeviceSurfaceSupportKHR`, `vkGetPhysicalDeviceSurfaceCapabilitiesKHR`, `vkGetPhysicalDeviceSurfaceFormatsKHR`, `vkDestroySurfaceKHR`, `vkCreateDevice` (with `--validation` also `vkEnumerateInstanceLayerProperties`).
- Device: `vkDestroyDevice`, `vkGetDeviceQueue`, `vkDeviceWaitIdle`, `vkCreateSwapchainKHR`, `vkGetSwapchainImagesKHR`, `vkAcquireNextImageKHR`, `vkQueuePresentKHR`, `vkDestroySwapchainKHR`, `vkQueueSubmit`, `vkCreateImage`, `vkDestroyImage`, `vkGetImageMemoryRequirements`, `vkBindImageMemory`, `vkCreateImageView`, `vkDestroyImageView`, `vkCreateBuffer`, `vkDestroyBuffer`, `vkGetBufferMemoryRequirements`, `vkBindBufferMemory`, `vkAllocateMemory`, `vkFreeMemory`, `vkMapMemory`, `vkUnmapMemory`, `vkCreateSampler`, `vkDestroySampler`, `vkCreateDescriptorSetLayout`, `vkDestroyDescriptorSetLayout`, `vkCreateDescriptorPool`, `vkDestroyDescriptorPool`, `vkAllocateDescriptorSets`, `vkUpdateDescriptorSets`, `vkCreateShaderModule`, `vkDestroyShaderModule`, `vkCreatePipelineLayout`, `vkDestroyPipelineLayout`, `vkCreateGraphicsPipelines`, `vkDestroyPipeline`, `vkCreateCommandPool`, `vkDestroyCommandPool`, `vkResetCommandPool`, `vkAllocateCommandBuffers`, `vkResetCommandBuffer`, `vkBeginCommandBuffer`, `vkEndCommandBuffer`, `vkCreateFence`, `vkDestroyFence`, `vkWaitForFences`, `vkResetFences`, `vkCreateSemaphore`, `vkDestroySemaphore`.
- Recording: `vkCmdPipelineBarrier2`, `vkCmdBeginRendering`, `vkCmdEndRendering`, `vkCmdBindPipeline`, `vkCmdSetViewport`, `vkCmdSetScissor`, `vkCmdBindVertexBuffers`, `vkCmdBindIndexBuffer`, `vkCmdBindDescriptorSets`, `vkCmdDrawIndexed`, `vkCmdCopyBuffer`, `vkCmdCopyBufferToImage`.
- Screenshot only (`HelloMeshApp.RequestScreenshot` → `ScreenshotCapture`, not used by the browser): `vkCmdCopyImageToBuffer`.

Swapchain images are created with `TRANSFER_SRC` usage only when the surface reports it (needed for the screenshot); the shared code always requests `ColorAttachment`.

## Licences

The model is CC BY 4.0 (Khronos glTF Sample Assets); see `Assets/LICENSES.md`. Using BRUTAL carries the credit and notice obligations of the BRUTAL licence.
