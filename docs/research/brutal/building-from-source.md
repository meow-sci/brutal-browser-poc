---
type: Recipe
title: Building BRUTAL libraries from source (csproj reconstruction, identity, drop-in replacement) — verified
description: How to rebuild Brutal.Vulkan, Brutal.Vulkan.Abstractions and the managed core libraries from the open-source tree without upstream project files, what assembly identity the NuGet DLLs have (no strong name, AssemblyVersion 1.0.0.0), how a patched build replaces the NuGet one, and how this differs from KSA's private BRUTAL build.
tags: [brutal, build, csproj, msbuild, strong-naming, assembly-identity, fork, vulkan]
sources:
  - id: brutal-readme
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/README.md
    title: BRUTAL README ("Project files, a solution and build scripts are coming")
  - id: asminfo
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/asminfo-full.txt
    title: Metadata dump of all downloaded NuGet DLLs (identity, attributes, references)
  - id: asminfo-tool
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/tools/asminfo/Program.cs
    title: System.Reflection.Metadata inspector used for the dump
  - id: vulkan-csproj
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/build/Brutal.Vulkan/Brutal.Vulkan.csproj
    title: Reconstructed Brutal.Vulkan.csproj (built OK)
  - id: abstr-csproj
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/build/Brutal.Vulkan.Abstractions/Brutal.Vulkan.Abstractions.csproj
    title: Reconstructed Brutal.Vulkan.Abstractions.csproj (built OK)
  - id: full-closure
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/build/full/
    title: Generated csprojs for the full managed closure (Core, Memory, Collections, Strings, Logging, Numerics, Maths, Interop, Concurrency, Localization, Vulkan, Vulkan.Abstractions) — all built OK
  - id: smoke
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/build/smoke/Program.cs
    title: Desktop smoke test (NuGet Abstractions + swapped source-built Brutal.Vulkan) — ran OK on Apple M4 Pro / MoltenVK
  - id: vulkan-nuspec
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/nupkg-extract/ahwoo.brutal.vulkan/Ahwoo.BRUTAL.Vulkan.nuspec
    title: Ahwoo.BRUTAL.Vulkan nuspec
  - id: assemblyinfo
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/AssemblyInfo.cs
    title: Brutal.Vulkan/AssemblyInfo.cs (InternalsVisibleTo)
  - id: managed-report
    resource: /Users/asherwin/.claude/projects/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/tool-results/toolu_01Mh5ZCpy1B57UGyMQb6ZVqe.txt
    title: Managed-library audit report (PDB-derived compiler options, analyzer/generator inventory)
  - id: ksa-csproj
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/Brutal.Vulkan.csproj
    title: KSA decompiled Brutal.Vulkan.csproj (references Brutal.Core.Common etc.; reference only)
  - id: ksa-dll
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/dll/Brutal.Vulkan.dll
    title: KSA's Brutal.Vulkan.dll (AssemblyVersion 2026.9.0.0; reference only)
  - id: ksa-version
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/version.json
    title: KSA build 2026.10.10.5554 (2026-10-07)
  - id: fork
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/build/vulkan-fork/
    title: Scratch fork of Brutal.Vulkan with VulkanLoaderOverride + patched CreateInstanceInternal (compiles)
  - id: fakeicd
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/build/fakeicd/Program.cs
    title: Fake C# ICD test program exercising the loader override (ran OK on desktop CoreCLR)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:16:59Z }
status: draft
---

# Bottom line

* The public tree has **no `.csproj`, `.sln`, `Directory.Build.props` or build scripts**. The README says "Project files, a solution and build scripts are coming. Until then use the NuGet packages".[^brutal-readme] The tree does contain the **complete sources** for the shipped DLLs: the PDB document lists match the tree file-for-file, apart from SDK-generated files.[^managed-report]
* **Reconstructing project files works, and was verified.** With the .NET SDK **10.0.100** installed on this machine, the template below compiled **Brutal.Vulkan** in about 3 s with 0 warnings and 0 errors. The output has the same type count as NuGet (2,958 types) and almost the same size (1,558,528 vs 1,559,552 bytes). **Brutal.Vulkan.Abstractions** compiled against it with 0 warnings and errors (117 vs 116 types). So did the **whole managed closure** (Core, Memory, Collections, Strings, Logging, Numerics, Maths, Interop, Concurrency, Localization, Vulkan, Vulkan.Abstractions), using ProjectReferences only.[^vulkan-csproj][^abstr-csproj][^full-closure]
* **The NuGet DLLs are not strong-named**: public key length 0, token null, CLI flags show no StrongNameSigned. Every BRUTAL assembly has **AssemblyVersion 1.0.0.0**; the package version lives only in `AssemblyFileVersion` (e.g. `2026.10.2.0`) and `AssemblyInformationalVersion` (`2026.10.2+<commit>`).[^asminfo] These values were read with a small System.Reflection.Metadata inspector.[^asminfo-tool] Binding is therefore by simple name plus version 1.0.0.0, so a source-built `Brutal.Vulkan.dll` with AssemblyVersion 1.0.0.0 is a **drop-in replacement**.
* **Verified at runtime:** a console app that references NuGet `Ahwoo.BRUTAL.Vulkan.Abstractions` 2026.10.2 ran unchanged after its `Brutal.Vulkan.dll` was overwritten with the source build (InformationalVersion `2026.10.2-src`). It created an `Instance` with portability enumeration, found **"Apple M4 Pro" (API 1.3.323) through MoltenVK**, created a `DeviceEx` with `VK_KHR_portability_subset` and a graphics queue, and allocated, mapped and wrote a host-visible `BufferEx`.[^smoke] The NuGet Abstractions binds to a source-built Vulkan, and the macOS `VulkanEx` loader path works on this machine (Vulkan SDK 1.4.328 installed in `/usr/local/lib`).

