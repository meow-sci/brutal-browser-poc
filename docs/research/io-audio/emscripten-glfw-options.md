---
type: Technology Assessment
title: Emscripten GLFW for Brutal.Glfw (strategy A) - built-in libglfw.js vs contrib.glfw3 port
description: Compares Emscripten's built-in JavaScript GLFW emulation (-sUSE_GLFW=3) with the pongasoft contrib.glfw3 port, how each would be linked into a .NET wasm binary so unmodified Brutal.Glfw P/Invokes resolve, which of BRUTAL's 127 GLFW entry points each one lacks, and the Emscripten-version coupling with .NET 10.
tags: [glfw, emscripten, wasm, dotnet, windowing, input, strategy-a, brutal-glfw, vulkan-surface]
sources:
  - id: emsc-libglfw
    resource: https://raw.githubusercontent.com/emscripten-core/emscripten/main/src/lib/libglfw.js
    title: emscripten-core/emscripten src/lib/libglfw.js (main, Emscripten 6.0.13-git, fetched 2026-10-16)
  - id: emsc-libglfw-3156
    resource: https://raw.githubusercontent.com/emscripten-core/emscripten/3.1.56/src/library_glfw.js
    title: emscripten-core/emscripten 3.1.56 src/library_glfw.js
  - id: emsc-contrib-port
    resource: https://raw.githubusercontent.com/emscripten-core/emscripten/3.1.56/tools/ports/contrib/glfw3.py
    title: Emscripten 3.1.56 tools/ports/contrib/glfw3.py (bundles contrib.glfw3 1.1.0)
  - id: emsc-contrib-port-402
    resource: https://raw.githubusercontent.com/emscripten-core/emscripten/6.0.2/tools/ports/contrib/glfw3.py
    title: Emscripten 6.0.2 tools/ports/contrib/glfw3.py (bundles contrib.glfw3 3.4.0.20260301)
  - id: pongasoft-readme
    resource: https://raw.githubusercontent.com/pongasoft/emscripten-glfw/master/README.md
    title: pongasoft/emscripten-glfw README (features, port options, release notes, Emscripten availability table)
  - id: pongasoft-usage
    resource: https://raw.githubusercontent.com/pongasoft/emscripten-glfw/master/docs/Usage.md
    title: pongasoft/emscripten-glfw docs/Usage.md
  - id: pongasoft-comparison
    resource: https://raw.githubusercontent.com/pongasoft/emscripten-glfw/master/docs/Comparison.md
    title: pongasoft/emscripten-glfw docs/Comparison.md (vs built-in libglfw.js)
  - id: pongasoft-glfw3cpp
    resource: https://raw.githubusercontent.com/pongasoft/emscripten-glfw/master/src/cpp/glfw3.cpp
    title: pongasoft/emscripten-glfw src/cpp/glfw3.cpp (exported GLFW API, stubs)
  - id: pongasoft-window
    resource: https://raw.githubusercontent.com/pongasoft/emscripten-glfw/master/src/cpp/emscripten/glfw3/Window.cpp
    title: pongasoft/emscripten-glfw src/cpp/emscripten/glfw3/Window.cpp (wheel normalisation, cursor modes, pointer lock)
  - id: pongasoft-portpy
    resource: https://raw.githubusercontent.com/pongasoft/emscripten-glfw/master/port/glfw3.py
    title: pongasoft/emscripten-glfw port/glfw3.py (how the port builds and links)
  - id: dotnet-targets
    resource: https://raw.githubusercontent.com/dotnet/runtime/release/10.0/src/mono/browser/build/BrowserWasmApp.targets
    title: dotnet/runtime release/10.0 BrowserWasmApp.targets
  - id: dotnet-common-targets
    resource: https://raw.githubusercontent.com/dotnet/runtime/release/10.0/src/mono/wasm/build/WasmApp.Common.targets
    title: dotnet/runtime release/10.0 WasmApp.Common.targets
  - id: dotnet-runtime-c
    resource: https://raw.githubusercontent.com/dotnet/runtime/release/10.0/src/mono/browser/runtime/runtime.c
    title: dotnet/runtime release/10.0 src/mono/browser/runtime/runtime.c (wasm_dl_load, wasm_dl_symbol, get_native_to_interp)
  - id: dotnet-emsc-version
    resource: https://raw.githubusercontent.com/dotnet/runtime/release/10.0/src/mono/browser/emscripten-version.txt
    title: dotnet/runtime release/10.0 emscripten-version.txt (3.1.56); main branch file says 6.0.2
  - id: brutal-glfw
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw
    title: BRUTAL Brutal.Glfw source (Glfw.cs, Manual/, Generated/)
  - id: brutal-glfw-init
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw/Manual/Libraries/Glfw.Initialization.cs
    title: Brutal.Glfw Glfw.Initialization.cs (static constructor, NativeLibrary.Load, callbacks)
  - id: ksa-surface
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/Core/Renderer.cs
    title: KSA Core/Renderer.cs (Glfw.CreateWindowSurface call site, reference only)
  - id: gap-script
    resource: "scope: python3 -I set-difference of Brutal.Glfw EntryPoint strings against libglfw.js and glfw3.cpp symbol lists, run 2026-10-16 (script at the session scratchpad r5/cmp_glfw.py)"
    title: Entry-point gap analysis
  - id: sib-fnptr
    resource: /dotnet-wasm/function-pointers-and-native-callbacks.md
    title: Function pointers, UnmanagedCallersOnly and native resolution on .NET 10 browser-wasm (sibling note)
  - id: sib-native-linking
    resource: /dotnet-wasm/native-linking-and-emscripten.md
    title: Native linking in .NET 10 wasm builds, Emscripten 3.1.56, ports (sibling note)
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:08:04Z }
status: draft
stale_after: 2027-01-31
---

