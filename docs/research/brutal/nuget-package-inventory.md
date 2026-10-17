---
type: Reference
title: BRUTAL NuGet package inventory (Ahwoo.BRUTAL.*, October 2026)
description: Every published Ahwoo.BRUTAL.* NuGet package with version, size, dependency list, shipped native payload per runtime identifier, analyzers and assembly identity, as the input to the browser-portability decisions.
tags: [brutal, nuget, packages, native-libraries, inventory, rid]
sources:
  - id: nuget-search
    resource: https://azuresearch-usnc.nuget.org/query?q=owner:Ahwoo&take=100&prerelease=true&semVerLevel=2.0.0
    title: NuGet search API, owner:Ahwoo (fetched 2026-10-16)
  - id: nuget-catalog
    resource: https://api.nuget.org/v3/catalog0/data/2026.10.15.04.39.14/ahwoo.brutal.vulkan.2026.10.2.json
    title: NuGet catalog leaf for Ahwoo.BRUTAL.Vulkan 2026.10.2 (one example; all 50 leaves were fetched the same way via each registration leaf's catalogEntry URL)
  - id: catalog-cache
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/catalog/
    title: Local cache of the 50 catalog leaves (packageEntries = file lists with byte sizes; dependencyGroups)
  - id: reg-cache
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/research/references/nuget-registration/
    title: Cached NuGet registration JSON (dependency lists) in the bundle
  - id: vulkan-nuspec
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/nupkg-extract/ahwoo.brutal.vulkan/Ahwoo.BRUTAL.Vulkan.nuspec
    title: Ahwoo.BRUTAL.Vulkan 2026.10.2 nuspec (dependency exclude="Build,Analyzers", repository, release notes)
  - id: asminfo
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/asminfo-full.txt
    title: System.Reflection.Metadata dump of the 31 downloaded package DLLs (assembly name/version, public key, assembly attributes, references, ModuleRefs)
  - id: vulkanex-nm
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/nupkg-extract/ahwoo.brutal.vulkan/runtimes/osx-arm64/native/libVulkanEx.dylib
    title: libVulkanEx.dylib shipped in Ahwoo.BRUTAL.Vulkan (inspected with nm -gU / otool -L)
  - id: brutal-readme
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/README.md
    title: BRUTAL README (library list, "Building from source")
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:16:59Z }
status: draft
---

# Summary

* The `Ahwoo` NuGet owner publishes **52 packages**: 50 `Ahwoo.BRUTAL.*` packages plus `Ahwoo.SessionTickets.Client/Server` (unrelated). Every BRUTAL package has exactly **one version**, published 2026-10-15 between 04:21 and 04:41 UTC, and a single dependency group for **`net10.0`**.[^nuget-search][^catalog-cache] The file lists and dependency groups come from each package's catalog leaf[^nuget-catalog] and agree with the cached registration JSON.[^reg-cache]
* The README's library table maps one source folder to one package, but the feed also has **`Ahwoo.BRUTAL.Render.Common`** and **`Ahwoo.BRUTAL.Render.Debug`**, which have no folder in the source tree. Both DLLs are **empty placeholders**: the only type is `<Module>`.[^asminfo] `Render.Mesh` still depends on `Render.Common`.
* **26 packages ship native code**, always for exactly three RIDs: `win-x64`, `linux-x64` and `osx-arm64`. No package ships `osx-x64`, `linux-arm64`, `win-arm64` or anything for `browser-wasm`/WASI.[^catalog-cache] The other 24 are purely managed.
* The total `.nupkg` size of all 50 packages is about 189 MB. `Slang` alone is 97.6 MB and `Jolt` 21 MB.[^catalog-cache]
* **`Ahwoo.BRUTAL.Vulkan` does ship a native library.** `VulkanEx.dll`, `libVulkanEx.so` and `libVulkanEx.dylib` (11–32 KB) export only `vxDestroyImages`, `vxDestroyImageViews`, `vxDestroyBuffers` and `vxDestroyBufferViews`, and link against `@rpath/libvulkan.1.dylib` on macOS.[^vulkanex-nm] The package does **not** ship the Vulkan loader (`vulkan-1.dll`, `libvulkan.so.1` or `libvulkan.1.dylib`). See [/brutal/vulkan-loader-and-proc-addr-contract.md](/brutal/vulkan-loader-and-proc-addr-contract.md).
* `Ahwoo.BRUTAL.Fmod` ships **no** FMOD binaries; the application must supply `fmod` and `fmodstudio`.[^catalog-cache]
* All package DLLs are **not strong-named** (empty public key). They all carry **AssemblyVersion 1.0.0.0**, and the package version appears only in `AssemblyFileVersion`/`InformationalVersion`, for example `2026.10.2+5dddc87e…`. Every DLL has `[assembly: DisableRuntimeMarshalling]` and `AssemblyConfiguration("Release")`.[^asminfo] Details are in [/brutal/building-from-source.md](/brutal/building-from-source.md).
* Package dependencies on BRUTAL siblings are declared with `exclude="Build,Analyzers"`. A consumer that references `Ahwoo.BRUTAL.Vulkan`, for example, does **not** pick up `Brutal.Core`'s analyzers transitively.[^vulkan-nuspec]