# Build facts recovered from the shipped binaries

| Fact | Value | Evidence |
|---|---|---|
| Target framework | `net10.0` only | `TargetFrameworkAttribute(.NETCoreApp,Version=v10.0)`; single nuspec group[^asminfo][^vulkan-nuspec] |
| Compiler / language | Roslyn 5.6.0-2 (SDK 10.0.10x band), **LangVersion 14.0**, `nullable enable`, `unsafe`, Release, defines `TRACE;RELEASE` only | PDB compilation options[^managed-report] |
| Implicit usings | **on** (System, System.Collections.Generic, System.IO, System.Linq, System.Net.Http, System.Threading, System.Threading.Tasks); the repo's `GlobalUsings.cs` files add to them | PDB-embedded `GlobalUsings.g.cs`; e.g. `Device.cs` uses `Dictionary<,>` with no `using`[^managed-report] |
| Assembly attributes added by the build, **not in source** | `AssemblyVersion("1.0.0.0")`, **`[assembly: DisableRuntimeMarshalling]`**, `AssemblyMetadata("RepositoryUrl", …)`, `AssemblyConfiguration("Release")`, file and informational versions | `[asm] DisableRuntimeMarshallingAttribute()` on all 31 inspected DLLs; `grep` finds it nowhere in the tree[^asminfo] |
| `InternalsVisibleTo` | In source: `Brutal.Vulkan` → `Brutal.Vulkan.Abstractions` (`AssemblyInfo.cs:1`) and `Brutal.Strings` → `Brutal.Strings.Tests`. **Build-added** (absent from source): `Brutal.OpenAl` → `Brutal.OpenAl.Generate.Test` and `Brutal.OpenAl.Test`; `Brutal.Sdl` → `Brutal.Sdl.Tests` | [^assemblyinfo][^asminfo] |
| C# 14 features in use | extension blocks (incl. extension operators and properties) in Core, Strings, Numerics, Localization and Texture.Abstractions; null-conditional assignment `binBuffer?.Uri = …` in `Brutal.Gltf/GltfApi/GltfUtility.cs:940` | grep plus audit[^managed-report] |
| Analyzers / generators | Shipped only as binaries: `Brutal.Core.Analyzers`, `Brutal.Numerics.Analyzers`, `Brutal.Strings.Analyzers` (+CodeFixes), `Brutal.Interop.SourceGenerators` (`InteropHandleGenerator`). **No source in the tree.** Not needed for Vulkan, Abstractions or the 10 managed libs. Needed for bindings that use `[InteropHandle]` (Jolt, NotCurses, Fmod, ShaderC, OpenAl, Sdl*, Slang) | [^managed-report] |
| T4 templates | `.gen.cs` outputs are checked in; the `.tt` sources (Vector.tt, EnumArithmetic.tt, …) are **absent**. Vulkan's `Generated/*` comes from an unpublished generator | [^managed-report] |

# csproj template (verified for Brutal.Vulkan)

Place the project outside the read-only BRUTAL checkout (or in a fork) and glob the sources:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>Brutal.Vulkan</AssemblyName>          <!-- must match NuGet simple name -->
    <RootNamespace>Brutal.VulkanApi</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>             <!-- required: sources rely on implicit usings -->
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <AssemblyVersion>1.0.0.0</AssemblyVersion>          <!-- keep: consumers reference 1.0.0.0 -->
    <FileVersion>2026.10.99.0</FileVersion>             <!-- > NuGet's 2026.10.2.0 so the fork wins file-version tie-breaks -->
    <InformationalVersion>2026.10.2-browser</InformationalVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <BrutalSrc>/path/to/BRUTAL/Brutal.Vulkan</BrutalSrc>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$(BrutalSrc)/**/*.cs" />
    <AssemblyAttribute Include="System.Runtime.CompilerServices.DisableRuntimeMarshallingAttribute" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Ahwoo.BRUTAL.Core"     Version="2026.10.1" />
    <PackageReference Include="Ahwoo.BRUTAL.Logging"  Version="2026.10.2" />
    <PackageReference Include="Ahwoo.BRUTAL.Memory"   Version="2026.10.1" />
    <PackageReference Include="Ahwoo.BRUTAL.Numerics" Version="2026.10.1" />
    <PackageReference Include="Ahwoo.BRUTAL.Strings"  Version="2026.10.1" />
  </ItemGroup>