# Verdict

Strategy A (link an Emscripten GLFW into the .NET wasm binary so the unmodified `Brutal.Glfw` P/Invokes resolve) is technically possible but is the weaker option for the PoC. It needs the net10 `wasm-tools` workload (native relink), a loader shim, no-op stubs for functions neither implementation provides, and it ties us to an Emscripten (3.1.56) that is several major versions behind the one the port's current release targets (6.0.7). See [the recommendation](/io-audio/recommendation-io-audio.md) (managed shim chosen) and the managed design in [the input shim design](/io-audio/managed-browser-input-shim-design.md).

# The two implementations

| Aspect | Built-in `libglfw.js` (`-sUSE_GLFW=3`) | `contrib.glfw3` (`--use-port=contrib.glfw3`) |
| --- | --- | --- |
| Language / licence | JavaScript JS-library, MIT[^emsc-libglfw] | C++ static library plus a small JS library, Apache 2.0[^pongasoft-readme] |
| API level reported | `glfwGetVersion` returns 3.2.1 although Emscripten ships a 3.3.8 `glfw3.h`[^emsc-libglfw][^pongasoft-comparison] | 3.5.1 API in release 3.5.1.20260809 (needs Emscripten 6.0.7)[^pongasoft-readme] |
| Windows | one (aborts on a second `glfwCreateWindow`)[^emsc-libglfw] | many, one canvas each (`SetNextWindowCanvasSelector`)[^pongasoft-readme] |
| Keyboard | maps deprecated `keyCode`; every modifier maps to the LEFT variant; no sticky keys[^emsc-libglfw] | maps `KeyboardEvent.code` through a scancode table; uses `KeyboardEvent.key` for char codepoints; sticky keys; Super-key workaround[^pongasoft-usage] |
| Mouse wheel | vertical delta quantised to at least +/-1, horizontal delta passed raw (mixed units)[^emsc-libglfw] | pixel mode /100, line mode /3, page mode x80 (copied from SDL)[^pongasoft-window] |
| Cursor modes | NORMAL and DISABLED (pointer lock on click); HIDDEN logs "not implemented"; raw motion, sticky, lock-key-mods log "not implemented"[^emsc-libglfw] | NORMAL, HIDDEN, DISABLED (async pointer lock); CAPTURED unsupported; raw mouse motion returns an error; `glfwSetCursorPos` unavailable[^pongasoft-window][^pongasoft-glfw3cpp] |
| Gamepad | joystick API only; no `glfwGetGamepadState`, `glfwGetGamepadName`, `glfwUpdateGamepadMappings`[^emsc-libglfw] | joystick plus gamepad state (W3C standard mapping), polled in `glfwPollEvents`[^pongasoft-usage] |
| Clipboard | stub returning null[^emsc-libglfw] | set works, get returns the last value set (async browser API)[^pongasoft-readme] |
| Timer | `glfwGetTime` works; `glfwGetTimerValue`/`Frequency` abort[^emsc-libglfw] | all timer functions implemented[^pongasoft-comparison] |
| `glfwPollEvents` | empty function; callbacks fire straight from DOM handlers[^emsc-libglfw] | applies pending resize and polls gamepads; other callbacks still fire from DOM handlers[^pongasoft-readme][^pongasoft-usage] |
| Hi-DPI and resize | `glfwGetWindowContentScale` returns the device pixel ratio; a `matchMedia` resolution listener tracks changes; window-size, framebuffer-size and content-scale callbacks are fired from the canvas-resize hook (framebuffer size = CSS size x DPR when Hi-DPI aware)[^emsc-libglfw] | `GLFW_SCALE_FRAMEBUFFER` (default true), dynamic content-scale callback[^pongasoft-usage] |
| Fullscreen | `glfwSetWindowMonitor` aborts; fullscreen only via `Browser.requestFullscreen`[^emsc-libglfw] | `Module.glfwRequestFullscreen`/`emscripten::glfw3::RequestFullscreen`, must run inside a user event; `glfwSetWindowMonitor` is a logged no-op[^pongasoft-usage][^pongasoft-glfw3cpp] |
| Window events not delivered | setters for close/iconify/maximize exist; actual delivery not verified here | close, iconify, maximize and drop callbacks are logged no-ops (a browser has no such user action)[^pongasoft-glfw3cpp] |
| Vulkan | `glfwVulkanSupported()` returns 0; `glfwGetRequiredInstanceExtensions` aborts[^emsc-libglfw] | `glfwVulkanSupported()` returns false; `glfwGetRequiredInstanceExtensions` logs and returns count 0 / null; surface functions are not defined at all[^pongasoft-glfw3cpp] |
| GL context | creates a WebGL context unless `GLFW_CLIENT_API` is `GLFW_NO_API`[^emsc-libglfw] | same rule; `GLFW_NO_API` leaves the canvas to the application[^pongasoft-usage] |
| Maintenance | recent commits are refactors/cleanups (strict equality, removal of legacy DOM APIs, canvas-resize fix on exit)[^emsc-libglfw] | tagged releases roughly every few months, latest 2026-08-09[^pongasoft-readme] |