# Inventory table

The native columns list the files under `runtimes/<rid>/native/`. Names joined by `=` are identical-size copies, i.e. Linux and macOS SONAME symlink chains that NuGet flattens into real files.

| Package (`Ahwoo.BRUTAL.`…) | Version | .nupkg KB | Dependencies (net10.0 group) | win-x64 native | linux-x64 native | osx-arm64 native | analyzers / buildTransitive |
|---|---|---|---|---|---|---|---|
| Box2D | 2026.10.1 | 1205 | Core, Numerics | box2d.dll (289 KB) | libbox2d.so = libbox2d.so.3 = libbox2d.so.3.1.1 (367 KB) | libbox2d.3.1.1.dylib = libbox2d.3.dylib = libbox2d.dylib (265 KB) | — |
| Collections | 2026.10.1 | 46 | CommunityToolkit.HighPerformance 8.4.2, Microsoft.Extensions.Logging.Console 10.0.0, Memory | — | — | — | — |
| Concurrency | 2026.10.1 | 42 | — | — | — | — | — |
| Core | 2026.10.1 | 89 | CommunityToolkit.HighPerformance 8.4.2, Microsoft.Extensions.Logging 10.0.0, Microsoft.Extensions.Logging.Console 10.0.0 | — | — | — | Brutal.Core.Analyzers.dll, Brutal.Core.CodeFixes.dll |
| Fmod | 2026.10.3 | 214 | Core, Interop, Numerics, Strings | — | — | — | — |
| FreeType | 2026.10.1 | 4188 | Core, ZLib | freetype.dll (668 KB)<br>png18.dll (205 KB) | libfreetype.so = libfreetype.so.6 = libfreetype.so.6.20.6 (921 KB)<br>libpng18.so = libpng18.so.18 = libpng18.so.18.0.git (304 KB) | libfreetype.6.20.6.dylib = libfreetype.6.dylib = libfreetype.dylib (717 KB)<br>libpng18.18.0.git.dylib = libpng18.18.dylib = libpng18.dylib (238 KB) | Ahwoo.BRUTAL.FreeType.targets |
| Glfw | 2026.10.4 | 858 | Core, Logging, Numerics | glfw3.dll (229 KB) | libglfw.so = libglfw.so.3 = libglfw.so.3.5 (442 KB) | libglfw.3.5.dylib = libglfw.3.dylib = libglfw.dylib (257 KB) | — |
| Gli | 2026.10.1 | 115 | Core, Numerics | gli.dll (51 KB) | libgli.so (69 KB) | libgli.dylib (88 KB) | — |
| Gltf | 2026.10.2 | 101 | Core, Numerics, Texture | — | — | — | — |
| ImGui.Abstractions | 2026.10.2 | 57 | ImGui.Extensions, ImGui, Logging, System.CommandLine 2.0.9 | — | — | — | — |
| ImGui.Backends | 2026.10.2 | 80 | ImGui, CommunityToolkit.HighPerformance 8.4.2, Glfw, OpenGl, Vulkan.Abstractions | — | — | — | — |
| ImGui.Extensions | 2026.10.2 | 38 | ImGui | — | — | — | — |
| ImGui.FreeType | 2026.10.2 | 60 | ImGui, FreeType | imgui_freetype.dll (17 KB) | libimgui_freetype.so (22 KB) | libimgui_freetype.dylib (52 KB) | — |
| ImGui | 2026.10.2 | 2804 | CommunityToolkit.HighPerformance 8.4.2, Core, Localization, Memory, Numerics, Strings | imgui.dll (1124 KB) | libimgui.so (1525 KB) | libimgui.dylib (1240 KB) | — |
| ImPlot | 2026.10.2 | 1374 | ImGui | implot.dll (1706 KB) | libimplot.so (2576 KB) | libimplot.dylib (1774 KB) | — |
| Interop | 2026.10.1 | 38 | Core | — | — | — | Brutal.Interop.SourceGenerators.dll |
| Jolt | 2026.10.2 | 20959 | Core, Interop, Logging, Memory, Numerics, Strings | jolt.dll (1911 KB) | libjolt.so (48537 KB) | libjolt.dylib (3062 KB) | — |
| Ktx | 2026.10.1 | 3433 | Core, Numerics | Ktx.dll (1846 KB) | libKtx.so (2859 KB) | libKtx.dylib (2255 KB) | — |
| Localization | 2026.10.2 | 71 | Core, Logging | — | — | — | — |
| Logging | 2026.10.2 | 36 | Microsoft.Extensions.Logging 10.0.0, Microsoft.Extensions.Logging.Console 10.0.0, Core, Strings | — | — | — | — |
| Maths | 2026.10.1 | 32 | Numerics | — | — | — | — |
| Memory | 2026.10.1 | 34 | Microsoft.Extensions.Logging 10.0.0, Microsoft.Extensions.Logging.Console 10.0.0, Core | — | — | — | — |
| MeshOptimizer | 2026.10.1 | 354 | Core, Interop, Numerics | meshoptimizer.dll (178 KB) | libmeshoptimizer.so (226 KB) | libmeshoptimizer.dylib (188 KB) | — |
| Monitor.Common | 2026.10.2 | 41 | — | — | — | — | — |
| Monitor.Host | 2026.10.2 | 3829 | Monitor.Common, Microsoft.Extensions.Logging.Abstractions 10.0.0, Core, Logging | Brutal.Monitor.Subprocess.exe (2538 KB) | Brutal.Monitor.Subprocess (2653 KB) | Brutal.Monitor.Subprocess (2554 KB) | Ahwoo.BRUTAL.Monitor.Host.targets |
| NativeFileDialog | 2026.10.2 | 93 | Core, Strings | nfd.dll (19 KB) | libnfd.so = libnfd.so.1 = libnfd.so.1.2.1 (32 KB) | libnfd.1.2.1.dylib = libnfd.1.dylib = libnfd.dylib (53 KB) | — |
| NotCurses | 2026.10.0 | 10738 | Core, Interop, Numerics, Strings, ZLib | libncursesw6.dll (625 KB)<br>libunistring-5.dll (5364 KB)<br>libwinpthread-1.dll (61 KB)<br>notcurses-core.dll (683 KB)<br>notcurses-ffi.dll (178 KB) | libncursesw.so = libncursesw.so.6 = libncursesw.so.6.6 (478 KB)<br>libnotcurses-core.so = libnotcurses-core.so.3 = libnotcurses-core.so.3.0.17 (622 KB)<br>libnotcurses-ffi.so = libnotcurses-ffi.so.3 = libnotcurses-ffi.so.3.0.17 (76 KB)<br>libunistring.so = libunistring.so.5 = libunistring.so.5.2.1 (3761 KB) | libncursesw.6.dylib = libncursesw.dylib (321 KB)<br>libnotcurses-core.3.0.17.dylib = libnotcurses-core.3.dylib = libnotcurses-core.dylib (412 KB)<br>libnotcurses-ffi.3.0.17.dylib = libnotcurses-ffi.3.dylib = libnotcurses-ffi.dylib (65 KB)<br>libunistring.5.dylib = libunistring.dylib (2317 KB) | — |
| Numerics | 2026.10.1 | 482 | CommunityToolkit.HighPerformance 8.4.2, Microsoft.Extensions.Logging 10.0.0, Microsoft.Extensions.Logging.Console 10.0.0, Core | — | — | — | Brutal.Numerics.Analyzers.dll, Brutal.Numerics.CodeFixes.dll |
| OpenAl | 2026.10.2 | 6276 | CommunityToolkit.HighPerformance 8.4.2, Core, Interop, Numerics, Strings | OpenAL32.dll (2061 KB) | libopenal.so = libopenal.so.1 = libopenal.so.1.25.2 (2328 KB) | libopenal.1.25.2.dylib = libopenal.1.dylib = libopenal.dylib (1730 KB) | — |
| OpenGl | 2026.10.1 | 292 | Core, Numerics | glad.dll (64 KB) | libglad.so (142 KB) | libglad.dylib (123 KB) | — |
| RakNet | 2026.10.2 | 1895 | CommunityToolkit.HighPerformance 8.4.2, Core, Interop, Strings | RakNetDLL.dll (1276 KB) | libRakNetDLL.so (1434 KB) | libRakNetDLL.dylib (1094 KB) | — |
| Render.Common | 2026.10.1 | 29 | Core | — | — | — | — |
| Render.Debug | 2026.10.1 | 29 | Render.Common | — | — | — | — |
| Render.Mesh | 2026.10.1 | 47 | Render.Common, Collections, Memory, Numerics | — | — | — | — |
| RenderDoc.ImGui | 2026.10.2 | 39 | RenderDoc, ImGui | — | — | — | — |
| RenderDoc | 2026.10.2 | 46 | Core, Logging, Strings | — | — | — | — |
| Sdl.Image | 2026.10.2 | 1085 | Sdl, Core, Interop, Logging | SDL3_image.dll (230 KB) | libSDL3_image.so = libSDL3_image.so.0 = libSDL3_image.so.0.4.4 (355 KB) | libSDL3_image.0.4.4.dylib = libSDL3_image.0.dylib = libSDL3_image.dylib (245 KB) | — |
| Sdl | 2026.10.2 | 8573 | Core, Interop, Logging | SDL3.dll (2635 KB) | libSDL3.so = libSDL3.so.0 = libSDL3.so.0.4.12 (4112 KB) | libSDL3.0.dylib = libSDL3.dylib (2768 KB) | — |
| Sdl.Mixer | 2026.10.2 | 1241 | Sdl, Core, Interop, Logging | SDL3_mixer.dll (274 KB) | libSDL3_mixer.so = libSDL3_mixer.so.0 = libSDL3_mixer.so.0.2.4 (394 KB) | libSDL3_mixer.0.2.4.dylib = libSDL3_mixer.0.dylib = libSDL3_mixer.dylib (333 KB) | — |
| Sdl.Ttf | 2026.10.2 | 6939 | Sdl, Core, Interop, Logging | SDL3_ttf.dll (1556 KB) | libSDL3_ttf.so = libSDL3_ttf.so.0 = libSDL3_ttf.so.0.2.2 (2313 KB) | libSDL3_ttf.0.2.2.dylib = libSDL3_ttf.0.dylib = libSDL3_ttf.dylib (2007 KB) | — |
| ShaderC | 2026.10.2 | 16405 | Interop, Memory, Strings | shaderc_shared.dll (4828 KB)<br>SPIRV-Tools-shared.dll (1592 KB) | libshaderc_shared.so = libshaderc_shared.so.1 (10480 KB)<br>libSPIRV-Tools-shared.so (2101 KB) | libshaderc_shared.1.dylib = libshaderc_shared.dylib (8385 KB)<br>libSPIRV-Tools-shared.dylib (2306 KB) | — |
| Slang | 2026.10.1 | 97599 | CommunityToolkit.HighPerformance 8.4.2, Core, Interop, Strings | slang-compiler.dll (29806 KB)<br>slang-glsl-module.dll (1252 KB)<br>slang-glslang.dll (9617 KB)<br>slang.dll (59 KB) | libslang-compiler.so = libslang-compiler.so.0.2026.7 = libslang.so (30826 KB)<br>libslang-glsl-module-2026.7.so (1263 KB)<br>libslang-glslang-2026.7.so (9598 KB) | libslang-compiler.0.2026.7.dylib = libslang-compiler.dylib = libslang.dylib (26627 KB)<br>libslang-glsl-module-2026.7.dylib (1290 KB)<br>libslang-glslang-2026.7.dylib (8319 KB) | — |
| Stb | 2026.10.2 | 299 | Core, Numerics, Strings | stb.dll (135 KB) | libstb.so (220 KB) | libstb.dylib (191 KB) | — |
| Strings | 2026.10.1 | 77 | CommunityToolkit.HighPerformance 8.4.2, Microsoft.Extensions.Logging 10.0.0, Microsoft.Extensions.Logging.Console 10.0.0, Microsoft.Extensions.ObjectPool 11.0.0-preview.5.26302.115, Collections, Core, Memory | — | — | — | Brutal.Strings.Analyzers.dll, Brutal.Strings.CodeFixes.dll |
| Texture.Abstractions | 2026.10.2 | 46 | Texture | — | — | — | — |
| Texture | 2026.10.2 | 67 | Core, Gli, Interop, Ktx, Numerics, Stb | — | — | — | — |
| Vulkan.Abstractions | 2026.10.2 | 129 | Vulkan, Collections, Logging, Memory, Numerics, Strings | — | — | — | — |
| Vulkan | 2026.10.2 | 1256 | Core, Logging, Memory, Numerics, Strings | VulkanEx.dll (11 KB) | libVulkanEx.so (15 KB) | libVulkanEx.dylib (32 KB) | — |
| Vulkan.Vma | 2026.10.2 | 266 | Vulkan.Abstractions, Vulkan | vma.dll (108 KB) | libvma.so (181 KB) | libvma.dylib (195 KB) | — |
| ZLib | 2026.10.1 | 447 | — | zlib.dll (88 KB) | libzlib.so = libzlib.so.1 = libzlib.so.1.3.2.1 (127 KB) | libzlib.1.3.2.1.dylib = libzlib.1.dylib = libzlib.dylib (104 KB) | Ahwoo.BRUTAL.ZLib.targets |

