---
type: Code Analysis
title: Brutal.Glfw, Brutal.OpenAl and Brutal.Sdl(.Mixer) — API surfaces, loaders and native payloads, with browser replacement options
description: The exact BRUTAL windowing/input/audio APIs a PoC would use (Glfw window + callbacks + Vulkan surface; OpenAL device/context/buffer/source), how each package locates its native library, what native versions ship per RID, and why each one is RED for browser-wasm and how to replace it.
tags: [brutal, glfw, openal, sdl3, sdl-mixer, input, audio, windowing, browser, replacement]
sources:
  - id: glfw-cs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw/Glfw.cs
    title: Brutal.Glfw/Glfw.cs (Dll const, ExceptionAggregator, Log category)
  - id: glfw-init
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw/Manual/Libraries/Glfw.Initialization.cs
    title: Glfw static ctor (native load + resolver), Init/Terminate
  - id: glfw-window
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw/Manual/Libraries/Glfw.Window.cs
    title: Glfw.CreateWindow / PollEvents / GlfwWindow class and events
  - id: glfw-window-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw/Generated/Libraries/Glfw.Window.gen.cs
    title: Generated window wrappers (WindowHint, GetKey, GetMouseButton, SetInputMode)
  - id: glfw-input
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw/Manual/Libraries/Glfw.Input.cs
    title: Input delegates, Glfw.Time
  - id: glfw-vulkan
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw/Manual/Libraries/Glfw.Vulkan.cs
    title: Glfw.CreateWindowSurface
  - id: glfw-vulkan-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw/Generated/Libraries/Glfw.Vulkan.gen.cs
    title: GetRequiredInstanceExtensions / GetPhysicalDevicePresentationSupport
  - id: glfw-pinvoke
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw/Generated/Glfw.PInvoke.gen.cs
    title: 148 generated GLFW LibraryImports (+6 manual in Manual/Glfw.PInvoke.cs), each wrapper calls Log.Trace()
  - id: glfw-delegates
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw/Generated/Glfw.Delegates.gen.cs
    title: "[UnmanagedFunctionPointer] callback delegate types"
  - id: glfw-native
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/nupkg-extract/ahwoo.brutal.glfw/runtimes/osx-arm64/native/libglfw.3.5.dylib
    title: libglfw.3.5.dylib (version string 3.5.1; trackpad exports)
  - id: openal-loader
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.OpenAl/Manual/NativeLoader.cs
    title: Brutal.OpenAl NativeLoader ([ModuleInitializer] + Platform.CurrentFamily)
  - id: openal-alc
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.OpenAl/Manual/Alc.Wrapper.cs
    title: Alc.OpenDevice and device enumeration
  - id: openal-device
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.OpenAl/Manual/Device.cs
    title: OpenAL Device class (CreateContext, Pause/Resume, Reset)
  - id: openal-context
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.OpenAl/Manual/Context.cs
    title: OpenAL Context class (MakeCurrent, MakeThreadCurrent)
  - id: openal-objnames
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.OpenAl/Generated/Al/Al.ObjectNames.gen.cs
    title: Al.GenBuffer(s)/GenSource(s)/Delete*
  - id: openal-source
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.OpenAl/Generated/Al/AlSource.Properties.gen.cs
    title: AlSource properties (Gain, Pitch, Position, Looping, Buffer, State, ...)
  - id: openal-buffer
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.OpenAl/Manual/AlBuffer.cs
    title: AlBuffer.SetData
  - id: openal-ext
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.OpenAl/Generated/Al/Al.Ext.gen.cs
    title: Extension entry points resolved via alcGetProcAddress
  - id: openal-cb
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.OpenAl/Manual/Callbacks.Internal.cs
    title: UnmanagedCallersOnly callback trampolines
  - id: platform-cs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Core/Platform.cs
    title: Brutal.Core Platform (throws PlatformNotSupportedException outside Windows/Linux/macOS)
  - id: sdl-pinvoke
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Sdl/Manual/Sdl.PInvoke.cs
    title: Brutal.Sdl lazy DllImportResolver with per-OS candidate names
  - id: sdl-vulkan
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Sdl/Manual/Libraries/Sdl.Vulkan.cs
    title: SDL_Vulkan_GetInstanceExtensions / SDL_Vulkan_CreateSurface
  - id: sdlmixer-cs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Sdl.Mixer/SdlMixer.cs
    title: Brutal.Sdl.Mixer (SDL3_mixer, MIX_* API)
  - id: sdl-readme
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/nupkg-extract/ahwoo.brutal.sdl/README.md
    title: Ahwoo.BRUTAL.Sdl README (SDL 3.4.12 vendored)
  - id: imgui-glfw
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ImGui.Backends/PlatformBackends/GlfwBackend.GlfwInterop.cs
    title: ImGui GLFW backend's own DllImports (glfw / glfw3)
  - id: inventory
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/research/brutal/nuget-package-inventory.md
    title: NuGet package inventory (native payload per RID)
  - id: brutal-readme
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/README.md
    title: BRUTAL README "Your first window" sample
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:16:59Z }
status: draft
---