Notes on the table:

- The built-in library's own header comment lists "Input modes", "Clipboard", "Multiple windows", "Error codes && messages through callback" and event transmission on `glfwPollEvents` as things it does not do.[^emsc-libglfw]
- The contrib port's comparison page flags the built-in error handling as broken (`glfwSetErrorCallback` stores the pointer but never calls it) and its timer functions as throwing.[^pongasoft-comparison]
- Both rely on `Module["canvas"]` by default; contrib also takes a CSS selector for the canvas.[^pongasoft-usage]

# How each one is linked

## Built-in

`-sUSE_GLFW=3` at link time pulls `src/lib/libglfw.js` (a JS library) into the module. The C symbols (`glfwInit`, ...) are JS-library functions; wasm-ld sees them as imports that Emscripten satisfies from the JS library, so any native object that references `glfwInit` links.[^emsc-libglfw]

## contrib.glfw3

`--use-port=contrib.glfw3` (needed at compile and link; for .NET only the link step matters) makes Emscripten fetch a release zip, compile the C++ sources into `lib_emscripten-glfw3_<tag>-O2.a` in the Emscripten cache, and in `linker_setup` append `lib_emscripten_glfw3.js` to `JS_LIBRARIES` and set `MAX_WEBGL_VERSION=2` (unless `disableWebGL2`). Port options are `disableJoystick`, `disableWarning`, `disableMultiWindow`, `disableWebGL2`, `optimizationLevel`.[^pongasoft-portpy][^pongasoft-readme]

