---
type: Recommendation
title: Windowing, input, main loop and audio for the BRUTAL browser PoC - choose the managed shim (strategy B)
description: Recommends a managed Brutal.Browser shim over JSImport for windowing, input and audio, an rAF-driven JSExport tick for the loop, no Asyncify or JSPI, and a decode-with-fallback Ogg strategy, with the shim's API sketch, milestones, risks and what would change the decision.
tags: [recommendation, windowing, input, audio, main-loop, brutal-browser, strategy-b, glfw, web-audio]
sources:
  - id: toolchain
    resource: /overview/toolchain-inventory-2026-10-16.md
    title: Toolchain inventory (no net10 wasm-tools workload, no emcc; browsers available)
  - id: brief
    resource: /overview/project-brief-and-constraints.md
    title: Project brief and acceptance criteria
  - id: glfw-options
    resource: /io-audio/emscripten-glfw-options.md
    title: Emscripten GLFW options (this bundle)
  - id: input-design
    resource: /io-audio/managed-browser-input-shim-design.md
    title: Managed browser input shim design (this bundle)
  - id: main-loop
    resource: /io-audio/main-loop-patterns-in-dotnet-wasm.md
    title: Main loop patterns in .NET wasm (this bundle)
  - id: audio-options
    resource: /io-audio/browser-audio-options.md
    title: Browser audio options (this bundle)
  - id: api-surface
    resource: /io-audio/brutal-glfw-and-openal-api-surface.md
    title: Brutal.Glfw and Brutal.OpenAl API surface (this bundle)
  - id: sdl3-alt
    resource: /io-audio/sdl3-emscripten-alternative.md
    title: SDL3 on Emscripten alternative (this bundle)
  - id: dotnet-targets
    resource: https://raw.githubusercontent.com/dotnet/runtime/release/10.0/src/mono/browser/build/BrowserWasmApp.targets
    title: dotnet/runtime release/10.0 BrowserWasmApp.targets
  - id: dotnet-emsc-version
    resource: https://raw.githubusercontent.com/dotnet/runtime/release/10.0/src/mono/browser/emscripten-version.txt
    title: dotnet/runtime release/10.0 emscripten-version.txt (3.1.56)
  - id: brutal-license
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/LICENSE.txt
    title: BRUTAL License v1.0
  - id: pongasoft-readme
    resource: https://raw.githubusercontent.com/pongasoft/emscripten-glfw/master/README.md
    title: contrib.glfw3 README
  - id: ksa-program
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA/Program.cs
    title: KSA KSA/Program.cs (reference only)
  - id: edge-test
    resource: "scope: headless Microsoft Edge 155 decodeAudioData of the PoC OGG, 2026-10-16 (session scratchpad r5/ogg-test)"
    title: Local decodeAudioData test in Edge 155
  - id: sib-fnptr
    resource: /dotnet-wasm/function-pointers-and-native-callbacks.md
    title: Function pointers, UnmanagedCallersOnly and native resolution on .NET 10 browser-wasm (sibling note)
  - id: sib-build-from-source
    resource: /brutal/building-from-source.md
    title: Building BRUTAL libraries from source, assembly identity, drop-in replacement (sibling note)
  - id: sib-windowing
    resource: /brutal/windowing-input-audio-apis.md
    title: Brutal.Glfw, Brutal.OpenAl and Brutal.Sdl API surfaces and browser replacement options (sibling note)
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:08:04Z }
status: draft
stale_after: 2027-01-31
---

# Decision

The PoC's acceptance criteria are keyboard and mouse input (for example spin the model with the mouse, pan and zoom with the arrow keys), playing `KSA_SFX_ROCKET_START_V2_01.ogg`, and a Vite 8 dev server.[^brief]