</Project>
```

`LangVersion` is left at the net10.0 default, which is 14. `InternalsVisibleTo("Brutal.Vulkan.Abstractions")` comes from the source file `AssemblyInfo.cs`. For **Brutal.Vulkan.Abstractions**, use the same template with `AssemblyName` `Brutal.Vulkan.Abstractions`, a `ProjectReference` to the Vulkan project, and package references to Collections, Logging, Memory, Numerics and Strings, mirroring the nuspec.[^abstr-csproj] Package versions and dependency edges for every library are in [/brutal/nuget-package-inventory.md](/brutal/nuget-package-inventory.md). The ProjectReference graph that built the full closure is in the `full/` scratch folder.[^full-closure]

# Patch sketch: Brutal.Vulkan with a pluggable loader (browser fork)

The smallest change that keeps the standard Vulkan loader contract and avoids a circular assembly reference is this. The browser implementation references Brutal.Vulkan for the struct types, so Brutal.Vulkan cannot reference the implementation back. The fork therefore adds a **registration hook in a separate static class**, so that registering does not run `Vulkan`'s static constructor, and fetches the four global commands through `vkGetInstanceProcAddr(NULL, name)`, as the Vulkan spec allows:

```csharp
// New file in the fork: Brutal.Vulkan/Manual/VulkanLoaderOverride.cs
namespace Brutal.VulkanApi;
public static class VulkanLoaderOverride
{
    // Set by the browser implementation before anything touches Brutal.VulkanApi.Vulkan.
    public static VkGetInstanceProcAddr? GetInstanceProcAddr;
}

// ImportResolver.cs, static Vulkan(): first lines
if (VulkanLoaderOverride.GetInstanceProcAddr is { } gipa)
{
    GetInstanceProcAddr = gipa;   // no NativeLibrary.Load / SetDllImportResolver / GetExport
    return;
}
// ... existing Windows / macOS / Linux code unchanged ...

// VulkanApiEx.gen.cs: CreateInstanceInternal (and the three Enumerate* wrappers), browser path
VkResult result = Dll != 0
    ? Vulkan.PInvoke.vkCreateInstance(pCreateInfoFixed, pAllocator, &pInstanceLocal)
    : ((delegate* unmanaged[Cdecl]<VkInstanceCreateInfo*, VkAllocationCallbacks*, VkInstance*, VkResult>)
          GetInstanceProcAddr.Invoke(default, "vkCreateInstance"u8.AsPointer()).Invoke)(pCreateInfoFixed, pAllocator, &pInstanceLocal);