# Verdict

| Package | Native | Loader behaviour on browser-wasm | Rating | Recommended browser strategy |
|---|---|---|---|---|
| `Ahwoo.BRUTAL.Glfw` 2026.10.4 | GLFW **3.5.1** (non-upstream: adds trackpad and scroll-detail callbacks) for win-x64, linux-x64 and osx-arm64[^inventory][^glfw-native] | `static Glfw()` calls `NativeLibrary.Load("glfw")` and throws[^glfw-init] | **RED** | API-compatible **replacement assembly** named `Brutal.Glfw` (namespace `Brutal.GlfwApi`) implemented on DOM events through `[JSImport]` |
| `Ahwoo.BRUTAL.OpenAl` 2026.10.2 | openal-soft **1.25.2**[^inventory] | `[ModuleInitializer]` installs a resolver that calls `Platform.CurrentFamily`, which throws `PlatformNotSupportedException` on the first AL call[^openal-loader][^platform-cs] | **RED** | API-compatible replacement `Brutal.OpenAl` over WebAudio, **or** Emscripten's built-in OpenAL (JS) statically linked (UNVERIFIED with .NET wasm) |
| `Ahwoo.BRUTAL.Sdl` / `.Sdl.Mixer` 2026.10.2 | SDL **3.4.12**, SDL3_mixer (soname 0.2.4)[^sdl-readme][^inventory] | lazy resolver; Linux-style candidates `libSDL3.so.0`/`libSDL3.so`/`SDL3` fail[^sdl-pinvoke] | **RED** | Alternative path: SDL3 has an upstream Emscripten port, so an Emscripten-built SDL3 + SDL3_mixer statically linked into the .NET wasm runtime is conceivable (UNVERIFIED; heavy) |
| `Ahwoo.BRUTAL.Fmod` 2026.10.3 | none shipped (bring-your-own FMOD) | — | **RED** | FMOD has an HTML5/wasm SDK but licensing and integration are out of PoC scope |

None of these libraries decodes **Ogg Vorbis** on its own. OpenAL takes raw PCM through `AlBuffer.SetData`.[^openal-buffer] SDL3_mixer and FMOD decode internally. The PoC's `.ogg` asset therefore needs a decoder: browser `decodeAudioData`, or a managed decoder (see `/io-audio/`).

# Brutal.Glfw

## Loader and binding mechanics

