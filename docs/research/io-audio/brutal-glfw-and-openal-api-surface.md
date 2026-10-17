---
type: Reference
title: Brutal.Glfw, Brutal.OpenAl (and Sdl/Fmod) API surface, loaders, callback mechanics and drop-in feasibility
description: Precise inventory of the Brutal.GlfwApi and Brutal.OpenAlApi types the PoC and a typical BRUTAL game touch, how each library loads its native binary, how native callbacks reach C#, and what a managed browser replacement with the same namespaces would have to mirror, including licence terms.
tags: [brutal, glfw, openal, sdl, fmod, api-surface, loader, callbacks, drop-in, licence]
sources:
  - id: brutal-glfw
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw
    title: BRUTAL Brutal.Glfw (Glfw.cs, Manual/, Generated/)
  - id: brutal-glfw-init
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw/Manual/Libraries/Glfw.Initialization.cs
    title: Glfw.Initialization.cs
  - id: brutal-glfw-window
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw/Manual/Libraries/Glfw.Window.cs
    title: Glfw.Window.cs (GlfwWindow class, callbacks, Refresh)
  - id: brutal-glfw-input
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw/Manual/Libraries/Glfw.Input.cs
    title: Glfw.Input.cs (callback delegate types, clipboard, Time)
  - id: brutal-glfw-delegates
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw/Generated/Glfw.Delegates.gen.cs
    title: Glfw.Delegates.gen.cs ([UnmanagedFunctionPointer] callback types)
  - id: brutal-glfw-notices
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw/LICENSES/BRUTAL-GLFW-THIRD-PARTY-NOTICES.txt
    title: Brutal.Glfw third-party notices (GLFW 3.5.1 RocketWerkz fork)
  - id: brutal-openal
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.OpenAl
    title: BRUTAL Brutal.OpenAl (Manual/, Generated/)
  - id: brutal-openal-loader
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.OpenAl/Manual/NativeLoader.cs
    title: Brutal.OpenAl NativeLoader.cs
  - id: brutal-openal-callbacks
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.OpenAl/Manual/Callbacks.cs
    title: Brutal.OpenAl Callbacks.cs
  - id: brutal-openal-ext
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.OpenAl/Generated/Al/Al.Ext.gen.cs
    title: Al.Ext.gen.cs (extension entry points via alcGetProcAddress)
  - id: brutal-platform
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Core/Platform.cs
    title: Brutal.Core Platform.cs
  - id: brutal-sdl
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Sdl/Manual/Sdl.PInvoke.cs
    title: Brutal.Sdl Sdl.PInvoke.cs (resolver and candidate names)
  - id: brutal-sdl-notices
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Sdl/LICENSES/BRUTAL-SDL-THIRD-PARTY-NOTICES.txt
    title: Brutal.Sdl third-party notices (SDL 3.4.12)
  - id: brutal-sdl-mixer-notices
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Sdl.Mixer/LICENSES/BRUTAL-SDL-MIXER-THIRD-PARTY-NOTICES.txt
    title: Brutal.Sdl.Mixer third-party notices (SDL_mixer 3.2.4, stb_vorbis)
  - id: brutal-fmod
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Fmod/FmodStudioApi/Manual/FmodStudio.cs
    title: Brutal.Fmod FmodStudio.cs (NativeLibrary.Load("fmod"))
  - id: brutal-fmod-core
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Fmod/FmodCoreApi/Manual/Fmod.cs
    title: Brutal.Fmod Fmod.cs (VERSION = 0x00020308)
  - id: brutal-license
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/LICENSE.txt
    title: BRUTAL License v1.0
  - id: brutal-imgui-glfw
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ImGui.Backends/PlatformBackends/GlfwBackend.cs
    title: Brutal.ImGui.Backends GlfwBackend.cs (platform backend that consumes Brutal.GlfwApi)
  - id: ksa-app
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/Core/App.cs
    title: KSA Core/App.cs (reference only)
  - id: ksa-program
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA/Program.cs
    title: KSA KSA/Program.cs (reference only)
  - id: ksa-audio
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA/GameAudio.cs
    title: KSA KSA/GameAudio.cs (FMOD use, reference only)
  - id: corpus-map
    resource: /overview/corpus-map.md
    title: Corpus map (package versions, dependency graph)
  - id: gap-script
    resource: "scope: python3 -I scripts comparing Brutal EntryPoint strings with libglfw.js / glfw3.cpp / libopenal.js, run 2026-10-16 (session scratchpad r5/cmp_glfw.py, cmp_al.py)"
    title: Entry-point gap analysis
  - id: sib-fnptr
    resource: /dotnet-wasm/function-pointers-and-native-callbacks.md
    title: Function pointers, UnmanagedCallersOnly and native resolution on .NET 10 browser-wasm (sibling note)
  - id: sib-build-from-source
    resource: /brutal/building-from-source.md
    title: Building BRUTAL libraries from source, assembly identity, drop-in replacement (sibling note)
  - id: sib-windowing
    resource: /brutal/windowing-input-audio-apis.md
    title: Brutal.Glfw, Brutal.OpenAl and Brutal.Sdl API surfaces and browser replacement options (sibling note)
  - id: imgui-glfw-interop
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ImGui.Backends/PlatformBackends/GlfwBackend.GlfwInterop.cs
    title: Brutal.ImGui.Backends GlfwBackend.GlfwInterop.cs (own LibraryImport of glfwGetCocoaWindow / glfwGetWin32Window)
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:08:04Z }
status: draft
stale_after: 2027-01-31
---

