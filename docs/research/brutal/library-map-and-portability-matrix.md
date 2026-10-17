---
type: Code Analysis
title: BRUTAL library map and browser-wasm portability matrix
description: One row per BRUTAL library (all 48 source folders plus the two NuGet-only Render packages) with purpose, NuGet ID/version, dependencies, native library and loader, shipped RIDs, and a GREEN/YELLOW/RED browser-wasm rating with the concrete action (as-is, patch, replace, wasm-native, drop).
tags: [brutal, portability, matrix, wasm, browser, native-libraries, nuget]
sources:
  - id: brutal-tree
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/
    title: BRUTAL open-source tree (HEAD a25cdf8, 2026-10-16)
  - id: inventory
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/research/brutal/nuget-package-inventory.md
    title: NuGet package inventory (versions, deps, native payloads)
  - id: deps
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/catalog/deps-all.txt
    title: Dependency list extracted from the 50 NuGet catalog leaves
  - id: asminfo
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/asminfo-full.txt
    title: Metadata dump (ModuleRefs = native library names, IVT, ModuleInitializer)
  - id: managed-audit
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/research/brutal/managed-libraries-portability-audit.md
    title: Managed libraries portability audit
  - id: vulkan-arch
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/research/brutal/vulkan-binding-architecture.md
    title: Brutal.Vulkan binding architecture
  - id: wia
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/research/brutal/windowing-input-audio-apis.md
    title: Windowing/input/audio APIs
  - id: assets
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/research/brutal/asset-libraries.md
    title: Asset libraries
  - id: platform
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Core/Platform.cs
    title: Brutal.Core Platform.cs
  - id: import-resolver
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/ImportResolver.cs
    title: Brutal.Vulkan ImportResolver.cs
  - id: openal-loader
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.OpenAl/Manual/NativeLoader.cs
    title: Brutal.OpenAl NativeLoader.cs ([ModuleInitializer])
  - id: box2d
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Box2D/Box2D.cs
    title: Brutal.Box2D loader ("box2dd" in DEBUG, "box2d" in Release)
  - id: imgui-interop
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ImGui/Manual/Interop.cs
    title: Brutal.ImGui Interop.CreateTrampoline (NativeLibrary.Load("imgui"))
  - id: shaderc
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ShaderC/ShaderC.cs
    title: Brutal.ShaderC static ctor (shaderc_shared / shaderc_shared.1)
  - id: slang
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Slang/Slang.cs
    title: Brutal.Slang loader
  - id: renderdoc
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.RenderDoc/Manual/RenderDoc.manual.cs
    title: Brutal.RenderDoc (NativeLibrary.TryLoad of renderdoc)
  - id: monitor
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Monitor.Common/CrashTail.cs
    title: Brutal.Monitor.Common (MemoryMappedFile, threads) / Monitor.Host (subprocess)
  - id: scan
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/
    title: Per-library grep scan (OperatingSystem/Platform, NativeLibrary, threads, UnmanagedCallersOnly, delegate marshalling, file IO, [InteropHandle]) recorded in this session
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:16:59Z }
status: draft
---

# Headline

The ratings below come from reading every library folder of the open tree[^brutal-tree] and the 50 published packages. Counts are per NuGet package (50).

