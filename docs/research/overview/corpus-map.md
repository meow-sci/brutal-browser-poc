---
type: Reference
title: Corpus map — where the input material lives and what it contains
description: Navigation guide to the BRUTAL source tree, the Ahwoo.BRUTAL NuGet packages, the KSA decompiled assemblies and Linux game build, and the two earlier wrapper projects (ImCode, brutal-imgui-standalone), with the headline facts about each.
tags: [corpus, brutal, ksa, nuget, reference-material]
sources:
  - id: brutal-tree
    resource: /Users/asherwin/repos/meow-sci/BRUTAL
    title: BRUTAL source checkout (commit a25cdf8, origin github.com/ahwootech/BRUTAL)
  - id: nuget-reg
    resource: /references/nuget-registration/
    title: Mirrored NuGet registration metadata for Ahwoo.BRUTAL packages (fetched 2026-10-16)
  - id: nupkg-listing
    resource: "scope: unzip -l of Ahwoo.BRUTAL nupkgs downloaded 2026-10-16"
    title: nupkg content listings
  - id: ksa-decomp
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current
    title: KSA decompiled assemblies (build 2026.10.10.5554)
  - id: ksa-linux
    resource: /Users/asherwin/repos/meow-sci/ksa-linux
    title: KSA Linux game build
  - id: imcode
    resource: /Users/asherwin/repos/meow-sci/ImCode
    title: ImCode repository
  - id: imgui-standalone
    resource: /Users/asherwin/repos/meow-sci/brutal-imgui-standalone
    title: brutal-imgui-standalone repository
generated: { by: claude-fable/5.1, at: 2026-10-16T19:05:00Z }
status: draft
---

# BRUTAL source tree (`/Users/asherwin/repos/meow-sci/BRUTAL`)

- Git checkout of `https://github.com/ahwootech/BRUTAL.git`, HEAD `a25cdf8` ("docs: add gruntz to the projects using brutal"), 10 commits; licence is MIT-based with an attribution requirement (`LICENSE.txt`, `NOTICE.txt`).[^brutal-tree]
- **No `.csproj`, `.sln`, `.props` or `.targets` files anywhere.** The README states project files and build scripts "are coming" and to use the NuGet packages meanwhile; the tree is for reading and forking.[^brutal-tree]
- 50 library folders, one per NuGet package: Box2D, Collections, Concurrency, Core, Fmod, FreeType, Glfw, Gli, Gltf, ImGui (+ Abstractions, Backends, Extensions, FreeType), ImPlot, Interop, Jolt, Ktx, Localization, Logging, Maths, Memory, MeshOptimizer, Monitor.Common, Monitor.Host, NativeFileDialog, NotCurses, Numerics, OpenAl, OpenGl, RakNet, Render.Mesh, RenderDoc (+ ImGui), Sdl (+ Image, Mixer, Ttf), ShaderC, Slang, Stb, Strings, Texture (+ Abstractions), Vulkan, Vulkan.Abstractions, Vulkan.Vma, ZLib.[^brutal-tree]
- `Brutal.Vulkan` is ~1956 files, almost all generated (`Generated/{Structs,Enums,Handles,Extensions,PInvoke}`, `Functions.gen.cs` 15.8k lines, `Globals.gen.cs` type aliases); hand-written parts are `ImportResolver.cs`, `Manual/Classes/{Instance,PhysicalDevice,Device,Queue,CommandBuffer,VulkanHandleWrapper,VkAllocator,VkResultError}.cs` and a few partial structs.[^brutal-tree]
- `Brutal.Vulkan.Abstractions` is ~50 files (DeviceEx, StagingPool, FrameResources, SyncObjects, Presets, VertexInput, DescriptorPoolEx/SetLayoutEx, BufferEx/ImageEx/ImageViewEx, MappedMemory, partitioners, extension methods).[^brutal-tree]
- `docs/` holds only branding logos. Each library carries a `LICENSES/` folder with third-party notices.

Detailed analyses: `/brutal/library-map-and-portability-matrix.md`, `/brutal/vulkan-binding-architecture.md`.

# Ahwoo.BRUTAL NuGet packages