This note overlaps with the sibling analysis [/brutal/windowing-input-audio-apis.md](/brutal/windowing-input-audio-apis.md), which reaches the same replacement-assembly verdict; this one adds the layering, the member inventory used by the ImGui backend, the gap analyses and licence terms.[^sib-windowing]

# Layering (applies to Glfw, OpenAl, Sdl)

1. A generated raw layer `Glfw.PInvoke` / `Al.PInvoke` / `Alc.PInvoke` of `[LibraryImport(Dll, EntryPoint = "...")] private static partial` functions with thin public wrappers that call `Log.Trace()`. Parameter types use BRUTAL handle structs and `[UnmanagedFunctionPointer]` delegates.[^brutal-glfw]
2. A hand-written managed layer (`Glfw` static class, `GlfwWindow`, `GlfwMonitor`, `GlfwCursor`; `Al`, `Alc`, `Device`, `Context`, `AlSource`, `AlBuffer`) that turns pointers into spans/properties/events.[^brutal-glfw][^brutal-openal]
3. Game code. The reference game uses only layer 2 for GLFW (`Glfw.Init`, `Glfw.CreateWindow(...)`, `window.OnKey += ...`) and `Brutal.FmodApi` (not OpenAL) for audio.[^ksa-program][^ksa-audio]

A browser replacement can therefore swap layer 1 only and keep layer 2 byte-for-byte, or replace layers 1 and 2 with a smaller hand-written implementation of the same public names (see "Drop-in feasibility").

# Loaders