| Area | Choice | One-line reason |
| --- | --- | --- |
| Windowing, input, time, gamepad | **B: managed `Brutal.Browser` shim** exposing a source-compatible `Brutal.GlfwApi` subset over `[JSImport]` | No native relink: works with a plain `dotnet build` of a `browser-wasm` project today; full control of key/wheel/pointer-lock semantics; no 19-symbol (built-in) or 7-symbol (contrib) gap to stub[^glfw-options][^toolchain] |
| Main loop | **rAF calls a `[JSExport] Tick`**; `dotnet.create()` + `getAssemblyExports` (not `run()`); `FrameLoop` helper; async init with `await NextFrame()` | Only pattern that returns to the event loop every frame and matches WebGPU's per-frame canvas texture; no Asyncify or JSPI[^main-loop] |
| Audio | **B: managed Web Audio** (`Brutal.Browser.Audio`) with `decodeAudioData` first and an NVorbis fallback | PoC is "decode one Ogg and play it"; OpenAL-on-Emscripten needs the relink and still cannot decode Ogg[^audio-options] |
| Native Emscripten route (strategy A) | Deferred; if ever done, prefer `contrib.glfw3` over built-in `libglfw.js` | Needs the net10 `wasm-tools` workload (not installed), a loader shim, stubs, and is tied to Emscripten 3.1.56 (contrib 1.1.0)[^glfw-options][^dotnet-emsc-version] |
| SDL3 route | Not chosen | no ImGui backend, GLES-only video, needs Emscripten at least 4.0.10[^sdl3-alt] |

# Why B for windowing and input

- Prerequisites: the machine has the .NET 10 SDK but not the net10 `wasm-tools` workload, and no Emscripten; `NativeFileReference`, ports and AOT all need that workload, whereas JS interop works in the plain browser-wasm runtime pack.[^toolchain] Per the `release/10.0` targets, native inputs enter via `NativeFileReference`/`EmccExtraLDFlags`, and a port that must be compiled needs a writable `WasmCachePath` and network access.[^dotnet-targets]
- Version coupling: .NET 10 pins Emscripten 3.1.56, which bundles contrib.glfw3 1.1.0; the current port (3.5.1.20260809, Emscripten 6.0.7) adds custom cursors, clipboard, touch, flicker-free resize and more.[^dotnet-emsc-version][^pongasoft-readme]
- BRUTAL fidelity: `Brutal.Glfw`'s static constructor loads the native library; `GlfwWindow`'s constructor registers fork-only callbacks (`ScrollDetail`, trackpad zoom/rotate) that neither Emscripten implementation has; `Glfw.CreateWindowSurface`/`GetRequiredInstanceExtensions` have to be redirected into the Vulkan-on-WebGPU layer in any case.[^api-surface][^glfw-options]
- Strategy A would not even keep Brutal.Glfw unmodified: its callbacks are delegates passed through LibraryImport, and the sibling analysis finds that `Marshal.GetFunctionPointerForDelegate` is unusable on Mono's interpreter (reverse thunks exist only for `[UnmanagedCallersOnly]` methods after a relink), so a source fork with UCO callbacks plus the relink would be needed anyway.[^sib-fnptr]
- The reference game's input needs are modest (window events, `GetKey`, `GetGamepadState`, `Glfw.Time`, cursor modes, one window) and the BRUTAL ImGui GLFW backend uses the same small set, so a compatible subset is tractable.[^api-surface][^ksa-program]
- Control over semantics that matter to a 3D PoC: pointer lock with `unadjustedMovement`, wheel normalisation, `KeyboardEvent.code` mapping with left/right modifiers, preventDefault policy, DPI and resize timing.[^input-design]