- Publisher profile: https://www.nuget.org/profiles/Ahwoo. IDs are `Ahwoo.BRUTAL.<Library>` (the README's `Ahwoo.Brutal.Core` spelling resolves case-insensitively).
- All packages observed target **net10.0 only**, versions `2026.10.1`–`2026.10.4`, published 2026-10-15; each has a single version so far.[^nuget-reg]
- Assembly names inside packages are `Brutal.<Library>.dll` (e.g. `Brutal.Core.dll`, `Brutal.Vulkan.dll`), different from the KSA game's private build names (`Brutal.Core.Common.dll`, `Brutal.Core.Numerics.dll`, `Brutal.Core.Package.dll`, ...).[^nupkg-listing]
- Native payloads observed (`runtimes/<rid>/native`, RIDs `win-x64`, `linux-x64`, `osx-arm64`):[^nupkg-listing]

| Package | Native payload | Notes |
| --- | --- | --- |
| Ahwoo.BRUTAL.Vulkan 2026.10.2 | `libVulkanEx.dylib` / `libVulkanEx.so` / `VulkanEx.dll` (+ `VulkanEx.lib`) | 34 KB helper that links `@rpath/libvulkan.1.dylib` and exports only `vxDestroyBuffers/BufferViews/Images/ImageViews`; the loader opens it, then resolves `vkGetInstanceProcAddr` through it |
| Ahwoo.BRUTAL.Glfw 2026.10.4 | GLFW 3.5 (`libglfw.3.5.dylib`, `libglfw.so.3.5`, `glfw3.dll`) | |
| Ahwoo.BRUTAL.OpenAl 2026.10.2 | openal-soft 1.25.2 | 6.4 MB package |
| Ahwoo.BRUTAL.ShaderC 2026.10.2 | `shaderc_shared` + `SPIRV-Tools-shared` | 16.8 MB |
| Ahwoo.BRUTAL.Slang 2026.10.1 | slang 2026.7 (`slang`, `slang-compiler`, `slang-glslang`, `slang-glsl-module`) | ~100 MB |
| Ahwoo.BRUTAL.Stb 2026.10.2 | `libstb` | |
| Ahwoo.BRUTAL.Core 2026.10.1 | none; ships `analyzers/dotnet/cs/Brutal.Core.{Analyzers,CodeFixes}.dll` and `docs/brutal-conventions.md` | |
| Ahwoo.BRUTAL.Numerics 2026.10.1 | none; ships analyzers | |
| Memory, Maths, Gltf, Render.Mesh, Vulkan.Abstractions | none (managed only) | |

- Dependency graph headline (from registration metadata):[^nuget-reg] Core → CommunityToolkit.HighPerformance 8.4.2, Microsoft.Extensions.Logging(.Console) 10.0.0. Vulkan → Core, Logging, Memory, Numerics, Strings. Vulkan.Abstractions → Vulkan, Collections, Logging, Memory, Numerics, Strings. Strings → **Microsoft.Extensions.ObjectPool 11.0.0-preview.5.26302.115** (prerelease; consumers must allow prerelease), Collections, Core, Memory. Glfw → Core, Logging, Numerics. OpenAl → Core, Interop, Numerics, Strings, CommunityToolkit. ShaderC → Interop, Memory, Strings. Slang → Core, Interop, Strings. Gltf → Core, Numerics, Texture (→ Gli, Ktx, Stb natives). Render.Mesh → Render.Common, Collections, Memory, Numerics. Concurrency → (none).
- The full registration JSON for 27 packages is mirrored under `/references/nuget-registration/` for offline use.

# KSA decompiled assemblies (`/Users/asherwin/repos/meow-sci/ksa-game-assemblies`)