| Library | Logical name | Where the native is loaded | File names | Notes for browser |
| --- | --- | --- | --- | --- |
| Brutal.Glfw | `const string Dll = "glfw"` | `static Glfw()` calls `NativeLibrary.Load(OperatingSystem.IsWindows() ? "glfw3" : "glfw", typeof(Glfw).Assembly, DllImportSearchPath.AssemblyDirectory)`, then `SetDllImportResolver` returning that handle (and asserting `libraryName == "glfw"`), then `PInvoke.SetErrorCallback(...)`[^brutal-glfw-init] | `glfw3.dll`, `libglfw.dylib`/`libglfw.3.dylib`/`libglfw.3.5.dylib`, `libglfw.so`/`.so.3`/`.so.3.5`[^brutal-glfw-notices] | Any use of `Glfw` triggers the static constructor; it must be bypassed (e.g. `if (OperatingSystem.IsBrowser())`) or the type replaced. The package ships GLFW 3.5.1 from a RocketWerkz fork with trackpad-gesture, scroll-detail and macOS video-mode patches.[^brutal-glfw-notices] |
| Brutal.OpenAl | `Al.Dll = Alc.Dll = "openal"` | `[ModuleInitializer] NativeLoader.Register()` installs a resolver that returns 0 for any other name and otherwise calls `NativeLibrary.Load(fileName, assembly, DllImportSearchPath.AssemblyDirectory)`[^brutal-openal-loader] | `OpenAL32.dll`, `libopenal.1.dylib`, `libopenal.so.1`, chosen with `Platform.CurrentFamily`[^brutal-openal-loader] | `Platform` has a static constructor that throws `PlatformNotSupportedException("Unsupported platform")` unless Windows, Linux or macOS, so the loader (and anything else touching `Platform`) throws on browser.[^brutal-platform] `OperatingSystem.IsBrowser()` is not considered anywhere in these two files. |
| Brutal.Sdl | `Dll = "SDL3"` | `static PInvoke()` registers a resolver; it tries a candidate list with `NativeLibrary.Load` under a lock (`SDL3.dll`; `libSDL3.dylib`; `libSDL3.so.0`, `libSDL3.so`, then bare `SDL3`) and throws `DllNotFoundException` listing every failure[^brutal-sdl] | SDL 3.4.12 shipped; Brutal.Sdl.Mixer ships SDL_mixer 3.2.4 with stb_vorbis/dr_flac/dr_mp3 decoders[^brutal-sdl-notices][^brutal-sdl-mixer-notices] | The bare `"SDL3"` candidate is the only one that could match a statically linked module name. |
| Brutal.Fmod | `"fmod"` | `FmodStudio` static constructor `NativeLibrary.Load("fmod", assembly, AssemblyDirectory)` before any Studio import; core imports use `[LibraryImport("fmod", EntryPoint = "FMOD5_...")]`[^brutal-fmod] | FMOD header version 2.03.08[^brutal-fmod-core] | proprietary; see [browser audio options](/io-audio/browser-audio-options.md) |

# Brutal.GlfwApi surface (types and members a typical game uses)

Namespace `Brutal.GlfwApi`; global usings pull in `Brutal.Numerics` (`int2`, `float2`, `double2`).[^brutal-glfw]

## Static `Glfw`

