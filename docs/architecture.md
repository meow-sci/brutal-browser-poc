# Architecture and component contracts

This file is the single source of truth for how the components fit together. Background and evidence: `docs/research/decisions/architecture-options.md` and `plans/01-proposal-a-managed-web-icd.md`. How to build a game on it: [`docs/game-guide.md`](game-guide.md).

```
samples/HelloMesh/Game/*.cs          game code, identical for desktop and browser (BRUTAL Vulkan API)
  ├─ HelloMesh.Desktop  (NuGet Ahwoo.BRUTAL.* + MoltenVK/Vulkan + GLFW + OpenAL)
  └─ HelloMesh.Browser  (browser flavor of the bindings + Brutal.Vulkan.WebGpu + Brutal.Browser + Brutal.Glfw.Browser)
src/Brutal.Vulkan.Browser, src/Brutal.Vulkan.Abstractions.Browser   bindings compiled from the pinned BRUTAL tree
src/Brutal.Vulkan.WebGpu                                             the web ICD: Vulkan subset on WebGPU (C# + wwwroot/webgpu-glue.js)
src/Brutal.Browser                                                   host: canvas, frame loop, input queue, time, audio, logging (+ wwwroot/*.js)
src/Brutal.Glfw.Browser                                              source-compatible Brutal.GlfwApi subset over Brutal.Browser
samples/HelloMesh.Web                                                Vite 8 app: serves the .NET output, boots the runtime, drives the frame loop
tools/                                                               shader pipeline (glslc → SPIR-V → naga → WGSL + manifest), generators
```

## 1. Projects, assembly names, flavors

| Project | AssemblyName | Notes |
| --- | --- | --- |
| `src/Brutal.Vulkan.Browser` | `Brutal.Vulkan` | Compiles `$(BrutalSourceRoot)Brutal.Vulkan/**/*.cs` through `patches/brutal/transform.py` into `obj/`; applies the pluggable-loader patch (`Patched/ImportResolver.cs`, `Patched/.../VulkanApiEx.gen.cs`). Property `BrutalManagedFnptr` (default `true`) rewrites `delegate* unmanaged[Cdecl]<…>` to managed `delegate*<…>` and defines `BRUTAL_MANAGED_FNPTR`. |
| `src/Brutal.Vulkan.Abstractions.Browser` | `Brutal.Vulkan.Abstractions` | Same transform (strips the three `[UnmanagedCallersOnly]` debug callbacks). Must be built with the same flavor as `Brutal.Vulkan`. |
| `src/Brutal.Vulkan.WebGpu` | `Brutal.Vulkan.WebGpu` | References the two flavor projects and `Brutal.Browser`. |
| `src/Brutal.Browser` | `Brutal.Browser` | References NuGet `Ahwoo.BRUTAL.Core`, `Numerics`, `Logging`, `NVorbis`. No Vulkan dependency. |
| `src/Brutal.Glfw.Browser` | `Brutal.Glfw` | Namespace `Brutal.GlfwApi`. References `Brutal.Browser`. |
| `samples/HelloMesh/HelloMesh.Desktop` | `HelloMesh.Desktop` | `<Compile Include="../Game/**/*.cs" />`; NuGet packages only. |
| `samples/HelloMesh/HelloMesh.Browser` | `HelloMesh.Browser` | `<Compile Include="../Game/**/*.cs" />`; SDK `Microsoft.NET.Sdk.WebAssembly`; project references to the five `src/` projects. |

Rules:
- Never mix the managed-pointer flavor with NuGet-built assemblies that touch `Brutal.Vulkan` function pointers. In browser projects, neutralise transitive packages: `<PackageReference Include="Ahwoo.BRUTAL.Vulkan" ExcludeAssets="all" />`, same for `Ahwoo.BRUTAL.Vulkan.Abstractions` and `Ahwoo.BRUTAL.Glfw`. `Ahwoo.BRUTAL.Vulkan.Vma` is never referenced by browser builds.
- Central package versions live in `Directory.Packages.props` (no `Version` on `PackageReference`). NuGet source: nuget.org only (`nuget.config`).
- `patches/brutal/` holds the unified diffs against BRUTAL commit `a25cdf8` and `transform.py`; `BrutalSourceRoot` defaults to `../BRUTAL`.

## 2. Loader contract (Brutal.Vulkan ↔ ICD)

