# Building a game on brutal-browser

For a developer who writes a BRUTAL game (C#, Vulkan, GLFW) and wants the same game code to run on the desktop and in the browser. Component contracts: [`architecture.md`](architecture.md). The device the browser gives you: [`profiles/README.md`](../profiles/README.md) and [`src/Brutal.Vulkan.WebGpu/README.md`](../src/Brutal.Vulkan.WebGpu/README.md).

## 1. Where game code goes

| Part | Location | What it is |
| --- | --- | --- |
| Game | `samples/HelloMesh/Game/**` | Shared code, compiled into both hosts (`<Compile Include="../Game/**/*.cs" />`). Only BRUTAL APIs (Vulkan, Vulkan.Abstractions, GlfwApi, Gltf, Numerics, Logging) and the sample's `IAudio` seam; no `#if`, no `OperatingSystem.IsBrowser()`. |
| `RenderHost` | `Game/Rendering/RenderHost.cs` | Reuse as is: window surface, instance, device, queue, swapchain + depth, 2 frames in flight, acquire → record → submit → present, resize/`OUT_OF_DATE`, `FrameRendered` event, optional `Capture` (desktop screenshots). |
| Scene | `Game/Scene/SunglassesScene.cs` | Your game: assets, camera, input mapping, `Update(dt)` (state), `Record(CommandBuffer cmd, in RenderTarget target)` (implements `IFrameRecorder`: begin rendering into `target.ColorView`/`DepthView`, draw, end; leave the colour image in `COLOR_ATTACHMENT_OPTIMAL`). Index per-frame buffers with `target.FrameIndex`. |
| `HelloMeshApp` | `Game/HelloMeshApp.cs` | Composition root the hosts construct: `new HelloMeshApp(window, audio)`, `Frame(dt)` (= `scene.Update(dt)` + `host.RenderFrame(scene)`), `ShouldClose`, `PlaySound()`, `RequestScreenshot()`; prints the console markers. |
| Desktop host | `HelloMesh.Desktop/` | NuGet `Ahwoo.BRUTAL.*`, GLFW, OpenAL, a `while` loop; `--frames`, `--screenshot`, `--validation`. |
| Browser host | `HelloMesh.Browser/` | Browser flavor of the bindings + web ICD; `Program.cs` calls `WebIcd.Install()` (before anything touches `Brutal.VulkanApi.Vulkan`, otherwise the loader's static constructor throws a `TypeInitializationException`), `WebIcd.LoadShaderManifest(...)`, `BrowserHost.Initialize()`, builds the app, registers the frame with `BrowserHost.RunLoop` and **returns**. |
| Page | `samples/HelloMesh.Web/` | Vite page: WebGPU device, boot, `requestAnimationFrame` → `BrowserHost.Tick`. |

New game: copy `samples/HelloMesh` to `samples/<Game>` (both host projects, `Game/`, `Assets/`), rename the two projects (file, `AssemblyName`, `RootNamespace`; the relative `../../../src/...` references and `$(RepoRoot)tools/Shaders.targets` assume the same folder depth), replace `SunglassesScene` with your scene and the asset/shader items in both `.csproj` files, and add the projects to `BrutalBrowser.slnx`. Serve it with the existing page (`DOTNET_APP_DIR=../<Game>/<Game>.Browser npm run dev` in `samples/HelloMesh.Web`) or a copy of it. In the browser an exception that escapes `Frame` stops the loop for good (`ERROR <message>` + stack): catch inside `Frame` if the game should show its own error screen.

**Page commands (page → game).** To let an HTML control drive the game (a "Play sound" button, a menu entry), register a named handler in the browser `Program.cs` and dispatch it from the page: `BrowserHost.RegisterCommand("play-sound", app.PlaySound);` in C#, and in the page's click handler `audio.imports.resume(); exports.Brutal.Browser.BrowserHost.Dispatch('play-sound');` (`samples/HelloMesh.Web/src/main.ts`). The handler runs synchronously inside the click, so anything it starts counts as started by a user gesture; call `resume()` first and without any `await` before it, or the browser keeps the `AudioContext` suspended. Unknown names print `WARN unknown command <name>`, an exception in a handler prints `ERROR <message>` and the loop continues (`docs/architecture.md` section 5).

## 2. BRUTAL libraries in the browser

Available: Core, Memory, Collections, Strings, Numerics, Maths, Logging (`BrowserHost.Initialize()` installs a synchronous log provider; never `AddConsole`), Vulkan and Vulkan.Abstractions (built from the BRUTAL source in the browser flavor), Gltf (reflection-based JSON: keep `JsonSerializerIsReflectionEnabledByDefault` and the `TrimmerRootAssembly` of `HelloMesh.Browser.csproj`), and GLFW through `Brutal.Glfw.Browser` (the subset of architecture.md section 7). Not available (each needs a native library that has no wasm build):

| Library | Use instead |
| --- | --- |
| `Brutal.Vulkan.Vma` | `DeviceEx` as the allocator: `device.CreateBuffer(new BufferEx.CreateInfo { ... })`, `device.CreateImage(new ImageEx.CreateInfo { ... })` (one `vkAllocateMemory` per resource); suballocate yourself where you need many small buffers. |
| `Brutal.ShaderC`, `Brutal.Slang` at run time | Compile at build time: `<ShaderSource>` + `tools/Shaders.targets` (section 5). |
| `Brutal.Texture`, `Ktx`, `Gli`, `Stb` (decoding) | Decode in managed code (the sample's `PngDecoder`: 8-bit RGB/RGBA, non-interlaced) or ship pre-decoded RGBA; BC/ETC2/ASTC data only when the device reports the format. |
| `Brutal.Concurrency` | Run the work inline in `Update` (the browser runs .NET on one thread; the job system's busy-wait would hang); `async`/`await` for loading. |
| `Brutal.OpenAl`, `Brutal.Fmod` | `Brutal.Browser.Audio` (`AudioEngine`, `Sound`, `Voice`: Web Audio, OGG through `decodeAudioData` with an NVorbis fallback) behind an interface like the sample's `IAudio` (OpenAL on the desktop). The `AudioContext` is created at decode time and started by the page's start click or its own buttons (see Page commands). |
| `Brutal.ImGui` and its native backends, `ImPlot` | No replacement yet (Dear ImGui is native): HTML overlays in the page, or your own UI pass in Vulkan. |

Jolt, Box2D, SDL, MeshOptimizer, RakNet and the other native-backed libraries are not available either (`docs/research/brutal/library-map-and-portability-matrix.md`).

## 3. The Vulkan subset

The ICD advertises a non-conformant Vulkan 1.3 device (`VP_BRUTAL_web_poc_2026`); `vkCreateDevice` rejects anything outside it. Its limits are the WebGPU core defaults on every conformant adapter (architecture.md section 5, device creation policy). Run the desktop build under `scripts/validate-web-profile.sh` (section 6) to see violations as VUIDs before you open a browser. On top of what Vulkan can express, follow these rules (the ICD's `WebProfileLint` reports violations at run time):

- Render with dynamic rendering (`vkCmdBeginRendering`) or single-subpass render passes; no input attachments.
- One queue (graphics + compute + transfer, one queue); submits execute in order, semaphores are inert.
- Dynamic uniform/storage buffer offsets are multiples of 256 (`minUniformBufferOffsetAlignment`).
- Buffer copy sizes and offsets, vertex-buffer offsets and fill sizes are multiples of 4.
- At most 3 descriptor sets per pipeline layout: set 3 carries the push constants, which are at most 128 bytes and are served from a uniform buffer ring (one 256-byte slot per draw or dispatch after a `vkCmdPushConstants`). No descriptor arrays, no texel buffers.
- Separate `texture2D` + `sampler` descriptors (combined image samplers work, split to binding `b` + `b + 500`, binding `< 500`).
- Sample counts 1 or 4.
- `storeOp = STORE` for every attachment that is read later (a `DONT_CARE` store becomes `STORE` with a warning; WebGPU's discard reads back as zero).
- No blocking waits on readbacks: a copy into host-visible memory lands a frame or more later; `vkWaitForFences` returns `VK_TIMEOUT` instead of waiting, so poll the fence across frames.
- One acquire and one present per frame, then return from the frame; never loop or wait inside it.
- Not available: `vkCmdBlitImage` (generate mip levels offline or with a render/compute pass), resolves and image clears outside a render pass, queries, timeline semaphores, extended dynamic state (dynamic depth bias, stencil masks etc. are ignored with a warning: put them in the pipeline), negative viewport heights (flip Y in the projection), border colours, non-identity swizzles, buffer device address. Full list: the ICD README, "Known gaps".

## 4. Performance rules and ICD traps

- **Coherent memory is uploaded at every submit.** The ICD re-uploads the mapped range of every persistently mapped `HOST_COHERENT` allocation bound to a buffer on each `vkQueueSubmit`. Keep persistently mapped memory small (a per-frame uniform ring), or unmap/flush explicitly.
- **Pipeline creation is synchronous** (`createRenderPipeline` compiles at once; strip topologies get an index-format variant at the first indexed draw). Create every pipeline while loading, never mid-game.
- **Descriptor writes after the first bind rebuild the bind group** (from JSON). Write descriptor sets once; vary per-draw data with dynamic offsets.
- **Readbacks have a frame or more of latency** and cost a staging buffer and a `mapAsync` each.
- **No per-frame allocations.** .NET runs on the browser's only thread, so a garbage collection is a frame hitch. The ICD allocates nothing per frame in steady state; keep `Update`/`Record` allocation-free and watch `alloc=` in the `PERF` line.
- **One JS call per submit** (the command stream replay): prefer one submit per frame. More than 4096 push-constant updates in one submit make the ring grow (once).
- **Shaders are looked up by SHA-256** of the exact SPIR-V bytes: pass the `.spv` the build embedded; anything else fails `vkCreateShaderModule` with `VK_ERROR_INVALID_SHADER_NV` naming the hash.
- **Interpreter vs AOT.** The default publish runs on the Mono interpreter (plus the jiterpreter). Measured in exp-07 (`docs/research/experiments/exp-07-hellomesh-aot.md`): `npm run build:dotnet -- -p:RunAOTCompilation=true` cut steady-state CPU per frame from about 0.5 ms to 0.2-0.3 ms and removed the 9-18 ms spikes of the first ~20 s, at the cost of 55.7 s instead of 11.8 s to publish and 4.15 instead of 2.18 MB brotli (`_framework` 18.4 instead of 6.6 MB raw); frame pacing was unchanged. Use AOT once the game's CPU time per frame matters; delete `bin/Release` and `obj/Release` of the browser project when switching.
- **Device loss** makes submits, acquires, presents and fence waits return `VK_ERROR_DEVICE_LOST`; the page has to be reloaded.

## 5. Adding a shader

1. Write Vulkan GLSL 450 in `Game/Shaders/` (`.vert`, `.frag`, `.comp`; one entry point `main`) following `tools/README.md`, "Conventions": descriptor set = `@group`, binding = `@binding`; separate textures and samplers; std140 arrays as `vec4[N]`, no `mat2`/`matCx2`, no 64-bit types; push constants ≤ 128 bytes (set 3 is reserved); no `gl_PointSize`; specialization constants are allowed.
2. Both host projects list it as `<ShaderSource Include="..." />` and import `$(RepoRoot)tools/Shaders.targets` (the sample globs `Game/Shaders/*.vert` and `*.frag`; add `*.comp` if you need it). The build compiles it with `glslc --target-env=vulkan1.1 -O` to `obj/<cfg>/<tfm>/shaders/<name>.<ext>.spv`, embedded as `Shaders/<name>.<ext>.spv` on both hosts. Browser builds also translate every `.spv` to WGSL (`tools/build-shaders.mjs`) and embed `Shaders/manifest.json` (format: architecture.md section 6), which `Program.cs` hands to `WebIcd.LoadShaderManifest`.
3. Load it with `ShaderModuleExtensions.FromSpirv(device, EmbeddedAssets.Read("Shaders/<name>.<ext>.spv"), null)`.
4. Build the browser project: a shader the web pipeline cannot translate fails the build with `FAIL <name>: <step>: <reason>`. `cd tools && npm test` also compiles every fixture with the browser's own WGSL compiler.

## 6. Adding an asset

Put the file in `samples/<Game>/Assets/` (licence in `Assets/LICENSES.md`), add `<EmbeddedResource Include="../Assets/<file>" LogicalName="Assets/<file>" />` to both host projects and read it with `EmbeddedAssets.Read("Assets/<file>")`. Formats that work today: GLB (`ModelLoader`, Brutal.Gltf; `TRIANGLES` primitives, PNG textures in buffer views), PNG (`PngDecoder`), Ogg Vorbis (`IAudio.LoadAsync`; decoded whole, so keep sounds short; Ogg Opus is not supported on the desktop, NVorbis reports `Found OPUS bitstream`). Embedded resources are part of the download at boot, and the browser has no file system except an in-memory one: there is no loading from paths.

## 7. Tests and profile validation

```sh
dotnet test BrutalBrowser.slnx                                   # unit tests: bindings, ICD (recording bridge), host
cd tools && npm test                                             # shader pipeline fixtures incl. the browser WGSL check
node tests/Brutal.Vulkan.WebGpu.Smoke/test/run.mjs               # ICD smoke in headless Chromium (after npm ci in that folder)
node tests/Brutal.Browser.Smoke/test/run.mjs                     # host smoke (input, audio, frame loop)
cd samples/HelloMesh.Web && npm run build:dotnet && npm run test:e2e   # the game in Chromium (28 checks incl. a measured audio output level); test:e2e:chrome for Chrome; add --strict-autoplay (node test/e2e.mjs [--chrome] --strict-autoplay)
dotnet run --project samples/HelloMesh/HelloMesh.Desktop -c Release -- --frames 120 --validation --screenshot out/desktop.ppm
scripts/validate-web-profile.sh -- dotnet run --project samples/HelloMesh/HelloMesh.Desktop -c Release -- --frames 120
```

The last command runs the desktop build under the Khronos validation and profiles layers with the `VP_BRUTAL_web` profile and exits 0 only without VUIDs (`profiles/README.md`). Delete `samples/HelloMesh/HelloMesh.Browser/bin/Release/net10.0/publish` before `build:dotnet` when old fingerprinted files pile up.

## 8. Debugging in the browser

- **Fast loop:** `dotnet build -c Debug` in the browser project and `DOTNET_CONFIGURATION=Debug npm run dev` (untrimmed, with PDBs; the page reloads when the build finishes).
- **Console:** the markers `READY`, `FRAME <n>`, `CAMERA ...`, `AUDIO ...`, `PERF ...`, `ERROR <message>` (architecture.md section 12); the ICD's `ERROR [webgpu]` (call failed or command dropped), `WARN [webgpu]` (ignored or approximated feature, web-profile lint; once per function and reason) and `INFO [webgpu]`; the glue's `WEBGPU ERROR ...`. The page's red `#error` overlay shows `ERROR` lines, uncaught exceptions and device loss.
- **WebGPU errors** (validation errors, failed object creation) reach C# after the next submit as `ERROR [webgpu] WebGPU (vkQueueSubmit): ...`. Set `WebIcd.Options.DebugErrorScopes = true` to wrap every replay in error scopes, which ties each error to its submit (slower). `WebIcd.Options.ValidateProfile` (default on) runs the web-profile lint.
- **Debug messenger:** a `VkDebugUtilsMessengerEXT` (`VK_EXT_debug_utils`) receives every ICD message, as on the desktop.
- **`window.__brutal`** in DevTools: `readPixel(x, y)` (GPU readback of the canvas), `frameCount`, `lastError`, `audio.getState()`, `audio.getOutputLevel()` (RMS 0..1 of what the Web Audio graph sends to the output: 0 means silence, a suspended context or a muted master), `browser.size()`, `browser.queued()`, `browser.dropped()`, `browser.locked()`.
- Find Vulkan usage errors on the desktop first (`--validation`, then the profile script): it is faster and the messages are VUIDs.
