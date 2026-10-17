---
type: Technology Assessment
title: SDL3 on Emscripten as an all-in-one alternative (strategy A') for windowing, input and audio
description: Evaluates replacing GLFW and OpenAL/FMOD with the SDL3 Emscripten port and the existing Brutal.Sdl and Brutal.Sdl.Mixer bindings, with API, loader, version, audio, Vulkan and ImGui-backend implications.
tags: [sdl3, emscripten, brutal-sdl, sdl-mixer, strategy-a, windowing, input, audio]
sources:
  - id: sdl-emscripten
    resource: https://raw.githubusercontent.com/libsdl-org/SDL/main/docs/README-emscripten.md
    title: SDL docs/README-emscripten.md
  - id: emsc-sdl3-port
    resource: https://raw.githubusercontent.com/emscripten-core/emscripten/main/tools/ports/sdl3.py
    title: Emscripten tools/ports/sdl3.py (SDL 3.4.2 port, -sUSE_SDL=3)
  - id: sdl-emaudio
    resource: https://raw.githubusercontent.com/libsdl-org/SDL/release-3.4.x/src/audio/emscripten/SDL_emscriptenaudio.c
    title: SDL release-3.4.x SDL_emscriptenaudio.c
  - id: sdl-emvideo
    resource: https://raw.githubusercontent.com/libsdl-org/SDL/release-3.4.x/src/video/emscripten/SDL_emscriptenvideo.c
    title: SDL release-3.4.x SDL_emscriptenvideo.c
  - id: emsc-ports-listing
    resource: https://api.github.com/repos/emscripten-core/emscripten/contents/tools/ports?ref=3.1.56
    title: GitHub contents listing of Emscripten tools/ports at tags 3.1.56, 4.0.0 and 4.0.10 (queried 2026-10-16)
  - id: sdlmixer-cmake
    resource: https://raw.githubusercontent.com/libsdl-org/SDL_mixer/main/CMakeLists.txt
    title: SDL_mixer main CMakeLists.txt (EMSCRIPTEN branch)
  - id: brutal-sdl
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Sdl
    title: BRUTAL Brutal.Sdl (Sdl.cs, Manual/, Generated/)
  - id: brutal-sdl-pinvoke
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Sdl/Manual/Sdl.PInvoke.cs
    title: Brutal.Sdl Sdl.PInvoke.cs (resolver and candidates)
  - id: brutal-sdl-notices
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Sdl/LICENSES/BRUTAL-SDL-THIRD-PARTY-NOTICES.txt
    title: Brutal.Sdl third-party notices (SDL 3.4.12)
  - id: brutal-sdl-mixer
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Sdl.Mixer
    title: BRUTAL Brutal.Sdl.Mixer (SdlMixer.cs, Generated/)
  - id: brutal-sdl-mixer-notices
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Sdl.Mixer/LICENSES/BRUTAL-SDL-MIXER-THIRD-PARTY-NOTICES.txt
    title: Brutal.Sdl.Mixer third-party notices (SDL_mixer 3.2.4, stb_vorbis)
  - id: brutal-imgui-backends
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ImGui.Backends/PlatformBackends
    title: Brutal.ImGui.Backends PlatformBackends directory (GlfwBackend only)
  - id: dotnet-common-targets
    resource: https://raw.githubusercontent.com/dotnet/runtime/release/10.0/src/mono/wasm/build/WasmApp.Common.targets
    title: dotnet/runtime release/10.0 WasmApp.Common.targets
  - id: dotnet-emsc-version
    resource: https://raw.githubusercontent.com/dotnet/runtime/release/10.0/src/mono/browser/emscripten-version.txt
    title: dotnet/runtime release/10.0 emscripten-version.txt (3.1.56)
  - id: ksa-decomp
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp
    title: KSA decompiled sources (grep for SdlApi/SdlMixer found no use; reference only)
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:08:04Z }
status: draft
stale_after: 2027-01-31
---