* `const string Dll = "glfw"` (`Glfw.cs:52`).[^glfw-cs] The `static Glfw()` constructor (`Glfw.Initialization.cs:35–103`) calls `NativeLibrary.Load(OperatingSystem.IsWindows() ? "glfw3" : "glfw", typeof(Glfw).Assembly, DllImportSearchPath.AssemblyDirectory)`, then `NativeLibrary.SetDllImportResolver(...)`. That resolver returns the handle for every name and asserts in Debug builds that the name is `"glfw"`. The constructor then builds the monitor, joystick and error delegates and calls `PInvoke.SetErrorCallback`.[^glfw-init]
* **154 native entry points**: 148 `[LibraryImport(Dll, EntryPoint = "glfw…")]` in `Generated/Glfw.PInvoke.gen.cs` and 6 in `Manual/Glfw.PInvoke.cs`. **Every wrapper calls `Log.Trace()` before the native call**, for example `SetKeyCallback` at `Glfw.PInvoke.gen.cs:1013–1017`.[^glfw-pinvoke]
* Callbacks are **managed delegates marshalled to native function pointers**. The delegate types are `[UnmanagedFunctionPointer(CallingConvention.Cdecl)]` in `Glfw.Delegates.gen.cs`, and LibraryImport parameters are delegate-typed; `InitAllocator` uses `Marshal.GetFunctionPointerForDelegate`. `GlfwWindow` keeps the lambdas alive in a `GCHandle[] _callbackHandles` and forwards to C# events, catching exceptions into `Glfw.ExceptionAggregator`, which rethrows from `PollEvents()`.[^glfw-delegates][^glfw-window][^glfw-init]
* `Brutal.ImGui.Backends`' GLFW platform backend has its **own** `[LibraryImport("glfw")]`/`("glfw3")` declarations, plus `user32.dll` on Windows. A replacement must cover those too, or the ImGui GLFW backend must be replaced.[^imgui-glfw]

## API surface the PoC needs

(`Brutal.GlfwApi` namespace; signatures as in source)

| Purpose | API | File |
|---|---|---|
| init/shutdown | `bool Glfw.Init()`, `void Glfw.Terminate()`, `event GlfwErrorCallback Glfw.ErrorCallback`, `Glfw.VersionString` | Glfw.Initialization.cs:186, 218, 122, 263[^glfw-init] |
| window hints | `Glfw.WindowHint(GlfwWindowHint hint, int value)`, e.g. `WindowHint(GlfwWindowHint.ClientApi, 0)` for no GL context; `Glfw.DefaultWindowHints()` | Glfw.Window.gen.cs:53, 25[^glfw-window-gen] |
| create window | `GlfwWindow Glfw.CreateWindow(in GlfwWindow.CreateInfo { required string Title; required int2 Size; GlfwMonitor Monitor; GlfwWindow? Share })`; `GlfwWindow` is a `sealed` class with an **internal** ctor and `[NativeMarshalling(typeof(GlfwWindowMarshaller))]`, implementing `IDisposable` | Glfw.Window.cs:241, 285–310[^glfw-window] |
| loop | `bool window.ShouldClose { get; set; }`, `Glfw.PollEvents()` (also rethrows callback exceptions), `Glfw.WaitEvents()`, `WaitEventsTimeout(double)`, `PostEmptyEvent()` | Glfw.Window.cs:970, 274; Glfw.Window.gen.cs:99, 146, 164 |
| sizes | `int2 window.Size`, `int2 window.FramebufferSize` (cached from the callback), `float2 window.ContentScale`, `float window.AspectRatio` | Glfw.Window.cs:720, 759, 818, 788 |
| input polling | `GlfwKeyAction window.GetKey(GlfwKey)`, `GlfwButtonAction window.GetMouseButton(GlfwMouseButton)`, `double2 window.CursorPos`, `window.SetInputMode(GlfwInputMode, int)` (cursor capture), `Glfw.RawMotionSupported()` | Glfw.Window.gen.cs:471, 494, 433; Glfw.Window.cs:854 |
| input events | `window.OnKey(GlfwWindow, GlfwKey, int scancode, GlfwKeyAction, GlfwModifier)`, `OnChar(window, uint codepoint)`, `OnMouseButton(window, GlfwMouseButton, GlfwButtonAction, GlfwModifier)`, `OnCursorPos(window, double2)`, `OnCursorEnter`, `OnScroll(window, double2 offset)`, `OnScrollDetail`, `OnTrackpadZoom/Rotate`, `OnDrop(window, int, string[])` | Glfw.Window.cs:1336–1511; Glfw.Input.cs[^glfw-input] |
| window events | `OnFramebufferSize(window, int2)`, `OnSize`, `OnClose`, `OnFocus`, `OnIconify`, `OnMaximize`, `OnContentScale`, `OnRefresh`, `OnPosition` | Glfw.Window.cs:1408–1619 |
| time | `double Glfw.Time { get; set; }` (`glfwGetTime`), `ulong Glfw.GetTimerValue()`, `GetTimerFrequency()` | Glfw.Input.cs:234; Glfw.Input.gen.cs:439, 456 |
| Vulkan | `byte** Glfw.GetRequiredInstanceExtensions(out uint count)`; `bool Glfw.VulkanSupported()`; `bool Glfw.GetPhysicalDevicePresentationSupport(nint instance, nint physicalDevice, int family)`; `int Glfw.CreateWindowSurface(nint instance, GlfwWindow window, out ulong surface)` returns a `VkResult` as an int, hard-codes a null allocator, and runs `glfwCreateWindowSurface` **inside native GLFW** | Glfw.Vulkan.gen.cs; Glfw.Vulkan.cs:55–62[^glfw-vulkan-gen][^glfw-vulkan] |
| misc | `Glfw.GetClipboard/SetClipboard`, cursors (`Glfw.CreateCursor`), monitors (`GlfwMonitor`), gamepads (`Glfw.GetGamepadState`, joystick APIs) | Glfw.Input.cs, Glfw.Cursor.cs, Glfw.Monitor.cs |