| Member | Signature (abridged) | Notes |
| --- | --- | --- |
| `Init` / `Terminate` | `bool Init()`, `void Terminate()` | `Init` also registers monitor and joystick callbacks[^brutal-glfw-init] |
| `SetInitHint` | `void SetInitHint(GlfwInitHint, int)` | `glfwInitHint` is absent from the built-in Emscripten GLFW |
| `ErrorCallback` | `static event GlfwErrorCallback? (GlfwErrorCode, string)` | |
| `VersionString`, `Version` | `string`, `VersionInfo{Major,Minor,Revision}` | |
| `WindowHint` | `void WindowHint(GlfwWindowHint, int)` and `(GlfwWindowHint, string)` | the reference game sets `ClientApi=0`, `AutoIconify`, `FocusOnShow`, `Visible`, `RefreshRate`[^ksa-program] |
| `CreateWindow` | `GlfwWindow CreateWindow(in GlfwWindow.CreateInfo)`; `CreateInfo{ required string Title; required int2 Size; GlfwMonitor Monitor; GlfwWindow? Share }` | throws `InvalidOperationException` with `GetError` text on failure[^brutal-glfw-window] |
| `PollEvents` | `void PollEvents()` | calls `glfwPollEvents` then `ExceptionAggregator.CheckExceptions()` (rethrows callback exceptions)[^brutal-glfw-window] |
| `WaitEvents`, `WaitEventsTimeout(double)`, `PostEmptyEvent` | | |
| `Time` | `double Time { get; set; }` | `glfwGetTime`/`glfwSetTime`; `GetTimerValue()`/`GetTimerFrequency()` (`ulong`) also exposed[^brutal-glfw-input] |
| `Clipboard`, `SetClipboard(string)` | `string? Clipboard { get; set; }` | hard-codes a null window[^brutal-glfw-input] |
| `PrimaryMonitor`, `GetMonitors()` | `GlfwMonitor`, `ReadOnlySpan<GlfwMonitor>` | `GlfwMonitor` exposes `Name`, `Position`, `PhysicalSize`, `ContentScale`, `VideoMode`, `VideoModes`, `Size`, `GetMonitorWorkArea`, `GammaRamp`[^brutal-glfw] |
| `MonitorCallback` | `static event GlfwMonitorCallback?` | |
| `JoystickPresent(int)`, `GetJoystickAxes/Buttons/Hats(int)`, `GetJoystickName/Guid`, `JoystickIsGamepad`, `GetGamepadName`, `GetGamepadState(int, out GlfwGamepadState)`, `UpdateGamepadMappings(string)`, `JoystickCallback` event | `GlfwGamepadState` has `fixed byte Buttons[15]; fixed float Axes[6]` | [^brutal-glfw-input][^brutal-glfw] |
| `RawMotionSupported()`, `GetKeyName(GlfwKey, int)`, `GetKeyScanCode(int)` | | |
| `CreateCursor` | `GlfwCursor CreateCursor(GlfwStandardCursor)` and `(in GlfwCursor.CreateInfo{GlfwImage Image; int2 Hot})` | |
| `SwapInterval(int)`, `ExtensionSupported(string)`, `GetCurrentContext()`, `GetProcAddress(ReadOnlySpan<byte>)` | OpenGL context helpers; unused by Vulkan games | |
| `VulkanSupported()`, `GetRequiredInstanceExtensions(out uint count)` -> `byte**`, `GetInstanceProcAddress(nint, string)`, `GetPhysicalDevicePresentationSupport(...)`, `CreateWindowSurface(nint instance, GlfwWindow window, out ulong surface)` -> `int` (VkResult) | the reference game calls the last two[^brutal-glfw] | see [Emscripten GLFW options](/io-audio/emscripten-glfw-options.md) |

## `GlfwWindow` (sealed class, `IDisposable`, `INullable`)

- Cached-state properties refreshed by `Refresh()` after creation and by callbacks afterwards: `Title`, `Position`/`X`/`Y`, `Size`/`Width`/`Height`, `FramebufferSize`, `AspectRatio`, `ContentScale`, `CursorPos`/`CursorX`/`CursorY`, `Opacity`, `Monitor`, `IsMaximised`, `IsIconified`, `IsFocused`, `IsFullscreen`, `IsWindowed`, `ShouldClose`, `Visible`, `UserPointer`, `NativeHandle`.[^brutal-glfw-window]
- Setters call the native function and rely on a later callback to refresh the cached value (for example the `Size` setter calls `glfwSetWindowSize` and `_size` only changes inside the `onWindowSize` callback). A managed implementation therefore **must raise the corresponding event** after a programmatic change or the property stays stale.[^brutal-glfw-window]
- Methods: `SetIcon`, `SetSizeLimits`, `SetFullscreen(GlfwMonitor, int2 size, int refreshRate)`, `SetWindowed(int2 pos, int2 size)`, `GetAttribute/SetAttribute`, `GetInputMode/SetInputMode(GlfwInputMode, int)`, `GetKey(GlfwKey)`, `GetMouseButton(GlfwMouseButton)`, `SetCursor`, `Iconify/Restore/Maximize/Focus/RequestAttention`, `GetWindowFrameSize`, `WaitForWindow()` (loops `WaitEvents` until the framebuffer is non-zero), `CenterToMonitor`, `MakeContextCurrent`, `SwapBuffers`, `Dispose`.[^brutal-glfw]
- Events (managed delegates invoked from `PollEvents`): `OnKey(GlfwWindow, GlfwKey, int scancode, GlfwKeyAction, GlfwModifier)`, `OnChar(GlfwWindow, uint)`, `OnCharMods`, `OnMouseButton(GlfwWindow, GlfwMouseButton, GlfwButtonAction, GlfwModifier)`, `OnCursorPos(GlfwWindow, double2)`, `OnCursorEnter(GlfwWindow, bool)`, `OnScroll(GlfwWindow, double2)`, `OnScrollDetail(..., GlfwScrollFlags)`, `OnTrackpadZoom(GlfwWindow, double)`, `OnTrackpadRotate(GlfwWindow, double)`, `OnDrop(GlfwWindow, int, string[])`, `OnClose`, `OnFocus(GlfwWindow, GlfwFocusEvent)`, `OnIconify`, `OnMaximize`, `OnPosition(GlfwWindow, int2)`, `OnSize(GlfwWindow, int2)`, `OnFramebufferSize(GlfwWindow, int2)`, `OnContentScale(GlfwWindow, float2)`, `OnRefresh`.[^brutal-glfw-window][^brutal-glfw-input]