## How the .NET 10 wasm build would carry that

Read from the `release/10.0` MSBuild targets (not executed here; the net10 `wasm-tools` workload is not installed):

- Native inputs: every `@(NativeFileReference)` item is added to the emcc link command line.[^dotnet-targets]
- Extra link arguments: `$(EmccExtraLDFlags)` is appended verbatim to the link step, so `-sUSE_GLFW=3` or `--use-port=contrib.glfw3` (and `--js-library x.js`) can be passed there.[^dotnet-targets]
- Undefined symbols: `WasmAllowUndefinedSymbols` defaults to `false`, which adds `-s ERROR_ON_UNDEFINED_SYMBOLS=1`; `true` adds `-Wl,--allow-undefined` and `ERROR_ON_UNDEFINED_SYMBOLS=0`.[^dotnet-targets]
- Emscripten cache: the workload's `WasmCachePath` defaults to the SDK's prebuilt cache, which the build freezes (`EM_FROZEN_CACHE=1`); a different, writable `WasmCachePath` sets `EM_FROZEN_CACHE=0`. A port that has to be compiled into the cache (contrib.glfw3 is not pre-built) therefore needs a writable cache directory and network access to fetch the release zip; Uno Platform does this by copying the SDK cache once into a writable directory. The built-in `-sUSE_GLFW=3` and `-lopenal` are JS libraries and need no cache build.[^dotnet-targets][^sib-native-linking] UNVERIFIED in practice.
- P/Invoke resolution: `_WasmPInvokeModules` is built from the base file name of each `NativeFileReference` (plus `libSystem.Native` and two others), the generator emits a pinvoke table keyed by that module name, and at run time `wasm_dl_load(name)` returns a table handle only on an exact `strcmp` match of the requested name; otherwise it falls through to `dlopen` (compiled out unless dynamic linking).[^dotnet-common-targets][^dotnet-runtime-c]
- Resolution chain end to end (inferred from the files above, not executed): `[LibraryImport("glfw", EntryPoint = "glfwInit")]` -> source-generated `DllImport` stub -> Mono looks up module `"glfw"` -> pinvoke table entry `{ "glfwInit", &glfwInit }` compiled into `dotnet.native.wasm` -> `glfwInit` is an `extern "C"` symbol that wasm-ld leaves undefined -> Emscripten finds the same name in the JS library (`libglfw.js` or `lib_emscripten_glfw3.js`, or a C object from the contrib archive) and wires it as a wasm import. A `NativeFileReference` item can opt out of pinvoke scanning with metadata `ScanForPInvokes=false`.[^dotnet-common-targets][^dotnet-runtime-c]
- Consequence for BRUTAL: `Brutal.Glfw` imports from module `"glfw"` (`Dll = "glfw"`) but its static constructor first calls `NativeLibrary.Load("glfw", assembly, DllImportSearchPath.AssemblyDirectory)` and then installs a resolver returning that handle.[^brutal-glfw-init] The sibling analysis of the same runtime sources concludes that there is no `dlopen` on browser (`WASM_SUPPORTS_DLOPEN` is not defined), that `NativeLibrary.Load(name)` succeeds only when `name` is a statically linked P/Invoke module, and that handles not obtained from `Load` must not be faked; whether Mono's name probing (`libglfw`, `glfw.so`, ...) also matches a table named `glfw` is still UNVERIFIED there.[^sib-fnptr] So the loader would need an `OperatingSystem.IsBrowser()` branch or a `NativeFileReference` literally named `glfw` plus a check that the `AssemblyDirectory`-qualified `Load` call resolves (see [the API surface note](/io-audio/brutal-glfw-and-openal-api-surface.md)).

# Emscripten version coupling