Notes on selected cells:

* **Core / Numerics / Strings** ship Roslyn analyzers and code fixes. **Interop** ships `Brutal.Interop.SourceGenerators.dll` under `analyzers/dotnet/cs`.[^catalog-cache] Whether their source is in the open tree is covered in [/brutal/building-from-source.md](/brutal/building-from-source.md).
* **FreeType / ZLib / Monitor.Host** ship `buildTransitive/*.targets` files. ZLib and FreeType also ship C headers and a CMake `FindZLIB.cmake` under `build/natives`, for native consumers. **Monitor.Host** ships an executable, `Brutal.Monitor.Subprocess(.exe)`, as a "native" asset.[^catalog-cache]
* Many native packages also ship Windows import libraries (`build/natives/lib/win-x64/*.lib`) for C++ consumers. These are irrelevant to .NET or the browser.[^catalog-cache]
* **Strings** depends on **`Microsoft.Extensions.ObjectPool 11.0.0-preview.5.26302.115`**, a .NET 11 preview package, from a net10.0 library. `Brutal.Logging.dll` also references `Microsoft.Extensions.ObjectPool` assembly version 11.0.0.0 directly.[^nuget-search][^asminfo] Implications are in [/brutal/managed-libraries-portability-audit.md](/brutal/managed-libraries-portability-audit.md).
* **ImGui.Abstractions** pulls `System.CommandLine 2.0.9`. **Monitor.Host** pulls `Microsoft.Extensions.Logging.Abstractions 10.0.0`.[^catalog-cache]