## Enums and values that matter for a shim

- `GlfwKey` values equal GLFW's (`Space`=32 ... `Z`=90, `Escape`=256 ... `Menu`=348); `GlfwKeyAction` Release/Press/Repeat = 0/1/2; `GlfwButtonAction` Release/Press; `GlfwModifier` Shift/Control/Alt/Super/CapsLock/NumLock = 1/2/4/8/16/32; `GlfwMouseButton` `Number1..Number8` plus `Left`(0)/`Right`(1)/`Middle`(2); `GlfwScrollFlags` Precise/Begin/End/Momentum = 1/2/4/8.[^brutal-glfw]
- `GlfwInputMode.Cursor` = 0x33001 with `GlfwCursorMode` Normal 0x34001, Hidden 0x34002, Disabled 0x34003, Captured 0x34004; `RawMouseMotion` = 0x33005; `UnlimitedMouseButtons` = 0x33006.[^brutal-glfw]
- Gamepad enums follow GLFW (`GlfwGamepadButton` A/B/X/Y/LeftBumper/RightBumper/Back/Start/Guide/LeftThumb/RightThumb/DpadUp/Right/Down/Left; `GlfwGamepadAxis` LeftX/LeftY/RightX/RightY/LeftTrigger/RightTrigger).[^brutal-glfw]

## How the reference game and the ImGui backend consume it

- `Core.App.Run()` is a blocking `while` loop calling `OnFrame(dt)` and sleeping to a frame limit; `Program.OnFrame` calls `Glfw.PollEvents()` once, tests `_window.ShouldClose`, and returns `OnFrameResult.Exit/Continue`. Loading screens call `Glfw.PollEvents()` and render from nested synchronous calls (`Loading.OnFrame`). See [main loop patterns](/io-audio/main-loop-patterns-in-dotnet-wasm.md).[^ksa-app][^ksa-program]
- Input is consumed through window events registered once (`OnClose`, `OnKey`, `OnMouseButton`, `OnCursorPos`, `OnScroll`, `OnFocus`) plus polling `GetKey`, `GetInputMode`, `Glfw.GetGamepadState(0, ...)` and `Glfw.Time`.[^ksa-program]
- `Brutal.ImGui.Backends/PlatformBackends/GlfwBackend.cs` (1227 lines) is a port of the Dear ImGui GLFW backend that uses exactly this API surface (window events, `Glfw.Time`, `CreateCursor(GlfwStandardCursor)`, `Clipboard`, `GetMonitors`, `GetGamepadState`, `GetKeyName`, `GetKey` for modifier state, `GetInputMode/SetInputMode` for cursor, `SetAttribute(MousePassthrough)`, `FramebufferSize/Size` for the DPI scale). Its nested `GlfwInterop` declares its own `[LibraryImport]` for `glfwGetCocoaWindow` (module `glfw`) and `glfwGetWin32Window` (module `glfw3`), reached only on macOS and Windows, so a replacement `Brutal.Glfw` does not need to supply them and they are never called in the browser.[^imgui-glfw-interop] A drop-in `Brutal.GlfwApi` would give the ImGui platform backend a working browser implementation without changes.[^brutal-imgui-glfw]