- .NET 10 pins Emscripten 3.1.56: `src/mono/browser/emscripten-version.txt` on `release/10.0` says so (the same file on `main`, .NET 11, says 6.0.2),[^dotnet-emsc-version] and the sibling note confirms it from the installed 10.0.101 workload manifest (`Microsoft.NET.Runtime.Emscripten.3.1.56.*` packs) and the runtime pack's `RuntimeEmccVersion`; upstream Emscripten is at 6.0.12 (2026-10-08).[^sib-native-linking]
- `--use-port=contrib.glfw3` exists since Emscripten 3.1.54 per the Emscripten ChangeLog as read in the sibling note (the contrib README says "since 3.1.55").[^pongasoft-readme][^sib-native-linking] Emscripten 3.1.56 bundles contrib.glfw3 **1.1.0** (GLFW 3.4 API, 2024-02-29).[^emsc-contrib-port] Emscripten 6.0.2 bundles 3.4.0.20260301.[^emsc-contrib-port-402]
- What 1.1.0 lacks relative to the current 3.5.1.20260809 line (from the release notes): custom cursors (added for Emscripten 3.1.69), Super-key workaround (3.1.63), clipboard set/get (3.1.60/3.1.63), touch (4.0.4), pthread and offscreen canvas (4.0), flicker-free resize applied in `glfwPollEvents` (4.0.14), automatic canvas `tabindex` (5.0.3), `GLFW_UNLIMITED_MOUSE_BUTTONS` (6.0.7).[^pongasoft-readme]
- The port's current files can be used with an older Emscripten via `--use-port=port/emscripten-glfw3.py`, but the release notes say the simplified port file relies on the external-port-file feature "as of 4.0.10", so that route is not available on 3.1.56.[^pongasoft-readme] UNVERIFIED lower bound for the C++ sources themselves.
- Building the port out of tree with a newer Emscripten and linking the resulting `.a` through `NativeFileReference` into .NET's 3.1.56 link is possible in principle but mixes LLVM versions and feature flags. UNVERIFIED.
- Built-in `libglfw.js` at 3.1.56 has the same stubs as `main` (`glfwGetTimerValue` and `glfwGetRequiredInstanceExtensions` throw, `glfwSetWindowMonitor` throws).[^emsc-libglfw-3156]

# BRUTAL coverage of each implementation

`Brutal.Glfw` binds 127 core GLFW entry points plus 27 platform-native getters (Win32, Cocoa, X11, Wayland, EGL, OSMesa, which are only resolved if called).[^brutal-glfw][^gap-script]