The README's "first window" sample uses exactly `Glfw.Init()`, `Glfw.WindowHint(GlfwWindowHint.ClientApi, 0)`, `Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title, Size = new int2(1280, 720) })`, `while (!window.ShouldClose) Glfw.PollEvents()` and `Glfw.Terminate()`.[^brutal-readme]

## Why GLFW is RED in the browser, and the replacement design

1. `static Glfw()` calls `NativeLibrary.Load`. Using enum types such as `GlfwKey` is harmless, but the first access to any member of the static `Glfw` class runs the constructor and throws on browser. `GlfwWindow` creation goes through `Glfw.PInvoke`, so it triggers the constructor as well.
2. Even with an Emscripten-compiled GLFW (Emscripten ships `contrib.glfw3`, a GLFW 3.4-style API on a canvas) statically linked into the wasm module: (a) callbacks go through `Marshal.GetFunctionPointerForDelegate` on closure delegates, and Mono-wasm reverse P/Invoke needs build-time thunks, generally only for static `[UnmanagedCallersOnly]` methods (UNVERIFIED for .NET 10; see `/dotnet-wasm/`); (b) the fork-only exports (`glfwSetScrollDetailCallback`, `glfwSetTrackpadZoomCallback`, `glfwSetTrackpadRotateCallback`) would be missing; (c) `glfwCreateWindowSurface` cannot create a Vulkan surface on the web.
3. **Recommendation: write a browser `Brutal.Glfw.dll` with the same assembly name and the same public surface** (`Brutal.GlfwApi.Glfw`, `GlfwWindow`, `GlfwWindow.CreateInfo`, the enums `GlfwKey`, `GlfwKeyAction`, `GlfwMouseButton`, `GlfwButtonAction`, `GlfwModifier`, `GlfwWindowHint`, `GlfwInputMode`, the delegate types and `GlfwMonitor`). Implement it on `<canvas>` DOM events through `[JSImport]`/`[JSExport]`. Keep `int2`/`double2` from `Brutal.Numerics`. Implement `CreateWindowSurface` by calling the C# Vulkan implementation's canvas-surface factory. Game code then compiles unchanged, and assemblies compiled against NuGet `Brutal.Glfw` 1.0.0.0, such as `Brutal.ImGui.Backends`, bind by simple name because nothing is strong-named; see [/brutal/building-from-source.md](/brutal/building-from-source.md). `PollEvents()` becomes "drain the queued DOM events". The browser drives frames with `requestAnimationFrame`, so a `while (!ShouldClose)` loop must become a per-frame callback (see `/dotnet-wasm/`).

# Brutal.OpenAl

## Loader and binding mechanics