# Native payload versions (from file names / embedded strings)

| Package | Native library | Version evidence |
|---|---|---|
| Glfw | GLFW **3.5.1**, a fork or pre-release: it exports non-upstream `glfwSetScrollDetailCallback`, `glfwSetTrackpadZoomCallback` and `glfwSetTrackpadRotateCallback` | `libglfw.3.5.dylib` version string "3.5.1 Cocoa NSGL Null EGL OSMesa monotonic dynamic"; `nm` shows the trackpad exports |
| OpenAl | **openal-soft 1.25.2** | `libopenal.so.1.25.2`; the dylib contains "OpenAL Soft" and "1.25.2" |
| Sdl | **SDL 3.4.12** | the dylib string `SDL-3.4.12-release-3.4.12`; the package README says "vendors and builds SDL 3.4.12" |
| Sdl.Image / Sdl.Mixer / Sdl.Ttf | SDL3_image (soname 0.4.4), SDL3_mixer (soname 0.2.4), SDL3_ttf (soname 0.2.2) | file names only (UNVERIFIED mapping to release numbers) |
| ShaderC | shaderc (soname 1) + SPIRV-Tools-shared | file names |
| Slang | Slang **2026.7** | `libslang-compiler.so.0.2026.7`, `libslang-glslang-2026.7.so` |
| FreeType | FreeType 2 (so.6.20.6) + libpng18 | file names |
| Box2D | Box2D **3.1.1** | `libbox2d.so.3.1.1` |
| NativeFileDialog | nfd 1.2.1 | `libnfd.so.1.2.1` |
| NotCurses | notcurses **3.0.17** (+ ncursesw 6.6, libunistring 5.2.1) | file names |
| ZLib | zlib 1.3.2.1 | `libzlib.so.1.3.2.1` |
| Vulkan | `VulkanEx` helper only; no loader | `nm -gU` shows only the four `vx*` exports; `otool -L` shows `@rpath/libvulkan.1.dylib`[^vulkanex-nm] |