* **GREEN, run on browser-wasm as-is (10 packages):** Collections, Interop, Memory, Strings, Numerics (performance caveat for `double4x4`), Maths, Render.Mesh, Render.Common and Render.Debug (both empty placeholders), and **Vulkan.Abstractions**. Abstractions needs no change of its own; it only needs a patched Brutal.Vulkan underneath, and its `OperatingSystem.Is*` uses are benign on browser.[^managed-audit][^vulkan-arch]
* **YELLOW, managed but needs a small patch, a host-side change, or care (8):** **Core** (`Platform` static ctor throws), **Vulkan** (loader static ctor throws; the 4 global P/Invokes and optionally the function-pointer calling convention need patching), **Logging** (the host must not use the console provider), **Concurrency** (ThreadPool + busy-wait hangs single-threaded; needs an inline mode), **Localization** (file IO only), **Gltf** (reflection-based System.Text.Json; path-based IO; texture decode is native), **Texture** and **Texture.Abstractions** (managed dispatchers; decoding is native).[^managed-audit][^assets]
* **RED, native-backed with no wasm binary shipped, or managed but useless without one (32):** Glfw, OpenAl, Sdl, Sdl.Image, Sdl.Mixer, Sdl.Ttf, ShaderC, Slang, ImGui, ImGui.FreeType, ImPlot, ImGui.Backends, ImGui.Abstractions/Extensions (transitively), Jolt, Box2D, Ktx, Gli, Stb, MeshOptimizer, Vulkan.Vma, FreeType, ZLib, RakNet, NotCurses, NativeFileDialog, OpenGl, Fmod, RenderDoc(+ImGui) (degrades to "not available"), and Monitor.Common/Host (subprocess, memory-mapped files, threads). Every shipped native payload targets only win-x64, linux-x64 and osx-arm64.[^inventory]
* The PoC-critical path is **Core, Memory, Collections, Strings, Logging, Numerics, Vulkan, Vulkan.Abstractions, Gltf (+ Texture assemblies)**, plus browser replacements for Glfw and OpenAl. All of it except Glfw and OpenAl is managed. The Core-to-Vulkan.Abstractions closure was verified to build from source in [/brutal/building-from-source.md](/brutal/building-from-source.md); Gltf and Texture were not test-built.

# Matrix

Columns: **Native** gives the `[LibraryImport]`/`DllImport` library name(s) (ModuleRefs in the shipped DLL)[^asminfo] and how they are resolved. **RIDs** = `w` win-x64, `l` linux-x64, `m` osx-arm64. The **Action** column uses: *as-is*; *patch* (source-build with small changes); *host* (application-side change only); *replace* (API-compatible browser assembly); *wasm-native* (compile the C/C++ library with Emscripten and link it into the .NET wasm runtime); *drop* (not needed or not possible in the browser).