Form of the compatibility layer (Option 2 in the API note): an assembly named `Brutal.Glfw` (the NuGet's simple name; BRUTAL's NuGet assemblies are not strong-named and are all AssemblyVersion 1.0.0.0, so prebuilt consumers such as `Ahwoo.BRUTAL.ImGui.Backends` bind to a same-named replacement) with `namespace Brutal.GlfwApi` exposing `Glfw`, `GlfwWindow`, `GlfwMonitor`, `GlfwCursor`, and BRUTAL's existing enums, constants and structs copied verbatim (they are pure managed), implemented on top of the `Brutal.Browser` core. The sibling BRUTAL analysis reaches the same verdict (replacement assembly named `Brutal.Glfw`).[^api-surface][^sib-build-from-source][^sib-windowing] Open packaging detail: NuGet still pulls the real `Ahwoo.BRUTAL.Glfw` transitively, so the browser build needs a project reference plus package exclusion or a source build of the dependent BRUTAL libraries.[^sib-build-from-source] A swap of only `Glfw.PInvoke` inside a full source fork (Option 1) is the alternative if exact wrapper behaviour is wanted. Licensing: BRUTAL License v1.0 permits modification and redistribution but requires the notice, the credits-screen entry, the Brutal logo before gameplay and `NOTICE.txt` in any distributed product that incorporates the software; confirm with Ahwoo Limited before publishing a fork. Not legal advice.[^brutal-license]

# Why B for audio

- The PoC asset is a 3.4 s stereo 48 kHz Ogg Vorbis file. `decodeAudioData` handled it in Edge/Chromium 155 on this Mac (2 ch, 48 kHz, 163,520 frames). Firefox is expected to work and Safari 18.4+/macOS 15.4+/iOS 18.4+ is likely but unverified; older Safari cannot decode Ogg, so the shim falls back to managed NVorbis (MIT, no unsafe code or P/Invoke).[^edge-test][^audio-options]
- Emscripten's OpenAL needs the same relink and stubs (35 of Brutal.OpenAl's 126 core entry points are missing: the 33 EFX functions and `alGetProcAddress`/`alcGetProcAddress`, the latter being what every extension wrapper needs), still requires PCM from the app, and schedules audio from a 25 ms main-thread timer.[^audio-options]
- KSA itself uses FMOD, so BRUTAL-OpenAL compatibility is not needed for the reference title; an OpenAL-shaped or FMOD-shaped adapter can be layered later over the same Web Audio engine.[^audio-options]

# API sketch of the managed shim

Two assemblies (net10.0, `browser-wasm`): `Brutal.Browser` (host, canvas, audio, JS interop core) and `Brutal.Glfw` (the source-compatible `Brutal.GlfwApi` layer, built on `Brutal.Browser`). Three public namespaces in total.