# Callback mechanics

## GLFW

- Each native callback type has a generated `[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate` (`GlfwKeyUnmanagedCallback(GlfwWindowHandle*, int key, int scancode, int action, int mods)`, ...).[^brutal-glfw-delegates]
- `GlfwWindow`'s constructor builds 20 lambdas, wraps each body in `try/catch` that pushes failures into `Glfw.ExceptionAggregator`, keeps them alive with `GCHandle.Alloc`, and registers them through `Glfw.PInvoke.Set*Callback(this, delegate)`. The window pointer is a `GlfwWindowHandle*`, so any replacement can use a fake handle (for example an index) as long as it is non-null.[^brutal-glfw-window]
- The static callbacks (`SetErrorCallback`, monitor, joystick) are stored in static readonly fields; `InitAllocator` roots delegates in a list.[^brutal-glfw-init]
- On the browser with Emscripten GLFW these delegates would become wasm function-table entries invoked from JS DOM handlers. The sibling .NET analysis finds that Mono's interpreter cannot create function pointers for delegates (`Marshal.GetFunctionPointerForDelegate` crashes, dotnet/runtime#104391; thunks exist only for static `[UnmanagedCallersOnly]` methods after a relink), so these delegate-typed registrations cannot work unmodified.[^sib-fnptr] With a managed shim there is **no native boundary**: the shim invokes the same delegates directly from `PollEvents`.

## OpenAL

- Core AL/ALC has no callbacks. Extensions (`AL_SOFT_events`, `ALC_SOFT_system_events`, `AL_SOFT_callback_buffer`) use `[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]` trampolines taken with `&Trampoline` and stored in concurrent dictionaries keyed by context/buffer pointer.[^brutal-openal-callbacks]
- 50 extension entry points are resolved lazily with `alcGetProcAddress(AlcDevice.Null, "...")` into `delegate* unmanaged[Cdecl]` fields and throw `AlExtensionException` when missing, so calling them needs `alcGetProcAddress`, which Emscripten's OpenAL does not export (see [browser audio options](/io-audio/browser-audio-options.md)).[^brutal-openal-ext]

# Brutal.OpenAlApi surface (what a basic game touches)

- Device/context: `Alc.OpenDevice(string? name = null)` -> `Device`; `Device.CreateContext(Context.CreateInfo info = default)` -> `Context`; `Context.MakeCurrent()` / `Context.MakeCurrent(Context?)` / `TryMakeCurrent`; `Context.Dispose()`; `Device.Close()` / `Dispose()`; `Alc.GetDeviceSpecifiers()`, `Device.Extensions`, `MajorVersion`, `MinorVersion`, `Pause()`/`Resume()` (AL_SOFT pause), `ClockTime`, `Latency` (device clock extension).[^brutal-openal]
- Objects are `readonly struct`s wrapping a `uint Name`: `AlBuffer` (`SetData(AlFormat, ReadOnlySpan<byte>, int sampleRate)`, `Delete()`, `IsValid()`, `Frequency`, `Bits`, `Channels`, `Size`, `LoopPoints`, `SampleLength`, `SecLength`) and `AlSource` (`Play/Pause/Stop/Rewind`, `QueueBuffers(ReadOnlySpan<AlBuffer>)`, `UnqueueBuffers(Span<AlBuffer>)`, properties `Gain`, `Pitch`, `MinGain`, `MaxGain`, `ReferenceDistance`, `MaxDistance`, `RolloffFactor`, cone angles/gains, `SecOffset`, `SampleOffset`, `Position`, `Velocity`, `Direction` (all `float3`), `Looping`, `SourceRelative`, `Buffer`, `State`, `Type`, `BuffersQueued`, `BuffersProcessed`, `DistanceModel`, `Spatialize`, plus `PlayAt(long)` and latency getters from AL_SOFT extensions).[^brutal-openal]
- Factories: `Al.GenBuffer()/GenBuffers(int)/GenBuffers(Span<AlBuffer>)`, `Al.GenSource()/GenSources(...)`, `Al.DeleteBuffers/DeleteSources`.[^brutal-openal]
- Globals: `Al.Listener.Gain/Position/Velocity/MetersPerUnit/Orientation`, `Al.DistanceModel`, `Al.DopplerFactor`, `Al.SpeedOfSound`, `Al.GetString`, `Al.GetExtensions()`, `Al.IsExtensionPresent`, `Al.DeferUpdates()`/`ProcessUpdates()`, `Al.PlaySources/PauseSources/StopSources/RewindSources(ReadOnlySpan<AlSource>)`, `Al.GetError()/ThrowIfError()` (wrappers never check errors automatically).[^brutal-openal]
- Formats: `AlFormat` includes Mono8/Mono16/Stereo8/Stereo16, float32/double, multichannel, IMA4/MSADPCM, B-format and UHJ variants; only the first six matter for Web Audio.[^brutal-openal]
- Entry points: 126 core P/Invokes (`al*`/`alc*`); 33 of them are EFX effect/filter/aux-slot functions.[^gap-script]
- KSA itself does not use OpenAL: it drives FMOD core (`Fmod.SystemCreate`, `System.Init(1024, InitFlags._3dRightHanded, 0)`, `Set3dSettings`, `SetFileSystem` callbacks, sound groups, channel groups, and DSP wrappers `Compressor`, `MultibandEq`, `Delay`, `DigitalReverb`, `ConvolutionReverb`).[^ksa-audio]