Missing from built-in `libglfw.js` (19): `glfwCreateWindowSurface`, `glfwGetError`, `glfwGetGamepadName`, `glfwGetGamepadState`, `glfwGetInstanceProcAddress`, `glfwGetMonitorUserPointer`, `glfwSetMonitorUserPointer`, `glfwGetPhysicalDevicePresentationSupport`, `glfwGetPlatform`, `glfwPlatformSupported`, `glfwGetProcAddress` (provided by Emscripten's GL layer, not by the GLFW file), `glfwGetWindowTitle`, `glfwInitAllocator`, `glfwInitHint`, `glfwInitVulkanLoader`, `glfwUpdateGamepadMappings`, and the three RocketWerkz fork extensions `glfwSetScrollDetailCallback`, `glfwSetTrackpadZoomCallback`, `glfwSetTrackpadRotateCallback`.[^gap-script]

Missing from contrib.glfw3 (7): `glfwCreateWindowSurface`, `glfwGetInstanceProcAddress`, `glfwGetPhysicalDevicePresentationSupport`, `glfwInitVulkanLoader`, and the same three fork extensions.[^gap-script]

Why this matters even though a game may never need most of them:

- `GlfwWindow`'s constructor unconditionally registers 20 callbacks, including `SetScrollDetailCallback`, `SetTrackpadZoomCallback` and `SetTrackpadRotateCallback`, and then calls `Refresh()` which reads the window title, position, size, framebuffer size, content scale, cursor position and attributes.[^brutal-glfw] With the built-in library `glfwGetWindowTitle` is also absent.
- The Mono wasm pinvoke generator emits a table entry (and so an `extern` reference) for every P/Invoke whose module name is in the module list, used or not, which for module `glfw` includes the 27 platform-native getters (`glfwGetWin32Window`, `glfwGetX11Display`, ...) unless IL trimming has already removed the unused imports. With `WasmAllowUndefinedSymbols=false` an undefined symbol fails the link; with `true` the call aborts only when reached (Emscripten's missing-function stub). Either way the three fork extensions need a one-file C stub (a tiny `.c` added as `NativeFileReference`) that defines them as no-ops, and the Vulkan surface functions need managed or stub replacements. UNVERIFIED (derived from the target files and generator source, not executed).[^dotnet-common-targets]
- The BRUTAL-specific `GLFW_UNLIMITED_MOUSE_BUTTONS` input mode and the `GlfwScrollFlags`/trackpad gestures exist only in the RocketWerkz GLFW fork (3.5.1 plus patches), so strategy A can never reproduce trackpad zoom/rotate or scroll phase (Begin/End/Momentum) events; a managed shim can synthesise `Precise` and pinch-zoom (see [the input shim design](/io-audio/managed-browser-input-shim-design.md)).[^brutal-glfw]

# Vulkan surface creation under either implementation

Neither implementation can create a `VkSurfaceKHR`: both report Vulkan unsupported and the contrib port does not define the surface functions at all.[^emsc-libglfw][^pongasoft-glfw3cpp] BRUTAL's `Glfw.CreateWindowSurface(nint instance, GlfwWindow window, out ulong surface)` is a thin wrapper over `glfwCreateWindowSurface`; the reference game calls it from `Core/Renderer.cs` and reads `Glfw.GetRequiredInstanceExtensions` in `KSADeviceContextEx.cs`.[^brutal-glfw][^ksa-surface]

Proposal (works for A and B): the Vulkan-on-WebGPU layer advertises `VK_KHR_surface` plus `VK_EXT_headless_surface` (or a vendor extension such as `VK_BRUTAL_canvas_surface`) through `vkEnumerateInstanceExtensionProperties`; the managed `Glfw.GetRequiredInstanceExtensions` returns exactly those two names; and `Glfw.CreateWindowSurface` is implemented in managed code by asking the Vulkan layer for a surface bound to the window's canvas (a registry keyed by the fake window handle), instead of calling native code. With strategy A this means replacing the two Vulkan-related `Glfw.PInvoke` calls with managed code anyway, which is a second reason the Brutal.Glfw loader would need patching.

# Callbacks across the boundary

Emscripten's GLFW calls the registered function pointers from JavaScript DOM handlers through `makeDynCall` (indirect calls through the wasm function table).[^emsc-libglfw] BRUTAL registers lambdas converted to `[UnmanagedFunctionPointer(Cdecl)]` delegates (for example `GlfwKeyUnmanagedCallback`), pins them with `GCHandle`, and relies on LibraryImport marshalling to turn them into function pointers.[^brutal-glfw] On Mono wasm a native-to-managed call is only possible through thunks that the build generated ahead of time and registers by method key (`get_native_to_interp` -> `wasm_dl_get_native_to_interp`).[^dotnet-runtime-c] The sibling analysis concludes that these thunks exist only for static `[UnmanagedCallersOnly]` methods (and need the workload's relink), and that `Marshal.GetFunctionPointerForDelegate`, which LibraryImport uses for delegate parameters, is not usable in the interpreter (open issue dotnet/runtime#104391, `memory access out of bounds` in `get_native_to_interp`).[^sib-fnptr] **Consequence: the unmodified `Brutal.Glfw` cannot register a single callback under strategy A** (`SetErrorCallback` in the static constructor already passes a delegate). Strategy A therefore also requires a source fork of Brutal.Glfw that replaces the delegate callbacks with `[UnmanagedCallersOnly]` static methods (the style `Brutal.OpenAl` already uses for its extension callbacks), a static window-handle-to-instance table, unique `Type.Method` names for the generator, and the relink. This removes strategy A's main selling point ("unmodified BRUTAL binaries").

BRUTAL catches every exception inside its callbacks and queues it in an `ExceptionAggregator` that `Glfw.PollEvents()` rethrows, which is the right design for a boundary where unwinding is unsafe and would carry over unchanged.[^brutal-glfw]

# Assessment

| Criterion | Built-in libglfw.js | contrib.glfw3 |
| --- | --- | --- |
| Needs wasm-tools workload / native relink | yes | yes (+ network, writable cache) |
| Works with .NET 10's Emscripten 3.1.56 | yes (old API) | only the 1.1.0 bundle |
| Missing BRUTAL entry points | 19 | 7 |
| Input fidelity (L/R modifiers, wheel, gamepad, clipboard) | poor | good |
| Loader shim needed in Brutal.Glfw | yes | yes |
| Callback registration works with unmodified Brutal.Glfw (delegates) | no (needs UCO fork) | no (needs UCO fork) |
| Can express BRUTAL fork features (trackpad, scroll phase) | no | no |
| Long-term maintainability | low (frozen at 3.2.1 semantics) | medium (depends on Emscripten cadence vs .NET pin) |

## Open questions

- UNVERIFIED: does Mono's library-name probing let `NativeLibrary.Load("glfw", assembly, DllImportSearchPath.AssemblyDirectory)` hit a pinvoke table named `glfw`? (open in the sibling dotnet-wasm note)
- UNVERIFIED: does the contrib port 1.1.0 (the one bundled with 3.1.56) compile and link inside .NET's frozen Emscripten cache layout, and does .NET's `dotnet.native.js` expose `Module["canvas"]` early enough (`dotnet.create()` returns `Module` per `dotnet.d.ts`, so it could be assigned before `glfwCreateWindow`)?
- Whether RocketWerkz would accept a managed-surface-creation hook upstream (see licensing in [the API surface note](/io-audio/brutal-glfw-and-openal-api-surface.md)).

[^emsc-libglfw]: emscripten-core/emscripten src/lib/libglfw.js (main, Emscripten 6.0.13-git, fetched 2026-10-16)
[^emsc-libglfw-3156]: emscripten-core/emscripten 3.1.56 src/library_glfw.js
[^emsc-contrib-port]: Emscripten 3.1.56 tools/ports/contrib/glfw3.py (bundles contrib.glfw3 1.1.0)
[^emsc-contrib-port-402]: Emscripten 6.0.2 tools/ports/contrib/glfw3.py (bundles contrib.glfw3 3.4.0.20260301)
[^pongasoft-readme]: pongasoft/emscripten-glfw README (features, port options, release notes, Emscripten availability table)
[^pongasoft-usage]: pongasoft/emscripten-glfw docs/Usage.md
[^pongasoft-comparison]: pongasoft/emscripten-glfw docs/Comparison.md (vs built-in libglfw.js)
[^pongasoft-glfw3cpp]: pongasoft/emscripten-glfw src/cpp/glfw3.cpp (exported GLFW API, stubs)
[^pongasoft-window]: pongasoft/emscripten-glfw src/cpp/emscripten/glfw3/Window.cpp (wheel normalisation, cursor modes, pointer lock)
[^pongasoft-portpy]: pongasoft/emscripten-glfw port/glfw3.py (how the port builds and links)
[^dotnet-targets]: dotnet/runtime release/10.0 BrowserWasmApp.targets
[^dotnet-common-targets]: dotnet/runtime release/10.0 WasmApp.Common.targets
[^dotnet-runtime-c]: dotnet/runtime release/10.0 src/mono/browser/runtime/runtime.c (wasm_dl_load, wasm_dl_symbol, get_native_to_interp)
[^dotnet-emsc-version]: dotnet/runtime release/10.0 emscripten-version.txt (3.1.56); main branch file says 6.0.2
[^brutal-glfw]: BRUTAL Brutal.Glfw source (Glfw.cs, Manual/, Generated/)
[^brutal-glfw-init]: Brutal.Glfw Glfw.Initialization.cs (static constructor, NativeLibrary.Load, callbacks)
[^ksa-surface]: KSA Core/Renderer.cs (Glfw.CreateWindowSurface call site, reference only)
[^gap-script]: Entry-point gap analysis
[^sib-fnptr]: Function pointers, UnmanagedCallersOnly and native resolution on .NET 10 browser-wasm (sibling note)
[^sib-native-linking]: Native linking in .NET 10 wasm builds, Emscripten 3.1.56, ports (sibling note)