* `internal const string Dll = "openal"` in both `Al` and `Alc` (`Al.cs:6`, `Alc.cs:6`). There are 126 `EntryPoint`s.
* `NativeLoader.Register()` is a **`[ModuleInitializer]`** (`NativeLoader.cs:16–20`). It runs when the assembly loads and only installs `NativeLibrary.SetDllImportResolver`. The resolver (`:22–39`) maps `"openal"` to `OpenAL32.dll`, `libopenal.1.dylib` or `libopenal.so.1` by **`Platform.CurrentFamily`**, then calls `NativeLibrary.Load(file, assembly, AssemblyDirectory)`.[^openal-loader] On browser, `Platform`'s static ctor throws `PlatformNotSupportedException` (see [/brutal/managed-libraries-portability-audit.md](/brutal/managed-libraries-portability-audit.md)).[^platform-cs]
* Extension functions (`alBufferDataStatic`, `alSourcePlayAtTimeSOFT`, EFX, …) are fetched with `alcGetProcAddress` into `delegate* unmanaged[Cdecl]` statics. A missing one throws `AlExtensionException` when called.[^openal-ext] Native→managed callbacks (buffer callbacks, events, debug) are static **`[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]`** methods, which is the wasm-friendly form (`Callbacks.Internal.cs:64, 93, 128`; `AlDebug.Internal.cs:37`).[^openal-cb]

## API surface the PoC needs

(`Brutal.OpenAlApi` namespace)

| Purpose | API | File |
|---|---|---|
| open device | `Device Alc.OpenDevice(string? name = null)`; enumeration with `Alc.GetDeviceSpecifiers()`, `GetAllDeviceSpecifiers()`, `GetDefaultDeviceSpecifier()` | Alc.Wrapper.cs:5, 59–67[^openal-alc] |
| context | `Context device.CreateContext(Context.CreateInfo info = default)` (`ReadOnlySpan<(AlcContextAttribute, int)> Attributes`); `context.MakeCurrent()` / `Context.MakeCurrent(ctx)`; `MakeThreadCurrent`; `Dispose()` | Device.cs:144; Context.cs:5, 53–121, 136[^openal-device][^openal-context] |
| device control | `device.Pause()`/`Resume()` (`ALC_SOFT_pause_device`), `Reset(attrs)`, `Reopen(...)`, `GetError()`/`ThrowIfError()`, `Dispose()` | Device.cs:132–263 |
| buffers | `AlBuffer Al.GenBuffer()`, `AlBuffer[] Al.GenBuffers(int)`, `Al.DeleteBuffers(...)`, `buffer.SetData(AlFormat format, ReadOnlySpan<byte> pcm, int sampleRate)` with `AlFormat.Mono8/Mono16/Stereo8/Stereo16` (+ float formats via extension) | Al.ObjectNames.gen.cs:13–49; AlBuffer.cs:5[^openal-objnames][^openal-buffer] |
| sources | `AlSource Al.GenSource()`, `GenSources(n)`; properties `Buffer`, `Gain`, `Pitch`, `Looping`, `Position`, `Velocity`, `Direction`, `SourceRelative`, `ReferenceDistance`, `RolloffFactor`, `MaxDistance`, cone parameters, `SecOffset`, `State`, `BuffersQueued`, `BuffersProcessed`; verbs `Play()`, `Pause()`, `Stop()`, `Rewind()`; streaming `QueueBuffers`/`UnqueueBuffers` | AlSource.Properties.gen.cs:30–575; AlSource.Verbs.gen.cs:12–27; AlSource.cs:5–15[^openal-source] |
| listener | `Al.Listener.Position`, `Velocity`, `Gain`, `(float3 At, float3 Up) Al.Listener.Orientation` | Al.Listener.cs:7; Al.Listener.Properties.gen.cs |
| batching | `Al.DeferUpdatesScope()`, `Al.PlaySourcesAt(...)` (SOFT extensions) | Al.Wrapper.cs:36–79 |

## Browser options

* **Replacement assembly (recommended for the PoC):** a `Brutal.OpenAl.dll` with the same public types (`Alc`, `Device`, `Context`, `Al`, `AlBuffer`, `AlSource`, `Al.Listener`, `AlFormat`, `AlSourceState`) mapped to WebAudio: `AudioBuffer` for each `AlBuffer`, `AudioBufferSourceNode` + `PannerNode`/`GainNode` for each `AlSource`, `AudioListener`. Browsers require a user gesture before audio starts; see `/io-audio/`.
* **Emscripten OpenAL:** Emscripten implements AL 1.1 and ALC in JavaScript on WebAudio. Linking it into the .NET wasm runtime and keeping `Brutal.OpenAl` unchanged requires that (a) the `"openal"` P/Invokes bind statically at build time, (b) the `[ModuleInitializer]` resolver never runs `Platform` (only if the runtime does not consult the resolver for statically linked libraries; UNVERIFIED), and (c) code avoids SOFT/EFX extensions. Lower confidence; see `/io-audio/`.