# Summary

SDL3 has an official Emscripten port in Emscripten itself and BRUTAL already ships SDL3 and SDL3_mixer bindings, so "SDL3 for window + input + audio" is the only way to get all three from one native library. It is still a native-relink strategy with the same workload and Emscripten-version prerequisites as GLFW (see [Emscripten GLFW options](/io-audio/emscripten-glfw-options.md)), it removes BRUTAL's only ImGui platform backend, and it offers no Vulkan or WebGPU path. Not recommended for the PoC; recorded as a fallback.

# What exists

- Emscripten's `tools/ports/sdl3.py` is selected by `-sUSE_SDL=3`, fetches `SDL release-3.4.2`, and has a `-mt` (pthread) variant; there is no SDL3_mixer or SDL3_image port in `tools/ports` (only `sdl3` and `sdl3_ttf` for SDL3), although SDL_mixer's own CMake file has an `EMSCRIPTEN` branch (static library only).[^emsc-sdl3-port][^sdlmixer-cmake]
- BRUTAL: `Brutal.Sdl` binds SDL 3.4.12 with 1,190 `[LibraryImport]` functions (events, gamepad, joystick, keyboard, mouse, render, video, plus a manual Vulkan helper and about 55 audio-device functions); `Brutal.Sdl.Mixer` binds SDL_mixer 3.2.4 (`MIX_*` API: tracks, gain, 3D position, groups, callbacks) built with stb_vorbis, dr_flac and dr_mp3 decoders.[^brutal-sdl][^brutal-sdl-notices][^brutal-sdl-mixer][^brutal-sdl-mixer-notices]
- The reference game does not use either (no `SdlApi`/`SdlMixer` references in the decompiled sources).[^ksa-decomp]
- The only BRUTAL ImGui platform backend in this checkout is `GlfwBackend.cs`; there is no SDL backend, so choosing SDL loses the ready-made ImGui integration described in [the API surface note](/io-audio/brutal-glfw-and-openal-api-surface.md).[^brutal-imgui-backends]

# Loader and linking

- `Sdl.PInvoke`'s static constructor registers a resolver for logical name `SDL3` and tries candidates under a lock: Windows `SDL3.dll`, `SDL3`; macOS `libSDL3.dylib`, `SDL3`; everything else `libSDL3.so.0`, `libSDL3.so`, `SDL3`. `OperatingSystem.IsWindows()` and `IsMacOS()` are false on browser, so the "other" list is used and its last entry is the bare `SDL3`.[^brutal-sdl-pinvoke] Unlike Brutal.Glfw and Brutal.OpenAl, nothing here touches `Platform.CurrentFamily`.
- On Mono wasm the pinvoke table is keyed by the `NativeFileReference` file name without extension, and `wasm_dl_load` needs an exact name match; a statically linked archive named `SDL3.a` (not `libSDL3.a`) would therefore match the bare candidate. UNVERIFIED (inferred from `WasmApp.Common.targets` and `runtime.c`; see [the GLFW note](/io-audio/emscripten-glfw-options.md)).[^dotnet-common-targets]
- Version skew: the port is SDL 3.4.2 and `Brutal.Sdl` is generated for 3.4.12. The `tools/ports` listing of Emscripten 3.1.56 (the version .NET 10 pins) contains only `sdl2*` ports, and 4.0.0 also has none for SDL3; `sdl3.py` first appears in the 4.0.10 listing. Using the SDL3 port therefore requires an Emscripten at least 4.0.10, newer than the one .NET 10's workload carries, or building SDL3 out of tree.[^emsc-ports-listing][^emsc-sdl3-port][^dotnet-emsc-version]

# Behaviour in the browser