- Before the first touch of `Brutal.VulkanApi.Vulkan`, the host calls `Brutal.Vulkan.WebGpu.WebIcd.Install()`, which stores the ICD's `vkGetInstanceProcAddr` as a boxed `nint` under `AppContext.SetData("BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR", ptr)`. The patched loader reads it on any platform. If the key is missing, the managed-pointer flavor throws `PlatformNotSupportedException` with guidance on every platform (it has no native loader), while the unmanaged flavor falls back to upstream's native loading on Windows/Linux/macOS and throws only on browser.
- The four global commands (`vkEnumerateInstanceVersion`, `vkEnumerateInstanceExtensionProperties`, `vkEnumerateInstanceLayerProperties`, `vkCreateInstance`) are resolved with `GIPA(NULL, name)`.
- BRUTAL requests 110 instance-level names (the four above, 105 table entries, then `vkGetDeviceProcAddr`) and 697 device-level names. **The ICD returns a correctly typed function for every name** (unimplemented ones are generated stubs that throw `VulkanUnsupportedException(name)`), never a null pointer: calling a null managed pointer aborts the runtime.
- ICD entry points are plain `static` C# methods with the exact Vulkan signature as declared by the function-pointer structs in `Functions.gen.cs` (managed flavor). Handles are `nint` (32-bit on wasm). Structs are BRUTAL's explicit-layout structs.

## 3. Host ↔ ICD surface contract

- The PoC supports exactly one canvas. `Brutal.Browser.BrowserCanvas.SurfaceHandle` is `1`.
- `Brutal.Browser.BrowserSurfaces.CreateSurface` is a hook (`delegate int CreateSurfaceHandler(nint instance, nint canvasHandle, out ulong surface)`) that `WebIcd.Install()` sets. `Brutal.Glfw.Browser`'s `Glfw.CreateWindowSurface(nint instance, GlfwWindow window, out ulong surface)` calls the hook; `Glfw.GetRequiredInstanceExtensions` returns `VK_KHR_surface` and `VK_BRUTAL_web_surface`.
- The ICD also exposes the extension entry point `vkCreateWebSurfaceBRUTAL(VkInstance, const VkWebSurfaceCreateInfoBRUTAL*, const VkAllocationCallbacks*, VkSurfaceKHR*)` through `vkGetInstanceProcAddr` for non-GLFW callers.

## 4. JavaScript modules (served from the browser app's `wwwroot/`)

Each C# project ships its JS in `wwwroot/`; the Vite plugin serves/copies the whole published `wwwroot/` (not only `_framework/`). `main.ts` imports the modules with `import(/* @vite-ignore */ base + '<file>')`, calls `init(...)`, and registers `imports` with `setModuleImports(name, imports)`.

| Module name (JSImport) | File | `init` options | Owner |
| --- | --- | --- | --- |
| `webgpu` | `webgpu-glue.js` | `{ device: GPUDevice, canvas: HTMLCanvasElement, format: GPUTextureFormat, getHeapU8: () => Uint8Array }` | Brutal.Vulkan.WebGpu |
| `brutal-browser` | `brutal-browser.js` | `{ canvas: HTMLCanvasElement, getHeapU8 }` (input queue, resize, time, clipboard) | Brutal.Browser |
| `brutal-browser-audio` | `brutal-browser-audio.js` | `{ getHeapU8 }` (AudioContext created lazily by `decode`/`play`/`resume()` or by the first user gesture; main.ts calls `resume()` synchronously from the start overlay and the "Play sound" button) | Brutal.Browser |

`getHeapU8` is `() => runtime.localHeapViewU8()` from the dotnet runtime API; JS code must call it on every access (the view detaches when the heap grows).

## 5. Boot and frame protocol