# Brutal.Sdl / Brutal.Sdl.Mixer (alternative stack)

* `Brutal.Sdl` binds **SDL 3.4.12**, the full generated surface: 1,195 imports and about 1,194 `Log.Trace()` call sites. Resolution is lazy: the `static PInvoke()` constructor installs a resolver that tries per-OS candidate names (`SDL3.dll`; `libSDL3.dylib`; else `libSDL3.so.0`, `libSDL3.so`, `SDL3`) with `NativeLibrary.Load`, under a `Lock`.[^sdl-pinvoke] It exposes `Sdl.PInvoke.Vulkan_GetInstanceExtensions(uint*)` and `Vulkan_CreateSurface(SdlWindow, nint instance, nint allocator, nint* surface)`.[^sdl-vulkan]
* `Brutal.Sdl.Mixer` binds the SDL3_mixer **`MIX_*`** API: `MIX_LoadAudio(mixer, path, predecode)`, `MIX_LoadAudio_IO`, … SDL3_mixer decodes Ogg/MP3/WAV internally.[^sdlmixer-cs]
* SDL3 officially supports Emscripten (canvas windowing, input and WebAudio audio). That makes an **SDL-based browser path** possible in principle: build SDL3 + SDL3_mixer with Emscripten and link them into the .NET wasm native runtime. Blockers are similar to GLFW: the resolver, the event loop (`emscripten_set_main_loop` vs a blocking loop), callback marshalling, and there is no Vulkan on the web. KSA uses GLFW, not SDL, so switching windowing libraries changes game code.

# Open questions

* UNVERIFIED: whether `Marshal.GetFunctionPointerForDelegate` on closure delegates (Brutal.Glfw callbacks) works on .NET 10 Mono browser-wasm with a native relink. If it does not, a statically linked Emscripten GLFW is ruled out even apart from the API-version gap.
* UNVERIFIED: whether Mono browser-wasm consults `NativeLibrary.SetDllImportResolver` before its static P/Invoke table. This decides whether Brutal.OpenAl's `[ModuleInitializer]` resolver, and its `Platform` throw, gets in the way of a statically linked `openal`.

[^glfw-cs]: Brutal.Glfw/Glfw.cs
[^glfw-init]: Glfw.Initialization.cs
[^glfw-window]: Glfw.Window.cs
[^glfw-window-gen]: Glfw.Window.gen.cs
[^glfw-input]: Glfw.Input.cs
[^glfw-vulkan]: Glfw.Vulkan.cs
[^glfw-vulkan-gen]: Glfw.Vulkan.gen.cs
[^glfw-pinvoke]: Glfw.PInvoke.gen.cs
[^glfw-delegates]: Glfw.Delegates.gen.cs
[^glfw-native]: libglfw.3.5.dylib
[^openal-loader]: Brutal.OpenAl NativeLoader.cs
[^openal-alc]: Alc.Wrapper.cs
[^openal-device]: OpenAL Device.cs
[^openal-context]: OpenAL Context.cs
[^openal-objnames]: Al.ObjectNames.gen.cs
[^openal-source]: AlSource.Properties.gen.cs
[^openal-buffer]: AlBuffer.cs
[^openal-ext]: Al.Ext.gen.cs
[^openal-cb]: Callbacks.Internal.cs
[^platform-cs]: Brutal.Core Platform.cs
[^sdl-pinvoke]: Brutal.Sdl Sdl.PInvoke.cs
[^sdl-vulkan]: Sdl.Vulkan.cs
[^sdlmixer-cs]: Brutal.Sdl.Mixer SdlMixer.cs
[^sdl-readme]: Ahwoo.BRUTAL.Sdl README
[^imgui-glfw]: ImGui GLFW backend interop
[^inventory]: NuGet package inventory
[^brutal-readme]: BRUTAL README