```csharp
namespace Brutal.Browser
{
    public static class BrowserHost
    {
        public static Task StartAsync(BrowserHostOptions options);   // wires the JS module, canvas selector, listeners
        public static void RunLoop(Func<double, bool> frame);        // browser: stores delegate, rAF calls Tick; desktop: while loop
        public static Task NextFrame();                              // completes at the start of the next Tick
        public static void Exit(int exitCode = 0);                   // stops the rAF chain
        public static double Now { get; }                            // seconds, performance.now based
        public static bool IsVisible { get; }
        public static event Action<bool>? VisibilityChanged;
        [JSExport] public static bool Tick(double timestampMs);       // called by requestAnimationFrame
    }

    public static class BrowserCanvas
    {
        public static int2 CssSize { get; }
        public static int2 PixelSize { get; }                        // device pixels, drives swapchain extent
        public static float DevicePixelRatio { get; }
        public static nint SurfaceHandle { get; }                    // key the Vulkan layer uses to bind a VkSurfaceKHR to the canvas
        public static void RequestFullscreen();
        public static event Action? PointerLockLost;                 // Escape pressed while locked
    }
}

namespace Brutal.GlfwApi   // source-compatible subset
{
    public static class Glfw
    {
        public static bool Init(); public static void Terminate();
        public static void WindowHint(GlfwWindowHint hint, int value);
        public static GlfwWindow CreateWindow(in GlfwWindow.CreateInfo info);
        public static void PollEvents();                              // drains the JS queue, raises events, rethrows callback exceptions
        public static double Time { get; set; }
        public static bool GetGamepadState(int id, out GlfwGamepadState state);
        public static string? Clipboard { get; set; }
        public static unsafe byte** GetRequiredInstanceExtensions(out uint count);     // "VK_KHR_surface", "VK_EXT_headless_surface"
        public static int CreateWindowSurface(nint instance, GlfwWindow window, out ulong surface); // delegates to the Vulkan layer
    }
    public sealed class GlfwWindow : IDisposable
    {
        public int2 Size { get; set; } public int2 FramebufferSize { get; } public float2 ContentScale { get; }
        public double2 CursorPos { get; set; } public bool ShouldClose { get; set; }
        public GlfwKeyAction GetKey(GlfwKey key); public GlfwButtonAction GetMouseButton(GlfwMouseButton b);
        public void SetInputMode(GlfwInputMode mode, int value);      // Cursor: Normal/Hidden/Disabled -> pointer lock
        public event GlfwKeyCallback? OnKey; public event GlfwMouseButtonCallback? OnMouseButton;
        public event GlfwCursorPosCallback? OnCursorPos; public event GlfwScrollCallback? OnScroll;
        public event GlfwFramebufferSizeCallback? OnFramebufferSize; public event GlfwWindowFocusCallback? OnFocus;
        // ...remaining BRUTAL members as no-ops or NotSupported until needed
    }
}

namespace Brutal.Browser.Audio
{
    public sealed class AudioEngine : IDisposable
    {
        public static AudioEngine Create(AudioEngineOptions? options = null);   // call inside a user gesture, or call ResumeAsync later
        public bool IsRunning { get; }
        public Task<bool> ResumeAsync();                                        // resume() from a user-activation handler
        public Task<Sound> LoadAsync(string url);                               // fetch + decodeAudioData, NVorbis fallback
        public Sound Decode(ReadOnlyMemory<byte> fileBytes, string name);
        public AudioListener Listener { get; }
        public float MasterGain { get; set; }
    }
    public sealed class Sound : IDisposable
    {
        public double Duration { get; } public int Channels { get; } public int SampleRate { get; }
        public Voice Play(in PlayOptions options = default);                    // Gain, Pitch, Loop, Position?, RefDistance, MaxDistance
    }
    public sealed class Voice { public bool IsPlaying { get; } public void Stop(); public float Gain { set; } public float3 Position { set; } public event Action? Ended; }
    public sealed class AudioListener { public float3 Position { set; } public float3 Forward { set; } public float3 Up { set; } }
    public interface IPcmDecoder { bool TryDecode(ReadOnlySpan<byte> file, out float[] interleaved, out int channels, out int sampleRate); }
}
```

JS counterpart modules: `brutal-browser` (input queue, canvas, clipboard, time) and `brutal-browser-audio` (AudioContext, buffers, voices).

# PoC milestones (input, loop, audio only)

1. Loop and canvas: `dotnet.create()` + rAF `Tick`, `BrowserCanvas` with ResizeObserver and DPR, a "click to start" overlay that also creates/resumes the `AudioContext` and (optionally) requests pointer lock; acceptance: steady dt, correct pixel size after resize and zoom.
2. Input: event queue, key/mouse/wheel mapping tables, pointer lock, preventDefault policy, gamepad polling; acceptance: orbit the model with the mouse drag or locked look, pan/zoom with arrow keys and wheel, no page scroll, no stuck keys after blur.[^input-design]
3. Audio: play `KSA_SFX_ROCKET_START_V2_01.ogg` from the overlay click and on a key press; `decodeAudioData` primary path, NVorbis fallback behind a feature test; acceptance: audible playback in Chromium (verified decode), Firefox and Safari checked manually.
4. Compatibility hardening: add the remaining `Brutal.GlfwApi` members used by the BRUTAL ImGui backend, then evaluate an `Brutal.OpenAlApi` adapter.

# Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Source-compatible subset drifts from upstream `Brutal.Glfw` (monthly `2026.10.x` releases) | Copy enums/constants verbatim, pin to a BRUTAL commit, add a script that diffs the public surface |
| `Span<double>`/`MemoryView` JSImport not usable in .NET 10 | Return a `double[]` copy per frame (small) |
| Safari cannot decode Ogg | feature test + NVorbis; test the actual asset on Safari |
| Pointer lock exits on Escape and cannot be re-requested immediately | treat as user intent; click to re-lock |
| Nested synchronous frame pumps in game startup (`Loading.OnFrame`) | convert to async slices with `NextFrame()` before porting the full game |
| Blocking GPU waits and thread pools in the game | owned by the webgpu and dotnet-wasm notes; flagged in [main loop patterns](/io-audio/main-loop-patterns-in-dotnet-wasm.md) |
| Licence obligations for a derivative of BRUTAL | keep notice, credits, logo, `NOTICE.txt`; ask Ahwoo Limited before publishing |

# What would change this decision

- The net10 `wasm-tools` workload gets installed and `NativeLibrary.Load` of a statically linked module is shown to work on Mono wasm: strategy A with `contrib.glfw3` (after stubbing the Vulkan and fork functions) becomes cheaper for GLFW, but still needs Emscripten-version alignment.
- .NET 11 (Emscripten 6.0.2 on `main`) or the CoreCLR-on-wasm runtime becomes the target: re-evaluate A, JSPI and the port versions.[^dotnet-emsc-version]
- A target game relies on OpenAL extensions or FMOD features beyond what a Web Audio facade can express: reconsider FMOD HTML5 (proprietary) or an OpenAL compatibility adapter.[^audio-options]

# Cross-bundle dependencies

- Vulkan surface: [Emscripten GLFW options](/io-audio/emscripten-glfw-options.md) proposes advertising `VK_KHR_surface` and `VK_EXT_headless_surface` and creating the surface inside the managed `Glfw.CreateWindowSurface`; the Vulkan-on-WebGPU layer must expose a way to bind a surface to `BrowserCanvas.SurfaceHandle` (see `/webgpu/` and [/brutal/vulkan-loader-and-proc-addr-contract.md](/brutal/vulkan-loader-and-proc-addr-contract.md)).
- Native callbacks and `NativeLibrary.Load` on wasm: [/dotnet-wasm/function-pointers-and-native-callbacks.md](/dotnet-wasm/function-pointers-and-native-callbacks.md) (only needed for strategy A).
- Canvas texture lifetime and fence waits: the webgpu notes.

## Open questions

- UNVERIFIED: Safari/Firefox decode of the PoC file; NVorbis speed on the Mono interpreter.
- UNVERIFIED: how .NET SDK conflict resolution treats a project-referenced `Brutal.Glfw` versus the transitive NuGet one with the same simple name and AssemblyVersion (open in the sibling build note).
- Does the PoC include ImGui (it would exercise the GLFW backend path)?
- Does RocketWerkz/Ahwoo want the browser shim upstream?

[^glfw-options]: Emscripten GLFW options (this bundle)
[^brief]: Project brief and acceptance criteria
[^toolchain]: Toolchain inventory (no net10 wasm-tools workload, no emcc; browsers available)
[^main-loop]: Main loop patterns in .NET wasm (this bundle)
[^audio-options]: Browser audio options (this bundle)
[^api-surface]: Brutal.Glfw and Brutal.OpenAl API surface (this bundle)
[^sdl3-alt]: SDL3 on Emscripten alternative (this bundle)
[^input-design]: Managed browser input shim design (this bundle)
[^dotnet-targets]: dotnet/runtime release/10.0 BrowserWasmApp.targets
[^dotnet-emsc-version]: dotnet/runtime release/10.0 emscripten-version.txt (3.1.56)
[^brutal-license]: BRUTAL License v1.0
[^pongasoft-readme]: contrib.glfw3 README
[^ksa-program]: KSA KSA/Program.cs (reference only)
[^edge-test]: Local decodeAudioData test in Edge 155
[^sib-fnptr]: Function pointers, UnmanagedCallersOnly and native resolution on .NET 10 browser-wasm (sibling note)
[^sib-build-from-source]: Building BRUTAL libraries from source, assembly identity, drop-in replacement (sibling note)
[^sib-windowing]: Brutal.Glfw, Brutal.OpenAl and Brutal.Sdl API surfaces and browser replacement options (sibling note)