- `current/version.json`: game build **2026.10.10.5554** (2026-10-07), with changelog entries (e.g. bindless texture library clamped to update-after-bind limits, planet frustum culling push constants reduced to 128 bytes, Brutal.Concurrency update).[^ksa-decomp]
- `current/decomp/`: 6,607 `.cs` files in 196 namespace folders. Game namespaces: `Core`, `Core.Files`, `KSA`, `KSA.Rendering.*` (PBR, Lighting, Particles, Planet, PostProcessing, Raytracing, Rings, Water, Sun, Thumbnails, Atmosphere), `KSA.GlbImport`, `KSA.Networking`, `RenderCore` (+ Animation, Cameras, Gltf, Input, Input.Controllers, Mesh, Pipelines, Shaders, Systems), `Planet.*`. BRUTAL namespaces are decompiled too (`Brutal.VulkanApi`, `Brutal.GlfwApi`, `Brutal.FmodApi.Studio`, ...), which lets us compare the shipped private build with the open-source tree.[^ksa-decomp]
- Rendering bootstrap entry points: `Core/Renderer.cs`, `Core/KSADeviceContextEx.cs`, `Core/PhysicalDeviceExtensions.cs`; shader compilation: `RenderCore.Shaders/ShaderCompilerResolve.cs`, `KSA.AssetReloader/ShaderReloader.cs`.
- Advanced Vulkan usage visible in the game code: bindless descriptor indexing, buffer device address, optional ray tracing / ray query, multiview, dynamic rendering, synchronization2, triangle fans and push constants (grep hits for "DescriptorBuffer" are `VkDescriptorBufferInfo`, not `VK_EXT_descriptor_buffer`) — most of which WebGPU does not offer (see `/webgpu/semantic-gaps-and-emulation.md` and `/webgpu/web-vulkan-profile-proposal.md`).
- `current/dll/`: the game's assemblies (reference only; never to be linked by the PoC).

# KSA Linux build (`/Users/asherwin/repos/meow-sci/ksa-linux`)

- Self-contained .NET app (`KSA`, `KSA.dll`, `libcoreclr.so`, `libfmod*.so`, Brutal.*.dll) plus `Content/`.[^ksa-linux]
- `Content/Core` ("Core" mod) asset types by count: `.ktx2` 268, `.json` 160, `.glb` 144, `.comp` 139, `.glsl` 133, `.dds` 91, `.xml` 80, `.frag` 68, `.png` 54, `.vert` 51, `.wav` 38, `.ogg` 37, `.h` 20, `.toml` 18, plus `.tese/.tesc/.rgen/.rmiss/.rchit/.rahit`. Shaders are GLSL (Vulkan dialect) with `#include`-style `.glsl` headers under `Content/Core/Shaders` and `Content/Shaders`; meshes are GLB; textures mostly KTX2.[^ksa-linux]
- These assets are reference material; the PoC should ship its own freely licensed model (e.g. a Khronos glTF sample) rather than KSA content.

# ImCode (`/Users/asherwin/repos/meow-sci/ImCode`)

- Code editor widget for Dear ImGui on BRUTAL (.NET 10), MIT. Demonstrates consuming the public NuGet packages directly: `Ahwoo.BRUTAL.Core 2026.10.1`, `Ahwoo.BRUTAL.Glfw 2026.10.4`, `Ahwoo.BRUTAL.ImGui 2026.10.2`, `Ahwoo.BRUTAL.ImGui.Backends 2026.10.2`; the sample host uses GLFW + OpenGL.[^imcode]

# brutal-imgui-standalone (`/Users/asherwin/repos/meow-sci/brutal-imgui-standalone`)

- Pre-open-source wrapper that hosted BRUTAL ImGui over GLFW/OpenGL using the *game's* managed assemblies supplied by the consumer. Lessons recorded in its README/NEXT_STEPS: the game's `Brutal.Core.Strings`/`Logging` bind to `Microsoft.Extensions.ObjectPool` assembly version 11.0.0.0 (prerelease package), structural ABI checks of Brutal struct layouts, zig-cross-compiled native ImGui images. Now superseded by the open-source packages (updating it is explicitly out of scope).[^imgui-standalone]

[^brutal-tree]: BRUTAL source checkout (commit a25cdf8, origin github.com/ahwootech/BRUTAL)
[^nuget-reg]: Mirrored NuGet registration metadata for Ahwoo.BRUTAL packages (fetched 2026-10-16)
[^nupkg-listing]: nupkg content listings
[^ksa-decomp]: KSA decompiled assemblies (build 2026.10.10.5554)
[^ksa-linux]: KSA Linux game build
[^imcode]: ImCode repository
[^imgui-standalone]: brutal-imgui-standalone repository