# Browser relevance at a glance

* Packages with **no native payload** are candidates to run unchanged on `browser-wasm`, subject to their managed platform assumptions: Collections, Concurrency, Core, Gltf, ImGui.Abstractions/Backends/Extensions, Interop, Localization, Logging, Maths, Memory, Monitor.Common, Numerics, Render.Common/Debug/Mesh, RenderDoc(.ImGui), Strings, Texture, Texture.Abstractions, Vulkan.Abstractions, and Fmod (native supplied by the app). Ratings are in [/brutal/library-map-and-portability-matrix.md](/brutal/library-map-and-portability-matrix.md).
* **Vulkan** is managed except for the loader resolution and the tiny `VulkanEx`. Its four `[LibraryImport("vulkan")]` functions plus `vkGetInstanceProcAddr` are the only native entry points. See [/brutal/vulkan-binding-architecture.md](/brutal/vulkan-binding-architecture.md).
* Every other native-backed package (Glfw, OpenAl, Sdl*, ShaderC, Slang, ImGui, ImPlot, Jolt, Box2D, Ktx, Gli, Stb, MeshOptimizer, Vma, FreeType, ZLib, RakNet, NotCurses, NativeFileDialog, OpenGl/glad, Monitor.Host) has **no wasm build**. Each one must be replaced with a browser implementation, recompiled with Emscripten and statically linked into the .NET wasm runtime (see `/dotnet-wasm/`), or avoided.

# Open questions

* UNVERIFIED: whether the upstream per-library repositories (`github.com/RocketWerkz/brutal.*`, named in each nuspec and README) are public. The public monorepo `github.com/ahwootech/BRUTAL` has no project files.[^brutal-readme]
* UNVERIFIED: the exact SDL3_image, SDL3_mixer and SDL3_ttf release numbers. Only SONAMEs were inspected.

[^nuget-search]: NuGet search API, owner:Ahwoo (fetched 2026-10-16)
[^nuget-catalog]: NuGet catalog leaf for Ahwoo.BRUTAL.Vulkan 2026.10.2
[^catalog-cache]: Local cache of the 50 catalog leaves (packageEntries and dependencyGroups)
[^reg-cache]: Cached NuGet registration JSON in the bundle
[^vulkan-nuspec]: Ahwoo.BRUTAL.Vulkan 2026.10.2 nuspec
[^asminfo]: System.Reflection.Metadata dump of the downloaded package DLLs
[^vulkanex-nm]: libVulkanEx.dylib inspected with nm/otool
[^brutal-readme]: BRUTAL README