- Main loop: replace blocking loops with a function registered by `emscripten_set_main_loop`, or use SDL main callbacks, which "default to requestAnimationFrame"; frames appear only after the loop function returns.[^sdl-emscripten]
- Window: the "window" is a canvas; the 2D render API goes through GLES2/WebGL; vsync defaults off and must be requested; canvas elements must have no border or padding. The Emscripten video backend registers only GLES entry points (`Emscripten_GLES_*`); the README does not cover Vulkan or WebGPU.[^sdl-emscripten][^sdl-emvideo]
- Threads: one build is either threaded or not; threaded builds need COOP/COEP headers; rendering stays on the main thread.[^sdl-emscripten]
- Audio: one default device; the driver creates an `AudioContext`, feeds a `ScriptProcessorNode` on the main thread, and while the context is suspended supplies silence so the app keeps making progress, then calls `resume()` once `navigator.userActivation.hasBeenActive` is true.[^sdl-emaudio][^sdl-emscripten] SDL_mixer's built-in stb_vorbis would decode Ogg Vorbis in wasm independent of browser codec support, which sidesteps the Safari question in [browser audio options](/io-audio/browser-audio-options.md), at the cost of building SDL_mixer for Emscripten and relinking.[^brutal-sdl-mixer-notices]

# Assessment

| Criterion | SDL3 route |
| --- | --- |
| Single native library for window, input, audio | yes |
| Needs wasm-tools workload and native relink | yes |
| Emscripten version needed | 4.0.10 or newer for the bundled port (.NET 10 pins 3.1.56) |
| BRUTAL bindings exist | yes (Brutal.Sdl 3.4.12, Brutal.Sdl.Mixer 3.2.4) |
| Reference game uses it | no |
| ImGui platform backend available | no (GLFW only) |
| Vulkan/WebGPU surface | none (GLES only); same managed surface hook needed as for GLFW |
| Audio quality/latency | ScriptProcessorNode on the main thread (deprecated API) |

## Open questions

- UNVERIFIED: the exact first Emscripten release containing `sdl3.py` (between 4.0.0 and 4.0.10 per the listings).
- UNVERIFIED: whether SDL_mixer 3.2.4 builds cleanly with Emscripten as a static library for linking into `dotnet.native.wasm`.
- Whether any target BRUTAL game actually prefers SDL over GLFW.

[^sdl-emscripten]: SDL docs/README-emscripten.md
[^emsc-sdl3-port]: Emscripten tools/ports/sdl3.py (SDL 3.4.2 port, -sUSE_SDL=3)
[^sdl-emaudio]: SDL release-3.4.x SDL_emscriptenaudio.c
[^sdl-emvideo]: SDL release-3.4.x SDL_emscriptenvideo.c
[^emsc-ports-listing]: GitHub contents listing of Emscripten tools/ports at tags 3.1.56, 4.0.0 and 4.0.10 (queried 2026-10-16)
[^sdlmixer-cmake]: SDL_mixer main CMakeLists.txt (EMSCRIPTEN branch)
[^brutal-sdl]: BRUTAL Brutal.Sdl (Sdl.cs, Manual/, Generated/)
[^brutal-sdl-pinvoke]: Brutal.Sdl Sdl.PInvoke.cs (resolver and candidates)
[^brutal-sdl-notices]: Brutal.Sdl third-party notices (SDL 3.4.12)
[^brutal-sdl-mixer]: BRUTAL Brutal.Sdl.Mixer (SdlMixer.cs, Generated/)
[^brutal-sdl-mixer-notices]: Brutal.Sdl.Mixer third-party notices (SDL_mixer 3.2.4, stb_vorbis)
[^brutal-imgui-backends]: Brutal.ImGui.Backends PlatformBackends directory (GlfwBackend only)
[^dotnet-common-targets]: dotnet/runtime release/10.0 WasmApp.Common.targets
[^dotnet-emsc-version]: dotnet/runtime release/10.0 emscripten-version.txt (3.1.56)
[^ksa-decomp]: KSA decompiled sources (grep for SdlApi/SdlMixer found no use; reference only)