```

The implementation's entry points are then either `[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]` methods, whose addresses fit the existing `delegate* unmanaged[Cdecl]` fields, or, if the fork also rewrites `delegate* unmanaged[Cdecl]<` to `delegate*<` in `Generated/Functions.gen.cs`, plain static managed methods. The trade-off is in [/brutal/vulkan-binding-architecture.md](/brutal/vulkan-binding-architecture.md). `Functions.gen.cs` has no wrapper struct for the four global commands, so the patch casts to an inline `delegate* unmanaged[Cdecl]<…>`. A source-built Brutal.Vulkan.Abstractions must accompany the fork.

**Verified on desktop (CoreCLR, not wasm):** this patch, applied to a scratch copy of the sources,[^fork] compiles. A test program[^fakeicd] then registered a fake C# implementation made of three `[UnmanagedCallersOnly]` functions (`vkCreateInstance`, `vkEnumeratePhysicalDevices`, `vkDestroyInstance`) that returns NULL for everything else. `Vulkan.CreateInstance(...)` succeeded: the implementation received the application info and the UTF-8 extension list, saw **106 `vkGetInstanceProcAddr` lookups** (1 global plus 105 table entries), and the `Instance` was created and disposed with 0 physical devices.

# Replacement strategies for a patched Brutal.Vulkan (browser fork)

| Strategy | How | Pros / cons |
|---|---|---|
| **A. Source-build only what you patch, keep the rest from NuGet** | `ProjectReference` to the forked `Brutal.Vulkan` (and `Brutal.Vulkan.Abstractions`, so internal APIs stay in sync); keep `Ahwoo.BRUTAL.Core/…` from NuGet | Smallest fork. But NuGet `Ahwoo.BRUTAL.Vulkan.Abstractions` still pulls NuGet `Ahwoo.BRUTAL.Vulkan` transitively, so two `Brutal.Vulkan.dll` 1.0.0.0 candidates appear. Avoid by **not** referencing the NuGet Abstractions (source-build it too) or by `ExcludeAssets="all"` on a direct `Ahwoo.BRUTAL.Vulkan` reference. Whether SDK conflict resolution picks the higher `FileVersion` (2026.10.99) over the package copy is UNVERIFIED |
| **B. Source-build the whole managed closure** | 12 projects (Core … Vulkan.Abstractions) as ProjectReferences; no `Ahwoo.BRUTAL.*` packages | Verified to compile.[^full-closure] Allows patching `Platform` (Core) and the job system (Concurrency) as well. No duplicate-assembly ambiguity |
| **C. Local NuGet feed with re-versioned packages** | `dotnet pack` the forks as `Ahwoo.BRUTAL.Vulkan` version `2026.10.2.1` (a 4-part version greater than 2026.10.2) on a local feed | Transparent for NuGet consumers; more ceremony |
| D. Post-build file swap | overwrite `Brutal.Vulkan.dll` in the output (what the smoke test did) | Proves binary compatibility; not a build strategy |

For the browser PoC, **B** (or A with Abstractions also source-built) is recommended. Abstractions binds to Brutal.Vulkan **internals**: the `VulkanHandleWrapper.Handle` interface member, `*Internal` wrapper methods and `Device._pInvoke`. Any loader or function-pointer patch that touches internals must therefore be compiled into both assemblies together. See [/brutal/vulkan-binding-architecture.md](/brutal/vulkan-binding-architecture.md) for the patch points: `ImportResolver.cs`, the `VulkanApiEx.gen.cs` global wrappers, and optionally `Functions.gen.cs` (`delegate* unmanaged[Cdecl]` to `delegate*`).

# Assembly naming: public packages vs the KSA game build

| | Public NuGet (Oct 2026) | KSA private build (game 2026.10.10.5554)[^ksa-version] |
|---|---|---|
| Core libraries | `Brutal.Core`, `Brutal.Memory`, `Brutal.Collections`, `Brutal.Strings`, `Brutal.Logging`, `Brutal.Numerics`, `Brutal.Maths` | **`Brutal.Core.Common`, `Brutal.Core.Memory`, `Brutal.Core.Collections`, `Brutal.Core.Strings`, `Brutal.Core.Logging`, `Brutal.Core.Numerics`, `Brutal.Core.Maths`**, plus `Brutal.Core.Package` |
| AssemblyVersion | 1.0.0.0 everywhere | **2026.9.0.0**; `Brutal.Vulkan.dll` references `Brutal.Core.Common 2026.9.0.0` etc.[^ksa-dll][^ksa-csproj] |
| Brutal.Vulkan types | 2,958 (instance table 105, device table 697) | 2,730 (101 / 634) |
| Namespaces | identical (`Brutal`, `Brutal.Pointers`, `Brutal.VulkanApi`, …) | identical |

The public packages are a **re-packaging and renaming** of what used to be KSA's internal "Brutal.Core.*" assemblies: same namespaces, different assembly names. Assembly names and versions differ, so **public-package DLLs are not binary-compatible with KSA's DLLs and vice versa**. Source compatibility is high, since namespaces and types are shared and the public tree is newer. This is irrelevant to the PoC, which must not link KSA binaries, but it matters for anyone trying to run KSA code against the public packages.

# Open questions

* UNVERIFIED: how .NET SDK conflict resolution behaves when a ProjectReference output and a NuGet runtime asset share simple name and AssemblyVersion but differ in FileVersion (strategy A).
* UNVERIFIED: whether the upstream per-library repositories (`RocketWerkz/brutal.vulkan`, `brutal.interop`, …) with real project files and generators will be published. The README promises project files "coming".

[^brutal-readme]: BRUTAL README
[^asminfo]: Metadata dump of NuGet DLLs
[^asminfo-tool]: asminfo inspector source
[^vulkan-csproj]: Reconstructed Brutal.Vulkan.csproj
[^abstr-csproj]: Reconstructed Brutal.Vulkan.Abstractions.csproj
[^full-closure]: Full managed closure csprojs
[^smoke]: Desktop smoke test
[^vulkan-nuspec]: Ahwoo.BRUTAL.Vulkan nuspec
[^assemblyinfo]: Brutal.Vulkan/AssemblyInfo.cs
[^managed-report]: Managed-library audit report
[^ksa-csproj]: KSA decompiled Brutal.Vulkan.csproj
[^ksa-dll]: KSA Brutal.Vulkan.dll
[^ksa-version]: KSA version.json
[^fork]: Scratch fork of Brutal.Vulkan with loader override
[^fakeicd]: Fake C# ICD test program