# Drop-in feasibility

## Option 1: swap layer 1 only ("virtual GLFW", "virtual OpenAL")

Keep the high-level wrappers unchanged and replace the body of each `private static partial` P/Invoke method by a call into a managed implementation, with `Glfw.PInvoke` returning fake handles and storing the registered delegates. The static constructor's `NativeLibrary.Load` and the OpenAL `NativeLoader` are removed or guarded. Pros: identical behaviour for `GlfwWindow` (cached state, callback exception aggregation), identical types so `Brutal.ImGui.Backends` keeps working. Cons: the wrappers use pointers and `[LibraryImport]` machinery (source generator output includes marshalling stubs), ~130 GLFW and ~130 AL methods to re-implement, and it tracks monthly upstream version changes (all packages are `2026.10.x`, net10.0 only).[^corpus-map]

## Option 2: new small library with the same namespaces

A new assembly named `Brutal.Glfw` (the NuGet's simple name) exposing `Brutal.GlfwApi.Glfw`, `GlfwWindow`, enums and structs with the same names but implemented directly over JSImport (no pointers, no fake handles). Pros: small, no unsafe code, can add browser-specific members (canvas selector, `RequestFullscreen`, pointer-lock events). Binding: the BRUTAL NuGet assemblies are not strong-named and all carry AssemblyVersion 1.0.0.0, so a replacement with the same simple name and version binds for prebuilt consumers such as `Ahwoo.BRUTAL.ImGui.Backends`, provided it keeps every member those binaries call; the practical wrinkle is NuGet still pulling the real `Ahwoo.BRUTAL.Glfw` transitively, which the sibling build note discusses (project reference plus excluding the package, or rebuilding the dependent libraries from source).[^sib-build-from-source] For source-compiled game code, source compatibility alone is sufficient.

The enums, constants and plain structs (`Generated/Enums`, `Glfw.Constants.gen.cs`, `Glfw.Structs.gen.cs`) are pure managed and can be reused as-is under the licence below in either option.

## Licence and legality

BRUTAL License v1.0 is MIT-style with extra conditions: the copyright notice must be kept in all copies; any "Product" distributed to end users that incorporates, links to or is built using the Software must show the copyright in its credits screen, show the Brutal logo before gameplay (unless the product cannot display graphics or written permission exists), and ship `NOTICE.txt`; third-party components keep their own licences (the shipped GLFW is zlib, OpenAL Soft is LGPL/BSD, and so on, listed in each package's `LICENSES/`); contributions are licensed to Ahwoo Limited and recipients; governing law is New Zealand.[^brutal-license]
Modification, merging, publishing and sublicensing are expressly permitted, so a browser fork or reimplementation is allowed provided the notice/credits/logo/NOTICE obligations carry over to the resulting game. A reimplementation that copies BRUTAL source (generated enums, XML docs, wrapper classes) is clearly a modified copy covered by that licence; an API-compatible clean-room rewrite raises no additional obligation beyond naming but the safest course is to treat it as covered. This is not legal advice; confirm with Ahwoo Limited before publishing a fork publicly. UNVERIFIED.

## Open questions

- UNVERIFIED: does Mono wasm accept `NativeLibrary.Load` for the bare names `"glfw"`/`"openal"` if the modules are statically linked? (needed only for strategy A)
- Which ImGui.Backends members does the browser build actually need (the Win32/Cocoa `GlfwInterop` calls are OS-guarded and should be inert on browser)?
- Does RocketWerkz intend to expose `GlfwScrollDetail`/trackpad events to the browser, and with which semantics (the fork patches are only documented in the fork's `ROCKETWERKZ.md`, not in this checkout)?

[^brutal-glfw]: BRUTAL Brutal.Glfw (Glfw.cs, Manual/, Generated/)
[^brutal-glfw-init]: Glfw.Initialization.cs
[^brutal-glfw-window]: Glfw.Window.cs (GlfwWindow class, callbacks, Refresh)
[^brutal-glfw-input]: Glfw.Input.cs (callback delegate types, clipboard, Time)
[^brutal-glfw-delegates]: Glfw.Delegates.gen.cs ([UnmanagedFunctionPointer] callback types)
[^brutal-glfw-notices]: Brutal.Glfw third-party notices (GLFW 3.5.1 RocketWerkz fork)
[^brutal-openal]: BRUTAL Brutal.OpenAl (Manual/, Generated/)
[^brutal-openal-loader]: Brutal.OpenAl NativeLoader.cs
[^brutal-openal-callbacks]: Brutal.OpenAl Callbacks.cs
[^brutal-openal-ext]: Al.Ext.gen.cs (extension entry points via alcGetProcAddress)
[^brutal-platform]: Brutal.Core Platform.cs
[^brutal-sdl]: Brutal.Sdl Sdl.PInvoke.cs (resolver and candidate names)
[^brutal-sdl-notices]: Brutal.Sdl third-party notices (SDL 3.4.12)
[^brutal-fmod]: Brutal.Fmod FmodStudio.cs (NativeLibrary.Load("fmod"))
[^brutal-fmod-core]: Brutal.Fmod Fmod.cs (VERSION = 0x00020308)
[^brutal-sdl-mixer-notices]: Brutal.Sdl.Mixer third-party notices (SDL_mixer 3.2.4, stb_vorbis)
[^brutal-license]: BRUTAL License v1.0
[^brutal-imgui-glfw]: Brutal.ImGui.Backends GlfwBackend.cs (platform backend that consumes Brutal.GlfwApi)
[^ksa-app]: KSA Core/App.cs (reference only)
[^ksa-program]: KSA KSA/Program.cs (reference only)
[^ksa-audio]: KSA KSA/GameAudio.cs (FMOD use, reference only)
[^corpus-map]: Corpus map (package versions, dependency graph)
[^gap-script]: Entry-point gap analysis
[^sib-fnptr]: Function pointers, UnmanagedCallersOnly and native resolution on .NET 10 browser-wasm (sibling note)
[^sib-build-from-source]: Building BRUTAL libraries from source, assembly identity, drop-in replacement (sibling note)
[^sib-windowing]: Brutal.Glfw, Brutal.OpenAl and Brutal.Sdl API surfaces and browser replacement options (sibling note)
[^imgui-glfw-interop]: Brutal.ImGui.Backends GlfwBackend.GlfwInterop.cs (own LibraryImport of glfwGetCocoaWindow / glfwGetWin32Window)