| Library | Purpose | NuGet (`Ahwoo.BRUTAL.`…) | BRUTAL deps / 3rd-party deps[^deps] | Native lib & loader | RIDs | Rating | Action / notes |
|---|---|---|---|---|---|---|---|
| Core | pointers (`Ptr`, `PtrPtr`), `ByteSize`/`ElementCount`/`Bool32`, enum utilities, `Platform`, `[Fixed]` | Core 2026.10.1 | — / CommunityToolkit.HighPerformance 8.4.2, M.E.Logging(.Console) 10.0.0 (unused) | none | — | YELLOW | **patch** `Platform` (`Platform.cs:24–34` throws on browser) **only if** something touches it: `VaList` (ImGui), the OpenAl resolver, KSA-style host code. `ByteSize` is 32-bit on wasm. Ships analyzers (BRUTALCORE0003…0012). C# 14 extension operators[^platform][^managed-audit] |
| Interop | `InteropStringMarshaller`, `InteropHandleMarshaller`, `IInteropHandle` | Interop 2026.10.1 | Core | none | — | GREEN | as-is. Ships `Brutal.Interop.SourceGenerators.dll` (`[InteropHandle]` generator; **source not in the tree**) |
| Memory | `Mem` over `NativeMemory`, `PtrOwner<T>` | Memory 2026.10.1 | Core / M.E.Logging(.Console) | none | — | GREEN | as-is |
| Collections | `NativeList(<T>)`, `NativeStrideList`, `ScratchSpan`, `Name`/`Namespace` | Collections 2026.10.1 | Memory / CommunityToolkit, M.E.Logging.Console | none | — | GREEN | as-is |
| Strings | `String8`, `RefString8`, `StringBuilder<T>`, UTF helpers | Strings 2026.10.1 | Collections, Core, Memory / CommunityToolkit, M.E.Logging(.Console), **M.E.ObjectPool 11.0.0-preview.5** | none | — | GREEN | as-is (ObjectPool binds its netstandard2.0 asset). IVT `Brutal.Strings.Tests` |
| Logging | `LogSystem`, `LogCategory` (interpolated-handler, runtime-filtered) | Logging 2026.10.2 | Core, Strings / M.E.Logging(.Console) 10.0.0 | none | — | YELLOW | **host**: do not call `AddConsole` on browser (its processor starts a `Thread`); use a synchronous provider |
| Numerics | `float2..4`, `double*`, `int*`…, `float4x4`/`double4x4`, quaternions, planes, rects, `Color` | Numerics 2026.10.1 | Core / CommunityToolkit, M.E.Logging(.Console) | none | — | GREEN | as-is. Portable `Vector128/256/512` only; `double4x4 *` uses unconditional `Vector256` (slow emulation). Ships analyzers |
| Maths | `Random` extensions | Maths 2026.10.1 | Numerics | none | — | GREEN | as-is |
| Concurrency | `JobScheduler`/`JobRunner` | Concurrency 2026.10.1 | — | none | — | YELLOW (RED single-threaded) | **patch**: inline execution mode, since `ThreadPool.UnsafeQueueUserWorkItem` + busy-spin `Wait()` hangs on single-threaded wasm |
| Localization | `LString`, `LStrings`, ResX reader/writer | Localization 2026.10.2 | Core, Logging | none | — | YELLOW | as-is with files in the VFS (`ResXReader(string)` uses `StreamReader`) |
| Vulkan | full Vulkan bindings: 1,486 structs, 814 function-pointer types, ~900 wrappers; `Instance`/`Device`/`Queue` | Vulkan 2026.10.2 | Core, Logging, Memory, Numerics, Strings / CommunityToolkit | `vulkan`: 4 `[LibraryImport]` + `vkGetInstanceProcAddr` via `NativeLibrary.GetExport`. Static ctor loads `vulkan-1` (Win, System32), **`VulkanEx`** (macOS), `vulkan` (Linux), else throws. Package ships `VulkanEx` (vx* batch-destroy helpers), **no Vulkan loader** | w l m (VulkanEx only) | YELLOW | **patch** `ImportResolver.cs` (and the `VulkanApiEx.gen.cs` globals) so a C# Vulkan-on-WebGPU implementation is used; optionally rewrite `delegate* unmanaged[Cdecl]` to managed `delegate*`. IVT → Vulkan.Abstractions[^import-resolver][^vulkan-arch] |
| Vulkan.Abstractions | `DeviceEx`, `BufferEx`, `ImageEx`, `MappedMemory`, `StagingPool`, descriptor wrappers, `Presets`, `VertexInput`, partitioners | Vulkan.Abstractions 2026.10.2 | Vulkan, Collections, Logging, Memory, Numerics, Strings | none | — | GREEN | as-is over a patched Vulkan (source-build together because it uses Vulkan internals) |
| Vulkan.Vma | Vulkan Memory Allocator bindings (`VmaAllocatorEx`) | Vulkan.Vma 2026.10.2 | Vulkan.Abstractions, Vulkan | `vma` (default probing) | w l m | RED | **drop**, or reimplement the `IBufferAllocator`/`IImageAllocator` facade in C# over the browser implementation; VMA C++ would call Vulkan through the C ABI, which does not exist in the browser. KSA uses it heavily |
| Glfw | windowing and input (GLFW **3.5.1** fork) | Glfw 2026.10.4 | Core, Logging, Numerics | `glfw` (`glfw3` on Windows): `static Glfw()` → `NativeLibrary.Load` + resolver; 154 entry points, each with `Log.Trace()`; delegate callbacks | w l m | RED | **replace** with an API-compatible `Brutal.Glfw` on DOM events[^wia] |
| OpenAl | OpenAL Soft 1.25.2 bindings | OpenAl 2026.10.2 | Core, Interop, Numerics, Strings / CommunityToolkit | `openal`: **`[ModuleInitializer]`** resolver → `Platform.CurrentFamily` → `OpenAL32.dll`/`libopenal.1.dylib`/`libopenal.so.1` | w l m | RED | **replace** with a WebAudio-backed `Brutal.OpenAl`, or Emscripten OpenAL (wasm-native, UNVERIFIED). IVT to its test assemblies (build-added)[^openal-loader] |
| Sdl | SDL 3.4.12 full bindings | Sdl 2026.10.2 | Core, Interop, Logging | `SDL3`: lazy resolver with per-OS candidates; ~1,194 `Log.Trace()` | w l m | RED | wasm-native (SDL3 has an Emscripten port; UNVERIFIED with .NET) or drop |
| Sdl.Image / Sdl.Mixer / Sdl.Ttf | SDL3_image / SDL3_mixer (MIX_*) / SDL3_ttf | 2026.10.2 each | Sdl, Core, Interop, Logging | `SDL3_image` / `SDL3_mixer` / `SDL3_ttf` (resolvers) | w l m | RED | as Sdl |
| ShaderC | runtime GLSL/HLSL → SPIR-V | ShaderC 2026.10.2 | Interop, Memory, Strings | `shaderc`: static ctor loads `shaderc_shared` (`shaderc_shared.1` on macOS) + resolver | w l m | RED | **drop** in the browser (compile offline), or a wasm shader compiler (see `/shaders/`)[^shaderc] |
| Slang | Slang 2026.7 compiler bindings | Slang 2026.10.1 | Core, Interop, Strings / CommunityToolkit | `slang`: `NativeLibrary.Load("slang")` + resolver | w l m (97.6 MB pkg) | RED | **drop** (offline compile)[^slang] |
| ImGui | Dear ImGui (docking) bindings, 1,146 imports | ImGui 2026.10.2 | Core, Localization, Memory, Numerics, Strings / CommunityToolkit | `imgui`; `Interop.CreateTrampoline` also `NativeLibrary.Load("imgui")` for viewport callbacks; `VaList` uses `Platform.IsLinux` and 8-byte vararg slots | w l m | RED | wasm-native (cimgui-style build) + VaList fix, or drop for the PoC[^imgui-interop] |
| ImGui.FreeType | FreeType font loader for ImGui | ImGui.FreeType 2026.10.2 | ImGui, FreeType | `imgui_freetype` | w l m | RED | drop |
| ImGui.Extensions / ImGui.Abstractions | helpers, console window (`System.CommandLine 2.0.9` dep) | 2026.10.2 | ImGui (+Logging, System.CommandLine) | none (but calls ImGui) | — | RED (transitive) | follows ImGui |
| ImGui.Backends | GLFW platform backend + Vulkan and OpenGL renderer backends | ImGui.Backends 2026.10.2 | ImGui, Glfw, OpenGl, Vulkan.Abstractions / CommunityToolkit | `glfw`, `glfw3`, `user32.dll` | — | RED | Vulkan renderer backend is managed and would run on the browser Vulkan implementation; platform backend needs the Glfw replacement |
| ImPlot | ImPlot bindings | ImPlot 2026.10.2 | ImGui | `implot` | w l m | RED | follows ImGui |
| Jolt | Jolt Physics bindings (3,237 imports, 269 `[InteropHandle]` files, 28 `[UnmanagedCallersOnly]`, threads) | Jolt 2026.10.2 | Core, Interop, Logging, Memory, Numerics, Strings | `jolt` | w l m (libjolt.so 48 MB) | RED | wasm-native possible in principle (JoltPhysics.js exists); out of PoC scope |
| Box2D | Box2D 3.1.1 bindings | Box2D 2026.10.1 | Core, Numerics | `box2d` (`box2dd` under `#if DEBUG`): static ctor `NativeLibrary.Load` + resolver | w l m | RED | wasm-native (C99) or drop[^box2d] |
| Ktx | KTX-Software bindings | Ktx 2026.10.1 | Core, Numerics | `Ktx` (default probing) | w l m | RED | wasm-native or browser-side transcode |
| Gli | GLI (DDS/KTX1) | Gli 2026.10.1 | Core, Numerics | `gli` (default probing) | w l m | RED | drop or wasm-native |
| Stb | stb_image | Stb 2026.10.2 | Core, Numerics, Strings | `stb` (default probing) | w l m | RED | wasm-native (trivial) or `createImageBitmap` |
| Texture | `TextureLoader` dispatch to Stb/Ktx/Gli; `ITexture` | Texture 2026.10.2 | Core, Gli, Interop, Ktx, Numerics, Stb | none directly | — | YELLOW | assembly needed by Gltf; decoding needs natives |
| Texture.Abstractions | format and layout helpers, upload abstractions | Texture.Abstractions 2026.10.2 | Texture | none | — | YELLOW | as Texture |
| Gltf | glTF 2.0 schema + GLB/GLTF loader (System.Text.Json) | Gltf 2026.10.2 | Core, Numerics, Texture | none | — | YELLOW | as-is (VFS + reflection JSON); avoid `GetOrLoadImage`[^assets] |
| MeshOptimizer | meshoptimizer bindings | MeshOptimizer 2026.10.1 | Core, Interop, Numerics | `meshoptimizer` | w l m | RED | wasm-native (easy) or drop |
| Render.Mesh | `Mesh<TKey>`, `ProcGenMesh` primitives | Render.Mesh 2026.10.1 | Render.Common, Collections, Memory, Numerics | none | — | GREEN | as-is |
| Render.Common / Render.Debug | **no source folder; DLLs contain no types** | 2026.10.1 | Core / Render.Common | none | — | GREEN | placeholders |
| FreeType | FreeType 2 (+libpng) | FreeType 2026.10.1 | Core, ZLib | `freetype` | w l m | RED | **no C# source in the tree** (folder holds only LICENSES); drop |
| ZLib | zlib 1.3.2.1 | ZLib 2026.10.1 | — | `zlib` | w l m | RED | **no C# source in the tree**; use `System.IO.Compression` instead |
| RakNet | RakNet networking | RakNet 2026.10.2 | Core, Interop, Strings / CommunityToolkit | `RakNetDLL` | w l m | RED | drop (no UDP in browsers) |
| NotCurses | terminal UI | NotCurses 2026.10.0 | Core, Interop, Numerics, Strings, ZLib | `notcurses-core`, `notcurses-ffi` | w l m | RED | drop |
| NativeFileDialog | OS file dialogs | NativeFileDialog 2026.10.2 | Core, Strings | `nfd` | w l m | RED | drop (use `<input type=file>`) |
| OpenGl | glad-based OpenGL bindings | OpenGl 2026.10.1 | Core, Numerics | `glad` | w l m | RED | drop |
| Fmod | FMOD Core + Studio bindings (KSA's audio) | Fmod 2026.10.3 | Core, Interop, Numerics, Strings | `fmod`, `fmodstudio` (Studio does `NativeLibrary.Load("fmod")`); **no natives shipped** | none | RED | drop for the PoC (FMOD HTML5 exists; licensing) |
| RenderDoc / RenderDoc.ImGui | RenderDoc in-app API | RenderDoc 2026.10.2 | Core, Logging, Strings (/ ImGui) | `NativeLibrary.TryLoad(renderdoc path)`, returns false when absent | — | RED (benign) | drop. `TryLoad` fails gracefully[^renderdoc] |
| Monitor.Common / Monitor.Host | crash monitor (subprocess, memory-mapped crash tail, reader thread) | 2026.10.2 | Monitor.Common, Core, Logging / M.E.Logging.Abstractions | ships `Brutal.Monitor.Subprocess` executables | w l m | RED | drop[^monitor] |

# Cross-cutting facts behind the ratings

* **Assembly-level:** every BRUTAL DLL is AssemblyVersion 1.0.0.0, not strong-named, and carries `[assembly: DisableRuntimeMarshalling]`. Only **Brutal.OpenAl** has a `[ModuleInitializer]`. The only `InternalsVisibleTo` between shipped libraries is Vulkan → Vulkan.Abstractions; the others target test assemblies.[^asminfo]
* **Platform touch points:** `Brutal.Core.Platform` is consumed only by `Brutal.ImGui/Core/VaList.cs:173` and `Brutal.OpenAl/Manual/NativeLoader.cs:31`. `OperatingSystem.Is*` appears in the Vulkan loader, Glfw, Sdl*, ShaderC, ImGui.Backends, Monitor.Host and (benignly) Vulkan.Abstractions.[^scan][^platform]
* **Threads:** Concurrency (ThreadPool), Jolt, Monitor.Common/Host. Nothing on the Vulkan path creates threads; `Queue` uses a `Lock`.[^scan]
* **Native→managed callbacks:** `[UnmanagedCallersOnly]` (wasm-friendlier) appears in OpenAl, Jolt, ImGui.Backends, Sdl, Sdl.Mixer, ShaderC and Vulkan.Abstractions (debug messenger callbacks). Delegate marshalling via `[UnmanagedFunctionPointer]`/`GetFunctionPointerForDelegate` appears in Glfw, ImGui, ImPlot, Box2D, Fmod, Ktx, OpenGl, Sdl and Sdl.Mixer.[^scan]
* **Source-generator dependency:** packages using `[InteropHandle]` (Jolt 269 files, NotCurses 19, Fmod 2, ShaderC 2, OpenAl 1, Sdl 1, Sdl.Image 1, Sdl.Mixer 1, Sdl.Ttf 1, Slang 1) need the binary-only `Brutal.Interop.SourceGenerators.dll` to build from source.[^scan]

# `GlobalUsings.cs` per library (compile-time dependency hints)

None of these files imports `System` or `System.Collections.Generic`, so every project must enable **SDK implicit usings**. The shipped PDBs confirm `ImplicitUsings` was on, and all libraries need `AllowUnsafeBlocks` and LangVersion 14 (see [/brutal/building-from-source.md](/brutal/building-from-source.md)).[^brutal-tree][^managed-audit]

| Library | Notable global usings | Implied compile references |
|---|---|---|
| Core | `System.Diagnostics.Contracts`, `System.Runtime.CompilerServices` | framework only + CommunityToolkit |
| Strings | `Brutal.Memory`, `Brutal.Pointers(.Extensions)`, `Brutal.Strings.Extensions`, `CommunityToolkit.HighPerformance`; aliases `Char8 = byte`, `String8View = ReadOnlySpan<byte>`, `StringBuilder8/16`, `ValueStringBuilder8/16`; `[assembly: InternalsVisibleTo("Brutal.Strings.Tests")]` | Core, Memory, Collections, CommunityToolkit |
| Numerics | `System.Numerics`, `System.Runtime.Intrinsics`, `System.Text.Unicode`, **`System.Xml.Serialization`** (attributes only) | Core |
| Vulkan | `Brutal.Logging`, `Brutal.Memory`, `Brutal.Numerics`, `Brutal.Pointers(.Extensions)`, `Brutal.Strings`, `CommunityToolkit.HighPerformance.Buffers` | Core, Logging, Memory, Numerics, Strings, CommunityToolkit |
| Vulkan.Abstractions | as Vulkan + `Brutal.Collections`, `CommunityToolkit.HighPerformance` | + Vulkan, Collections |
| Vulkan.Vma | `System.Runtime.InteropServices`, `Brutal.Pointers` | Vulkan, Abstractions, Core |
| Glfw | `static Brutal.GlfwApi.Glfw.Constants`, `Brutal.Numerics`, `CommunityToolkit.HighPerformance` | Core, Numerics, Logging |
| OpenAl | `System.Text`, `Brutal`, `Brutal.Numerics`, `CommunityToolkit.HighPerformance` | Core, Interop, Numerics, Strings |
| Gltf | `Brutal.Numerics` | Core, Numerics, Texture |
| ImGui | `Brutal.ImGuiApi.(Internal.)InlineArrays`, `Brutal.Numerics`, `Brutal.Pointers(.Extensions)`, `Brutal.Strings`; aliases `PInvoke = Brutal.ImGuiApi.Internal.PInvoke`, `Char8/Char16/String8View/StringBuilder8` | Core, Memory, Numerics, Strings, Localization |
| ImPlot / ImGui.Abstractions / ImGui.Extensions / ImGui.FreeType / RenderDoc.ImGui | ImGui namespaces + Numerics/Pointers/Strings | ImGui |
| Jolt | `Brutal.Memory`, `Numerics`, `Pointers`, `Strings` + about 60 generated type aliases (`Real = float`, `RVec3 = float3`, `RMat44 = float4x4`, …) | Core, Interop, Logging, Memory, Numerics, Strings |
| Slang | generated aliases (`Bool = Brutal.Bool8`, `unsafe DiagnosticCallback = delegate* unmanaged[Cdecl]<…>`, …) | Core, Interop, Strings |
| Sdl / Sdl.Image / Sdl.Mixer / Sdl.Ttf | `System.Runtime.InteropServices`; the add-ons use `Brutal.SdlApi` | Sdl, Core, Interop, Logging |
| ShaderC | `Brutal.Strings` | Interop, Memory, Strings |
| Fmod | `Brutal.Interop`, `Brutal.Numerics`, `Brutal.Strings` | Core, Interop, Numerics, Strings |
| RenderDoc | `Brutal.Numerics`, `Brutal.Strings` | Core, Logging, Strings |
| MeshOptimizer | `System.Runtime.InteropServices` | Core, Interop, Numerics |
| *(no GlobalUsings.cs)* | Box2D, Collections, Concurrency, Gli, Interop, Ktx, Localization, Logging, Maths, Memory, Monitor.*, NativeFileDialog, NotCurses, OpenGl, RakNet, Render.Mesh, Stb, Texture, Texture.Abstractions, ImGui.Backends; FreeType and ZLib have no sources at all | — |

# Open questions

* UNVERIFIED: whether any statically linked Emscripten build of a RED library (stb, meshoptimizer, box2d, SDL3, OpenAL) binds to these `[LibraryImport]` names on .NET 10 browser-wasm without patching the assemblies' custom resolvers. See `/dotnet-wasm/`.
* The FreeType and ZLib C# sources and the Render.Common/Render.Debug projects are absent from the public tree. Only the NuGet binaries exist.

[^brutal-tree]: BRUTAL open-source tree
[^inventory]: NuGet package inventory
[^deps]: Dependency list from catalog leaves
[^asminfo]: Metadata dump
[^managed-audit]: Managed libraries portability audit
[^vulkan-arch]: Brutal.Vulkan binding architecture
[^wia]: Windowing/input/audio APIs
[^assets]: Asset libraries
[^platform]: Brutal.Core Platform.cs
[^import-resolver]: Brutal.Vulkan ImportResolver.cs
[^openal-loader]: Brutal.OpenAl NativeLoader.cs
[^box2d]: Brutal.Box2D loader
[^imgui-interop]: Brutal.ImGui Interop.cs
[^shaderc]: Brutal.ShaderC static ctor
[^slang]: Brutal.Slang loader
[^renderdoc]: Brutal.RenderDoc TryLoad
[^monitor]: Brutal.Monitor
[^scan]: Per-library grep scan