1. `main.ts`: create the `<canvas>`, `navigator.gpu.requestAdapter()` → `requestDevice()` (device creation policy below), a bootstrap `configure` of the canvas context with `getPreferredCanvasFormat()` and usage `RENDER_ATTACHMENT | COPY_SRC` (the ICD configures the context again in `vkCreateSwapchainKHR` with the swapchain's format, usage and sRGB view format, so the swapchain decides); `init` the three JS modules; `dotnet.create()` (never `dotnet.run()`), `setModuleImports` ×3, `await runtime.runMain(mainAssembly, [])`. `WasmEnableHotReload=false`, `InvariantGlobalization=true`.
2. C# `Main` (HelloMesh.Browser): `WebIcd.Install()`, `WebIcd.LoadShaderManifest(...)`, construct the game (`HelloMeshApp` = `RenderHost` + `SunglassesScene`), then `BrowserHost.RunLoop(frame)` which stores the delegate and returns. `Main` must return promptly; nothing blocks.
3. `main.ts` then calls `exports.Brutal.Browser.BrowserHost.Tick(timestampMs)` from `requestAnimationFrame` (`getAssemblyExports("Brutal.Browser.dll")`). `Tick` computes `dt`, invokes the frame delegate, and returns `false` when the loop should stop.
4. A frame: `Glfw.PollEvents()` (drains the JS input queue) → update → `vkAcquireNextImageKHR` (binds the slot to `getCurrentTexture()`) → record → `vkQueueSubmit` (one `replayCommandStream` JS call) → `vkQueuePresentKHR` (end-of-frame marker) → return. Exactly one acquire/present per `Tick`.
5. Fences of submits without readback are signalled at submit; `vkWaitForFences` never blocks (returns `VK_SUCCESS`/`VK_TIMEOUT`); `vkQueueWaitIdle`/`vkDeviceWaitIdle` are no-ops. Once the `GPUDevice` is lost (`device.lost`), `vkQueueSubmit`, `vkAcquireNextImageKHR`, `vkQueuePresentKHR` and `vkWaitForFences` return `VK_ERROR_DEVICE_LOST`; the ICD logs one `ERROR [webgpu]` line, which also reaches every `VkDebugUtilsMessengerEXT`. The page owns the `GPUDevice`, so the game cannot recover without a reload.

6. **Page → game commands.** The page talks to the game through named commands, not through ad-hoc exports. The game registers handlers (`Brutal.Browser.BrowserHost.RegisterCommand(string name, Action handler)`, replaces an existing handler of that name; `UnregisterCommand(name)` removes one; HelloMesh.Browser registers `"play-sound"` → `app.PlaySound()` in `Program.cs`). The page runs one with `exports.Brutal.Browser.BrowserHost.Dispatch(name)` (a `[JSExport]`, same assembly export object as `Tick`) from a DOM event handler, so the handler executes synchronously inside the user gesture. `Dispatch` never throws into JS: an unknown name prints `WARN unknown command <name>`, an exception from a handler prints `ERROR <message>` (stack on stderr) and the frame loop keeps running. Commands take no arguments and return nothing; a command that needs data reads game state, or the page registers a new command per value. Gesture rule for audio: a handler that starts sound must be reached from a click/keydown handler that first calls `audio.imports.resume()` **synchronously** (no `await`, no async work before it), then `Dispatch(...)`: that is the `#play-sound` button in `main.ts`. The audio module also resumes a context that is not running on every `pointerdown`/`keydown`/`touchend` (capture phase) as a safety net, and `play` starts a voice on a suspended context anyway (it sounds as soon as the context runs).

Device creation policy: `main.ts` requests every feature and every limit the adapter offers. The ICD does not pass that through: the Vulkan limits it reports are the WebGPU core defaults (the `VP_BRUTAL_web_poc_2026` values) clamped by the device, so every conformant adapter gives the same limits, and an optional Vulkan feature or format (BC/ETC2/ASTC, `depthClamp`) is reported only when the WebGPU device has the matching feature. `vkCreateDevice` rejects features outside the profile with `VK_ERROR_FEATURE_NOT_PRESENT`.

## 6. Shader manifest (produced by `tools/build-shaders.mjs`, consumed by the ICD)

`Shaders/manifest.json` (embedded resource of the browser game assembly, LogicalName `Shaders/manifest.json`; the ICD receives it through `WebIcd.LoadShaderManifest(ReadOnlySpan<byte> json)` before the first `vkCreateShaderModule`):

```json
{
  "version": 1,
  "generator": { "glslc": "<glslc --version first line>", "naga": "<naga-wasm version>" },
  "conventions": { "combinedSamplerOffset": 500, "pushConstantGroup": 3, "pushConstantBinding": 0 },
  "shaders": {
    "<lowercase sha256 hex of the exact SPIR-V bytes>": {
      "name": "mesh.vert", "stage": "vertex|fragment|compute", "entryPoint": "main",
      "wgsl": "<full WGSL text>",
      "push": { "mode": "none|uniform|storage", "size": 0 },
      "bindings": [ { "group": 0, "binding": 0, "kind": "uniform|storage|texture|sampler|storageTexture", "name": "ubo" } ],
      "overrides": { "<SpecId>": { "name": "<wgsl override name>", "type": "bool|i32|u32|f32|f16" } }
    }
  }
}
```

`push.mode` is `none`, `uniform` or `storage` (the pipeline emits nothing else; the ICD fails `vkCreateShaderModule` for any other mode, WebGPU immediates are not used); `push.size` is the block size rounded up to 16. `overrides` is keyed by the SPIR-V `SpecId`; the pipeline `constants` key is that id. `bindings` and `stage` are informational: the ICD reflects bindings from the WGSL text itself.

Shader conventions (enforced by the pipeline's lint): Vulkan GLSL 450 compiled with `glslc --target-env=vulkan1.1 -O`; separate `texture2D` + `sampler` (combined samplers are split to texture `b` / sampler `b+500`); descriptor set → `@group`, binding → `@binding`; std140 UBOs with vec4-granular arrays; push-constant blocks of at most 128 bytes (the ICD's `maxPushConstantsSize`), which become a uniform (or, for std430-only blocks, read-only storage) buffer at group 3 binding 0, so at most 3 descriptor sets; the ICD feeds it from a ring of 256-byte slots (one per draw or dispatch after a `vkCmdPushConstants`; 4096 slots, doubled when one submit needs more); naga's default Y flip is used, so front-face winding is unchanged.

## 7. GLFW subset (what game code may use; what `Brutal.Glfw.Browser` implements)

`Glfw.Init()`, `Glfw.Terminate()`, `Glfw.WindowHint(GlfwWindowHint.ClientApi, 0)`, `Glfw.CreateWindow(in GlfwWindow.CreateInfo { Title, Size })`, `Glfw.PollEvents()`, `Glfw.Time` (get/set), `Glfw.GetRequiredInstanceExtensions(...)`, `Glfw.CreateWindowSurface(nint, GlfwWindow, out ulong)`;
`GlfwWindow`: `Size`, `FramebufferSize`, `ContentScale`, `CursorPos`, `ShouldClose` (get/set), `IsFocused`, `GetKey(GlfwKey)`, `GetMouseButton(GlfwMouseButton)`, `GetInputMode/SetInputMode(GlfwInputMode, int)`, `Dispose()`; events `OnKey`, `OnChar`, `OnMouseButton`, `OnCursorPos`, `OnScroll`, `OnFramebufferSize`, `OnSize`, `OnFocus`, `OnClose`, with BRUTAL's delegate signatures and enums (`GlfwKey`, `GlfwKeyAction`, `GlfwButtonAction`, `GlfwModifier`, `GlfwMouseButton`, `GlfwInputMode`, `GlfwCursorMode`, `GlfwWindowHint`) copied verbatim from the pinned BRUTAL tree. Joystick and gamepad queries (`JoystickPresent`, `GetJoystickAxes/Buttons/Hats/Name/Guid`, `JoystickIsGamepad`, `GetGamepadName/State`, ...) answer "nothing connected" instead of throwing, so per-frame polling works. Anything else declared throws `NotSupportedException` naming the member (the full list: `src/Brutal.Glfw.Browser/README.md`).

## 8. Audio

Game code uses the sample-local `IAudio { Task LoadAsync(ReadOnlyMemory<byte> oggBytes); void PlayOneShot(); }`. Desktop: `Brutal.OpenAl` + NVorbis. Browser: `Brutal.Browser.Audio` (`decodeAudioData`, NVorbis fallback when decoding fails).

Autoplay rules the browser side follows (`brutal-browser-audio.js`):
- Decoding happens at boot, before any user gesture, so the `AudioContext` is created then and, under Chrome's desktop policy (`document-user-activation-required`), is `suspended`. `decodeAudioData` works while suspended.
- `main.ts` calls `audio.imports.resume()` **synchronously** inside the click handlers of the start overlay and of `#play-sound` (no `await` before it). `resume()` creates the context if needed and calls `ctx.resume()` before it returns. Additionally the module listens for `pointerdown`/`keydown`/`touchend` (capture phase) and creates/resumes the context there, which covers games whose sound is triggered by a key the game only sees a frame later (outside the gesture) and Safari's `interrupted` state.
- `play` on a context that is not `running` calls `ctx.resume()` and starts the source anyway: the context clock does not advance while suspended, so the voice is heard from its start as soon as the context runs (verified by the e2e: a sound started before the first gesture is audible after the start click).
- Signal path: voice source → voice `GainNode` → master `GainNode` → `AnalyserNode` (pass-through) → `ctx.destination`. `imports.getOutputLevel()` / `window.__brutal.audio.getOutputLevel()` return the RMS (0..1) of the analyser's last frame (2048 samples), 0 while the context is not running. It proves that a signal reaches the end of the Web Audio graph; it cannot see the operating system mixer, a muted tab or a muted output device.

## 9. Assets

`SunglassesKhronos.glb`, the OGG and the PNG inside the GLB are embedded resources of the game assembly (`Assets/<file>`), loaded with `Assembly.GetManifestResourceStream`. GLB parsing: `Brutal.Gltf` on both hosts (`ModelLoader`, through a temporary file on the wasm virtual file system). PNG decoding: the sample's managed `PngDecoder` (8-bit RGB/RGBA, non-interlaced).

## 10. Testing

- xunit (`tests/`) for the bindings flavor (fake ICD) and the ICD (recording fake bridge).
- End-to-end: Playwright 1.64.1 (`samples/HelloMesh.Web/test/e2e.mjs`): launch with `chromium.launch({ channel: 'chromium', headless: true })` (the full Chromium in new headless mode exposes the WebGPU adapter with no flags; the default headless shell has no adapter), serve over `http://localhost` (secure context), wait for `FRAME 120`, check pixels (GPU readback and screenshot), send synthetic mouse/keyboard events, click `#play-sound`, check the audio state and the measured output level (`window.__brutal.audio.getOutputLevel() > 0.01` within 500 ms of `AUDIO play`, for Space and for the button). `--strict-autoplay` launches with `--autoplay-policy=document-user-activation-required` (Chrome's desktop default, pinned) and additionally requires the `AudioContext` to be `suspended` until the first click and a sound started before that click to be audible after it (the pre-gesture phase evaluates through CDP with `userGesture:false`: `page.evaluate` itself counts as a user gesture and would unlock the context). Do not use `--autoplay-policy=user-gesture-required` for Web Audio: for a top-level page it does not restrict the context at all (measured: the context is created `running`). Exit code 0 is the pass criterion.
- Desktop: `HelloMesh.Desktop` runs clean under `VK_LAYER_KHRONOS_validation` and the web profile (`profiles/VP_BRUTAL_web.json`, `VK_LAYER_KHRONOS_profiles`).

## 11. Code conventions

File-scoped namespaces; `sealed` classes; no per-frame allocations on hot paths (spans, pooled buffers, unmanaged command streams; once-only log messages test their key before formatting); public APIs get XML doc comments (`CS1591` is on in every `src/` project; only the verbatim BRUTAL files under `src/Brutal.Glfw.Browser/Generated/` are exempt); test seams of the ICD live in `IcdTestHooks`; each project has a short README (purpose, how to build/test, limits). Conventional commits.

## 12. Diagnostics markers (console contract used by the e2e tests)

The game writes these lines with `Console.WriteLine` (they reach `console.log` in the browser):

| Line | When |
| --- | --- |
| `READY` | after initialisation, before the first frame |
| `FRAME <n>` | every 60 frames (`n` = total frames rendered) |
| `CAMERA yaw=<deg> pitch=<deg> dist=<m> pan=<x>,<y>` | when the camera changed in a frame (at most once per frame) |
| `AUDIO play` / `AUDIO loaded` / `AUDIO error <msg>` | audio events |
| `ERROR <message>` | any caught exception (the host also logs the stack), including an exception thrown by a page command handler |
| `WARN unknown command <name>` | `BrowserHost.Dispatch(name)` for a name nobody registered (section 5, page → game commands) |
| `PERF avg=<ms> p95=<ms> max=<ms> interval=<ms> alloc=<bytes/frame> gc=<n>` | browser host (HelloMesh.Browser) only, every 300 frames: CPU time of the frame delegate, mean rAF interval, managed allocations per frame, gen-0 collections in the window; informational, tests do not assert on it |

Other console lines: the ICD writes `ERROR [webgpu] …` (the call failed or the command was dropped), `WARN [webgpu] …` (ignored or approximated feature, web-profile lint; once per function and reason) and `INFO [webgpu] …`; `webgpu-glue.js` writes `WEBGPU ERROR …` (`console.error`) for WebGPU validation errors, failed object creation, replay problems and device loss, which the ICD also reports as `ERROR [webgpu] WebGPU (<function>): …` after the next submit. `main.ts` writes one `HOST webgpu adapter=… format=… canvas=…` line.

Browser-side test hooks on `window.__brutal` (each JS module adds its own): `readPixel(x, y) → Promise<[r,g,b,a]>` (GPU readback of the current canvas texture), `frameCount`, `lastError` (`webgpu-glue.js`); `audio.getState()` (`'none'` until the `AudioContext` exists, then its `state`), `audio.sampleRate()`, `audio.voices()`, `audio.buffers()`, `audio.getOutputLevel()` (RMS 0..1 of the master mix, 0 while not running) (`brutal-browser-audio.js`); `browser.queued()`, `browser.dropped()`, `browser.locked()`, `browser.size()` (input queue, pointer lock, canvas sizes; `brutal-browser.js`). `main.ts` adds `window.__brutalAudioState()` (same as `audio.getState()`) and `window.__brutalDispatch(name)` (same as `BrowserHost.Dispatch`). The page uses `<canvas id="canvas">`, a start overlay `<button id="start">` and the command button `<button id="play-sound">` (disabled until boot finishes).
