---
type: Experiment Report
title: "exp-04: Brutal.Vulkan rebuilt from source with a pluggable loader (desktop MoltenVK, managed ICD, browser-wasm)"
description: Rebuilds BRUTAL's Brutal.Vulkan from the public source, adds a two-file pluggable-loader patch with no public API change, and shows it works on macOS/MoltenVK, with a C# vkGetInstanceProcAddr on desktop, and in headless Chrome on .NET 10 browser-wasm next to the unmodified NuGet Brutal.Vulkan.Abstractions.
tags: [experiment, brutal, vulkan, loader, icd, vkGetInstanceProcAddr, browser-wasm, moltenvk, source-build, patch, upstream]
sources:
  - id: project
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/README.md
    title: exp-04 project README (layout, build/run commands, out/ index)
  - id: csproj
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/src/Brutal.Vulkan/Brutal.Vulkan.csproj
    title: Rebuilt Brutal.Vulkan.csproj
  - id: patch
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/patches/brutal-vulkan-pluggable-loader.patch
    title: Combined unified diff against BRUTAL a25cdf8 (git apply --check OK)
  - id: patched-resolver
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/src/Brutal.Vulkan/Patched/ImportResolver.cs
    title: Patched ImportResolver.cs (AppContext hook + Vulkan.GlobalCommands)
  - id: fake-icd
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/shared/FakeIcd.cs
    title: Managed fake ICD (UCO wrappers and dictionary-based proc-addr)
  - id: browser-icd
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/apps/BrowserCompileCheck/wwwroot/main.js
    title: Browser host script creating Module.addFunction trampolines into [JSExport] wrappers
  - id: out-desktop
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/out/desktop-instance-smoke.txt
    title: DesktopInstanceSmoke console output (MoltenVK, Apple M4 Pro)
  - id: out-envmatrix
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/out/desktop-instance-smoke-envmatrix.txt
    title: Desktop run with and without setup-env.sh, DYLD_PRINT_LIBRARIES
  - id: out-baseline
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/out/desktop-instance-smoke-nuget-baseline.txt
    title: Same program against the published NuGet Brutal.Vulkan 2026.10.2
  - id: out-managed
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/out/managed-loader-smoke.txt
    title: ManagedLoaderSmoke console output (desktop, managed ICD)
  - id: out-callnull
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/out/managed-loader-call-null.txt
    title: ManagedLoaderSmoke --call-null (exit 139)
  - id: out-inst-names
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/out/instance-proc-names.txt
    title: Ordered vkGetInstanceProcAddr requests (110)
  - id: out-dev-names
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/out/device-proc-names.txt
    title: Ordered vkGetDeviceProcAddr requests (697)
  - id: out-browser-js
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/out/browser-run-js.txt
    title: Headless Chrome for Testing 151 run, JS-trampoline ICD
  - id: out-browser-uco
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/out/browser-run-uco.txt
    title: Headless Chrome run, [UnmanagedCallersOnly] pointers (expected failure)
  - id: out-browser-callnull
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/out/browser-run-js-callnull_1.txt
    title: Headless Chrome run calling a NULL table entry
  - id: out-browser-build
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/out/browser-publish-trimwarn.log
    title: browser-wasm publish log (ILLink trim-mode link, SuppressTrimAnalysisWarnings=false)
  - id: out-api-diff
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/out/api-diff.txt
    title: Signature-level API diffs (NuGet vs pristine source vs patched)
  - id: out-identity
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/out/assembly-identity.txt
    title: Assembly identity, attributes and P/Invoke tables
  - id: out-vulkanex
    resource: ../../../projects/exp-04-brutal-vulkan-source-build/out/libvulkanex-inspection.txt
    title: otool/nm/objdump of libVulkanEx (osx-arm64, linux-x64, win-x64, KSA Linux)
  - id: brutal-import-resolver
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/ImportResolver.cs
    title: BRUTAL Brutal.Vulkan/ImportResolver.cs (a25cdf8)
  - id: brutal-pinvoke-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/PInvoke/VulkanApi/VulkanApi.gen.cs
    title: The four [LibraryImport("vulkan")] global commands
  - id: brutal-pinvokeex-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/PInvoke/VulkanApi/VulkanApiEx.gen.cs
    title: CreateInstanceInternal / Enumerate* wrappers that call Vulkan.PInvoke.vk*
  - id: brutal-instance
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Manual/Classes/Instance.cs
    title: Instance constructor (CreateInstanceInternal, FunctionTable, eager EnumeratePhysicalDevices)
  - id: brutal-device
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Manual/Classes/Device.cs
    title: Device constructor (vkGetDeviceProcAddr through the instance table)
  - id: brutal-vkdevice-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Handles/VkDevice.gen.cs
    title: VkDevice.FunctionTable (697 getProc calls, incl. vxDestroy*)
  - id: brutal-handles
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Handles/VkBuffer.gen.cs
    title: Generated handle structs (VkHandle is nint for all 56 handle types)
  - id: brutal-contributing
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/CONTRIBUTING.md
    title: BRUTAL CONTRIBUTING.md (issue first, no public API change without agreement, DCO sign-off)
  - id: nuget-vulkan
    resource: https://www.nuget.org/packages/Ahwoo.BRUTAL.Vulkan/2026.10.2
    title: Ahwoo.BRUTAL.Vulkan 2026.10.2 (lib/net10.0/Brutal.Vulkan.dll + runtimes/*/native/*VulkanEx*)
  - id: nuget-abstractions
    resource: https://www.nuget.org/packages/Ahwoo.BRUTAL.Vulkan.Abstractions/2026.10.2
    title: Ahwoo.BRUTAL.Vulkan.Abstractions 2026.10.2
  - id: exp01-log
    resource: ../../../projects/exp-01-dotnet-wasm-smoke/test/console.release-verbose.log
    title: exp-01 console log (T07/T08 &UcoMethod == 0 and "RuntimeError null function"; T08e JS trampoline PASS)
  - id: exp01-sigprobes
    resource: ../../../projects/exp-01-dotnet-wasm-smoke/test/console.sigprobes.log
    title: exp-01 interp→native signature probes (P01–P45)
  - id: vk-gipa
    resource: https://registry.khronos.org/vulkan/specs/latest/man/html/vkGetInstanceProcAddr.html
    title: Vulkan spec, vkGetInstanceProcAddr (NULL instance valid for the global commands)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:12:14Z }
status: draft
---

# Purpose

The browser plan needs a managed "Vulkan-on-WebGPU" ICD. It consists of C# functions whose pointers a C#
`vkGetInstanceProcAddr` returns. Upstream `Brutal.Vulkan` cannot use one. Its static constructor always
`NativeLibrary.Load`s a platform library and throws on any other OS. It also binds the four global commands as
`[LibraryImport("vulkan")]` P/Invokes.[^brutal-import-resolver][^brutal-pinvoke-gen] This experiment asks four
questions:

1. Can `Brutal.Vulkan` be rebuilt from the public source (which has no `.csproj`) into an assembly that can replace
   the NuGet one?
2. What is the smallest patch that accepts a supplied `vkGetInstanceProcAddr` and routes the global commands through
   it?
3. Does desktop (macOS/MoltenVK) behaviour stay unchanged?
4. Does the patched build compile, and ideally run, on browser-wasm under a managed ICD while the **NuGet**
   `Brutal.Vulkan.Abstractions` binds to it?

Related analyses: [/brutal/vulkan-loader-and-proc-addr-contract.md](/brutal/vulkan-loader-and-proc-addr-contract.md),
[/webgpu/brutal-abstractions-icd-surface.md](/webgpu/brutal-abstractions-icd-surface.md),
[/dotnet-wasm/function-pointers-and-native-callbacks.md](/dotnet-wasm/function-pointers-and-native-callbacks.md),
[/experiments/exp-03-webgpu-jsimport.md](/experiments/exp-03-webgpu-jsimport.md).

# Setup

* macOS 27.0.1 arm64 (Apple M4 Pro), .NET SDK 10.0.100, no `wasm-tools` workload. Vulkan SDK 1.4.328.1, installed
  system-wide (`/usr/local/lib/libvulkan.1.dylib`, `/usr/local/share/vulkan/icd.d/MoltenVK_icd.json`).
  Headless Chrome for Testing 151 is driven by puppeteer-core from exp-01's `node_modules`.
* BRUTAL source at commit `a25cdf8` (read-only). NuGet: Core/Memory/Numerics/Strings 2026.10.1, Logging 2026.10.2,
  Vulkan and Vulkan.Abstractions 2026.10.2.[^nuget-vulkan][^nuget-abstractions]
* Project: `projects/exp-04-brutal-vulkan-source-build/`. Build and run commands are in its README.[^project]

## Reconstructed `Brutal.Vulkan.csproj`

These are the settings needed to compile the tree unchanged:[^csproj]

| Setting | Value | Why |
|---|---|---|
| `TargetFramework`, `AssemblyName`, `RootNamespace` | `net10.0`, `Brutal.Vulkan`, `Brutal.VulkanApi` | as published |
| `ImplicitUsings` | **enable** | With `disable` the build fails with 663 errors (`ReadOnlySpan<>`, `HashSet<>`, `Flags`, `IntPtr` …). `GlobalUsings.cs` does not import `System`/`System.Collections.Generic`. |
| `AllowUnsafeBlocks`, `Nullable`, `LangVersion` | true, enable, latest | The tree uses C# 12 struct primary constructors (`ref struct CreateInfo()`), `u8` literals and inline arrays. No `field` keyword or extension blocks. |
| `<AssemblyAttribute Include="System.Runtime.CompilerServices.DisableRuntimeMarshallingAttribute" />` | — | The published dll carries `[assembly: DisableRuntimeMarshalling]`, but no source file declares it, so the upstream project must add it. |
| `AssemblyVersion` | `1.0.0.0` | Matches the published identity `Brutal.Vulkan, Version=1.0.0.0, PublicKeyToken=null`. FileVersion 2026.10.2.0. |
| Compile items | `$(BRUTAL)/Brutal.Vulkan/**/*.cs`, minus `ImportResolver.cs` and `Generated/PInvoke/VulkanApi/VulkanApiEx.gen.cs`, plus `Patched/**/*.cs` | patch carried as two file copies |
| PackageReferences | Core, Logging, Memory, Numerics, Strings | Strings pulls `Microsoft.Extensions.ObjectPool 11.0.0-preview.5`. Restore accepted it with no extra flag because it is an exact transitive version. |

# Patch design

**Two files: +121/−0 lines in the hand-written `ImportResolver.cs`, and +4/−4 in the generated `VulkanApiEx.gen.cs`.**
The diff is in `patches/brutal-vulkan-pluggable-loader.patch`. It applies cleanly to BRUTAL `a25cdf8`
(`git apply --check`).[^patch][^patched-resolver]

1. **Loader selection (`static Vulkan()`).** The constructor first reads
   `AppContext.GetData("BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR")`, which may be a boxed `nint`, `long` or `ulong`. If
   that is non-zero, it becomes `Vulkan.GetInstanceProcAddr`. The constructor then resolves `vkCreateInstance`,
   `vkEnumerateInstanceExtensionProperties`, `vkEnumerateInstanceLayerProperties` and `vkEnumerateInstanceVersion`
   with `GetInstanceProcAddr(NULL, name)`, which the spec allows for the global commands.[^vk-gipa] It returns without
   loading a library or registering a `DllImportResolver`. The hook works on **every** platform (software ICDs, layering,
   tests).
   * If the key is absent on browser, the constructor throws
     `PlatformNotSupportedException("There is no native Vulkan library on this platform. Set AppContext data '…'")`
     instead of upstream's `NullReferenceException`.
   * If the key is absent elsewhere, the original code runs unchanged: same `NativeLibrary.Load` names, search paths,
     resolver and `GetExport`.
2. **Global-command routing.** A new `internal static class Vulkan.GlobalCommands` has the four methods. Each one calls
   `PInvoke.vk*` (the LibraryImport) when `!OperatingSystem.IsBrowser() && !UseProcAddrForGlobalCommands`. Otherwise it
   calls the resolved function pointer, and throws `EntryPointNotFoundException` if that pointer is NULL. The only edit
   to generated code changes four call sites in `VulkanApiEx.gen.cs` from `Vulkan.PInvoke.vkX(` to
   `Vulkan.GlobalCommands.vkX(`.
3. **Trimmer-friendly.** The trimmer constant-folds `OperatingSystem.IsBrowser()`, so the published browser
   `Brutal.Vulkan.dll` has **0** P/Invokes (the desktop dll has 4) and no `NativeLibrary` path.[^out-identity]
4. **No public API change.** The signature-level diff, pristine vs patched, adds only private fields and the internal
   nested type `Vulkan+GlobalCommands`.[^out-api-diff]

**Why patch these call sites rather than elsewhere.** The `[LibraryImport]` methods are `partial`, and the source
generator implements them, so they cannot be overridden from a hand-written partial. A `DllImportResolver` cannot help
either, because it can only return a native library handle, never managed function pointers. The four wrappers in
`VulkanApiEx.gen.cs` are the only callers of `Vulkan.PInvoke.vk*` in Brutal.Vulkan and Abstractions.[^brutal-pinvokeex-gen]
Editing them is a one-token change per call site, and upstream it becomes a one-line generator template change. All
logic lives in hand-written code. Direct user calls to the public `Vulkan.PInvoke.vk*` still bypass the hook; this is
documented.

**Why an AppContext key rather than `Vulkan.ConfigureLoader(nint)`.** `Vulkan` has an explicit static constructor, so
calling any static method on it runs the native load *first*. A setter on `Vulkan` would therefore force the
constructor to become lazy, which is a larger and behaviour-changing refactor. The AppContext key needs no new public
API, which BRUTAL's CONTRIBUTING declines without an agreed issue.[^brutal-contributing] It has drawbacks: it is
stringly typed, it is ignored silently if set after first use, and it cannot come from `runtimeconfig.json` because
the value is a runtime pointer. A pointer value of 0 counts as "unset". This matters on the browser (see Results 5).

# Results

## 1. Inspection

* **libVulkanEx is not a Vulkan loader.** It is a 34 KB helper. It exports only `vxDestroyImages`,
  `vxDestroyImageViews`, `vxDestroyBuffers` and `vxDestroyBufferViews`; `nm -gU | grep -c ' _vk'` returns 0. It
  imports `vkDestroyImage(View)` and `vkDestroyBuffer(View)` and links `@rpath/libvulkan.1.dylib` with
  `LC_RPATH /usr/local/lib` and `@loader_path`.[^out-vulkanex]
  * On macOS, `NativeLibrary.GetExport(VulkanEx, "vkGetInstanceProcAddr")` and the `"vulkan"` P/Invokes resolve
    through dlsym's dependency search into the Khronos loader.
  * linux-x64 `libVulkanEx.so` (NEEDED `libvulkan.so.1`, RUNPATH `$ORIGIN`) and win-x64 `VulkanEx.dll` (imports
    `vulkan-1.dll`) export the same four symbols.
  * On Linux and Windows, Brutal.Vulkan loads `vulkan`/`vulkan-1` directly and never opens VulkanEx.
  * KSA Linux ships an older `libVulkanEx.so` (different hash, no RUNPATH) with the same exports, and no libvulkan of
    its own.
* **vx\* is dead on real drivers.** `VkDevice.FunctionTable` asks **`vkGetDeviceProcAddr`** for the four `vx*`
   names.[^brutal-vkdevice-gen] On MoltenVK the table entries come back **NULL** (`vxDestroyBuffers=NULL,
   vxDestroyImages=NULL`).[^out-desktop] This settles the UNVERIFIED item in the loader-contract note.
   `device.DestroyImages/Buffers(…)` would therefore crash on desktop. A managed ICD can implement them trivially.
* **Identity.** None of the BRUTAL assemblies is strong-named (`PublicKeyToken=null`).[^out-identity]
  * NuGet `Brutal.Vulkan.dll`: `AssemblyVersion 1.0.0.0`, `FileVersion 2026.10.2.0`, `InformationalVersion
    2026.10.2+5dddc87e…`. It carries `DisableRuntimeMarshalling` and `InternalsVisibleTo(Brutal.Vulkan.Abstractions)`.
  * Abstractions references `Brutal.Vulkan, Version=1.0.0.0`.
  * KSA Linux's `Brutal.Vulkan.dll` is an older 2026.9 build (`Version=2026.9.0.0`, references the `Brutal.Core.*`
    split assemblies).
* **The source matches the package.** The pristine source build and the NuGet dll have the same 24,614-line
  signature listing (all types, methods and fields, including private ones), with **zero differences**.[^out-api-diff]

## 2. Build

* `dotnet build src/Brutal.Vulkan -c Release`: **0 warnings, 0 errors**, for both the pristine and the patched source
  (about 3 s with a warm compiler server). The Brutal.Core, Brutal.Numerics and Brutal.Strings analyzers from the
  packages are passed to csc and report nothing.
* `-p:IsAotCompatible=true` (trim, AOT and single-file analyzers): 0 warnings.

## 3. Desktop MoltenVK (native path, patched build + NuGet Abstractions)

The output below is abridged from the console log:[^out-desktop]

```
Brutal.Vulkan: … informational=2026.10.2-exp04-source+BRUTAL.a25cdf8   (source-built dll loaded)
Brutal.Vulkan.Abstractions: … informational=2026.10.2+5dddc87e…         (NuGet dll, binds unchanged)
vkEnumerateInstanceVersion -> 1.4.328
Instance extensions (18): … VK_KHR_portability_enumeration, VK_LUNARG_direct_driver_loading
Abstractions VulkanHelpers.Validation.IsSupported -> True
Instance created … physical devices=1
  PhysicalDevice: 'Apple M4 Pro' api=1.3.323 driver=0.2.2208 vendor=0x106b type=IntegratedGpu
Graphics queue family=0; device extensions=128; VK_KHR_portability_subset=True
DeviceEx created … Queue … Device table: vkCreateBuffer=set, vxDestroyBuffers=NULL, vxDestroyImages=NULL
AllocateBuffer: … memoryType=1 allocSize=1 KB
Disposed device + instance.   RESULT: OK
```

* **Environment.** No variables are needed when the SDK is installed system-wide. dyld loads
  `runtimes/osx-arm64/native/libVulkanEx.dylib`, then `/usr/local/lib/libvulkan.1.4.328.dylib` and
  `/usr/local/lib/libMoltenVK.dylib`.[^out-envmatrix] After `source setup-env.sh` (which sets `DYLD_LIBRARY_PATH` and
  `VK_ICD_FILENAMES`), the SDK copies under `~/VulkanSDK/1.4.328.1/macOS/lib` are loaded instead. Without a
  system-wide install, libVulkanEx needs `libvulkan.1.dylib` reachable through `DYLD_LIBRARY_PATH` or next to itself
  (`@loader_path`), plus an ICD manifest.
* **Unchanged behaviour.** The same program built against the published NuGet `Brutal.Vulkan` prints identical output
  once handles, versions and paths are normalised.[^out-baseline]
* **Binding trick.** The Abstractions package pulls in `Ahwoo.BRUTAL.Vulkan` transitively. Its `lib/` dll then
  **silently overwrites** the project-reference `Brutal.Vulkan.dll` in `bin/`, with no warning, and the app runs the
  unpatched NuGet code; this was reproduced in a variant without the fix. The fix is a direct
  `<PackageReference Include="Ahwoo.BRUTAL.Vulkan" Version="2026.10.2" ExcludeAssets="compile;runtime" />`. It keeps
  the native `runtimes/*/native` assets in `deps.json` and drops the managed dll.

## 4. Managed ICD on desktop (no Vulkan library loaded)

`ManagedLoaderSmoke` passes the source-built Brutal.Vulkan an `[UnmanagedCallersOnly]` `vkGetInstanceProcAddr`
through the AppContext key. That function returns C# implementations of 20 commands and NULL for everything else.[^fake-icd]
The whole scenario passes: globals, `VulkanHelpers.Validation.IsSupported`, Instance, 1 physical device ("BRUTAL
exp-04 managed fake ICD"), NuGet `DeviceEx`, queue, `AllocateBuffer`/`FreeAllocation`, dispose. `RESULT: OK`. With
`DYLD_PRINT_LIBRARIES`, no libvulkan, VulkanEx or MoltenVK image is loaded.[^out-managed]

**Ordered `vkGetInstanceProcAddr` requests: 110** (full list in `out/instance-proc-names.txt`; * = the fake ICD
implemented it).[^out-inst-names]

1. Four `instance=NULL` lookups from the patched static constructor: `vkCreateInstance*`,
   `vkEnumerateInstanceExtensionProperties*`, `vkEnumerateInstanceLayerProperties*`, `vkEnumerateInstanceVersion*`.
2. 105 names from `VkInstance.FunctionTable`, all with the instance handle, in this order:
   `vkDestroyInstance*, vkEnumeratePhysicalDevices*, vkGetPhysicalDeviceFeatures, …FormatProperties,
   …ImageFormatProperties, vkGetPhysicalDeviceProperties*, …QueueFamilyProperties*, …MemoryProperties*,
   vkCreateDevice*, vkEnumerateDeviceExtensionProperties*, vkEnumerateDeviceLayerProperties, …`, through the surface,
   display, debug and vendor entries, ending `…, vkCreateMetalSurfaceEXT, vkCreateAndroidSurfaceKHR`.
   `vkGetInstanceProcAddr` is never requested: the table stores the supplied pointer directly.
3. `vkGetDeviceProcAddr*`, requested once per `Device` (from `Device.cs`).[^brutal-device]

**Ordered `vkGetDeviceProcAddr` requests: 697** (full list in `out/device-proc-names.txt`).[^out-dev-names] They
start `vkDestroyDevice*, vkGetDeviceQueue*, vkQueueSubmit, vkQueueWaitIdle, vkDeviceWaitIdle, vkAllocateMemory*,
vkFreeMemory*, vkMapMemory, …, vkBindBufferMemory*, …, vkGetBufferMemoryRequirements*, …, vkCreateBuffer*,
vkDestroyBuffer*, …` and end `…, vxDestroyImages, vxDestroyImageViews, vxDestroyBuffers, vxDestroyBufferViews`. This
matches the static extraction in the loader-contract note.

**Calls made by the scenario, in order:**

1. `vkEnumerateInstanceVersion`
2. `vkEnumerateInstanceExtensionProperties` (count only)
3. `vkEnumerateInstanceLayerProperties` ×2 (Abstractions `Validation`; both with NULL props, because the write pass
   passes an empty span)
4. `vkCreateInstance` (flags `EnumeratePortabilityBitKHR`, apiVersion 1.3.0, two extensions)
5. `vkEnumeratePhysicalDevices` ×2, eagerly in the `Instance` constructor
6. `vkGetPhysicalDeviceProperties` ×2
7. `vkGetPhysicalDeviceQueueFamilyProperties` ×2
8. `vkCreateDevice`
9. `vkGetDeviceQueue`
10. `vkCreateBuffer`
11. `vkGetBufferMemoryRequirements`
12. `vkGetPhysicalDeviceMemoryProperties`
13. `vkAllocateMemory`
14. `vkBindBufferMemory`
15. `vkDestroyBuffer`
16. `vkFreeMemory`
17. `vkDestroyDevice`
18. `vkDestroyInstance`

**NULL tolerance.**

* *Construction* tolerates NULLs fully. `VkInstance.FunctionTable` and `VkDevice.FunctionTable` store whatever comes
  back, with no checks: 98 of 110 and 689 of 697 entries were NULL and nothing failed.
* *The `Instance` constructor is not lazy.* It immediately calls `vkEnumeratePhysicalDevices` twice, so that entry
  (and `vkCreateInstance` and `vkDestroyInstance`) must be real.[^brutal-instance]
* `PhysicalDevice` construction makes no calls.
* *Calling* a NULL entry is fatal and cannot be caught. `physicalDevice.GetFeatures()` (NULL
  `vkGetPhysicalDeviceFeatures`) kills the process with SIGSEGV, exit 139, and no managed
  exception.[^out-callnull]

## 5. Browser-wasm

* **Build.** `dotnet build apps/BrowserCompileCheck -c Release` passes with **0 warnings, 0 errors**. The project uses
  `Microsoft.NET.Sdk.WebAssembly`, `RuntimeIdentifier=browser-wasm`, `OutputType=Exe`, and the .NET 10 `wasmbrowser`
  template layout (`wwwroot/main.js`; the template no longer uses `WasmMainJSPath`).
* **Publish.** `dotnet publish` with ILLink `--trim-mode link` and `-p:SuppressTrimAnalysisWarnings=false
  -p:TrimmerSingleWarn=false` also gives **0 warnings**.[^out-browser-build]
  * No pinvoke-table or relink step runs: the prebuilt `dotnet.native.wasm` is used. The only related output is an
    informational message, not a warning: "Publishing without optimizations. Although it's optional for Blazor, we
    strongly recommend using `wasm-tools` workload!"
  * There are no messages about the `LibraryImport("vulkan")` methods. Before the `IsBrowser()` guard they survived
    trimming (4 ImplMap rows). With the guard they are gone.
  * The trimmed `Brutal.Vulkan` is 573 KB (140 KB brotli), down from 1.56 MB.
* **Run, `?mode=js`: PASS.** A C# `[UnmanagedCallersOnly]` pointer cannot be used here (next bullet). Instead,
  `main.js` wraps 21 `[JSExport]` C# entry points in `Module.addFunction` trampolines, using signatures `iii`, `ii`,
  `iiii`, `vii`, `viii`, `iiiii`, `viiii` and `iiiij`. It registers their table indices, and C# installs the
  `vkGetInstanceProcAddr` trampoline (index `0x2973`) as the AppContext value.[^browser-icd]
  * The same scenario prints `RESULT: OK` in headless Chrome 151, including the i64 `vkBindBufferMemory` offset.
  * The instance-level (110) and device-level (697) name sequences are **byte-identical** to the desktop
    lists.[^out-browser-js]
  * Device creation (`vkCreateDevice` plus 697 `vkGetDeviceProcAddr` calls, each C#→JS→C#) took about 19 ms between
    two console timestamps. That is roughly 27 µs per lookup in the interpreter, a coarse figure that includes the
    ICD's own logging.
* **Run, `?mode=uco`: expected failure.** In a no-relink runtime, `&UcoMethod` evaluates to `0x0` for all 21 entry
  points, as exp-01 also found.[^exp01-log] The patched constructor treats 0 as "not configured" and raises
  `TypeInitializationException` → `PlatformNotSupportedException: There is no native Vulkan library on this platform.
  Set AppContext data 'BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR' …`.[^out-browser-uco]
* **Run, `?mode=js&callnull=1`.** Calling a NULL table entry gives `RuntimeError: null function`. This is a wasm trap,
  the runtime is dead, and there is no managed exception.[^out-browser-callnull]

# Findings

1. **Rebuilding from source is practical.** About 1,955 files compile with a 66-line project (comments included) and 0 warnings. The
   result is metadata-identical to NuGet 2026.10.2. The NuGet Abstractions binds to it unchanged, on desktop and in
   the browser, because neither assembly is strong-named and the version stays 1.0.0.0.
2. **The pluggable-loader patch is small and additive:** two files, +125/−4. It adds no public API. Desktop behaviour is
   unchanged (identical MoltenVK output). Browser builds trim away all native-loading code.
3. **A managed ICD works with Brutal.Vulkan as it stands.** The tables tolerate NULL, every call goes through
   `delegate* unmanaged[Cdecl]`, and `vkGetDeviceProcAddr` comes from the instance proc-addr.
   * For instance + device + queue + one buffer, the fake ICD implemented 20 commands plus `vkGetInstanceProcAddr`.
     18 of them were actually invoked, plus the two proc-addr functions. `vkCreateInstance`,
     `vkEnumeratePhysicalDevices`, `vkDestroyInstance`, `vkCreateDevice`, `vkGetDeviceProcAddr`, `vkGetDeviceQueue`
     and `vkDestroyDevice` are needed by any app. The rest depend on what the app does: Abstractions'
     `Validation`, `FindQueueFamily` and `AllocateBuffer`.
   * Every other NULL is harmless until called. A call to a NULL entry is a process kill (desktop) or a wasm trap
     (browser), never an exception. An ICD should return **typed stubs** for advertised but unimplemented commands
     rather than NULL.
4. **On browser without the `wasm-tools` workload, the ICD's pointers must come from JS** (`Module.addFunction` +
   `[JSExport]`), not from `[UnmanagedCallersOnly]`, which yields 0.
   * This route runs today without any change to Brutal.Vulkan beyond this patch.
   * It is limited to the interp→native `calli` signatures that the prebuilt runtime has trampolines for. exp-01's
     probes show 17 of 45 BRUTAL signature shapes run and 28 abort; all nine used here run.[^exp01-sigprobes]
   * Each call costs a C#→JS→C# round trip.
5. **On wasm32, every Brutal handle is 32 bits** (`nint VkHandle` for all 56 handle types).[^brutal-handles]
   Non-dispatchable handles are `uint64_t` in the C ABI. This does not matter for a C# ICD, but native wasm code that
   shares Vulkan structs with Brutal (for example an Emscripten build of VMA for `Brutal.Vulkan.Vma`) would disagree on
   layout.

# Implications

* **Can it go upstream?** Probably. It is opt-in, additive, adds no public API, keeps desktop behaviour, and is tested
  on macOS. Per CONTRIBUTING it still needs an issue first, a DCO sign-off and a note on which platforms were
  tested.[^brutal-contributing] The generated-file edit should be proposed as a generator-template change.
* **A cleaner phase 2 for upstream:** generate a `Vulkan.GlobalFunctionTable` loaded through
  `GetInstanceProcAddr(NULL, …)` on **all** platforms, as volk does, and delete the four LibraryImports and the
  `DllImportResolver`. Brutal.Vulkan would then need one native export, and the browser branch would only choose where
  `vkGetInstanceProcAddr` comes from.
* **Suggested `Brutal.Vulkan.Browser` loader API** (or an upstream equivalent):
  * a public, cctor-free configuration point such as
    `public static class VulkanLoader { public static unsafe void UseGetInstanceProcAddr(delegate* unmanaged[Cdecl]<VkInstance, byte*, PfnVkVoidFunction> gipa); }`
    that stores into a field read by `static Vulkan()`, keeping the AppContext key as the zero-API fallback;
  * fail fast on a 0 pointer with a message that names the wasm-relink cause;
  * a helper that builds the JS trampoline table from a generated `(name, signature, [JSExport] target)` list while
    no-relink builds are the target. Later, either `[UnmanagedCallersOnly]` plus `wasm-tools` relink, or managed
    `delegate*<…>` tables in a browser-specific source build (see
    [/dotnet-wasm/function-pointers-and-native-callbacks.md](/dotnet-wasm/function-pointers-and-native-callbacks.md)).
* **The ICD must implement the `vx*` batch-destroy commands**, or the generated `Destroy*s` helpers must be fixed
  upstream; they crash on desktop today.

# Open questions

* Does the JS-trampoline route scale to the ~50 lowered signatures a real renderer needs, given that 28 of exp-01's 45
  probe shapes abort? Or does the project accept the `wasm-tools` workload, or a managed `delegate*` source build?
* Is a C#→JS→C# hop per Vulkan command fast enough for command-buffer recording? The 697-lookup device-table build
  cost about 19 ms. Per-draw costs were not measured here; see exp-03 for JSImport costs.
* Should `GlobalCommands` treat a NULL `vkEnumerateInstanceVersion` as Vulkan 1.0, as the spec recommends, instead of
  throwing?
* Not tested here: Windows and Linux desktop runs of the patched build. The code path is byte-identical, but they were
  not executed. Also untested: whether Linux `NativeLibrary.Load("vulkan")` finds `libvulkan.so.1` when the
  unversioned dev symlink is absent.

[^project]: exp-04 project README
[^csproj]: Rebuilt Brutal.Vulkan.csproj
[^patch]: Combined unified diff (brutal-vulkan-pluggable-loader.patch)
[^patched-resolver]: Patched ImportResolver.cs
[^browser-icd]: BrowserCompileCheck wwwroot/main.js
[^fake-icd]: shared/FakeIcd.cs
[^brutal-instance]: BRUTAL Manual/Classes/Instance.cs
[^brutal-device]: BRUTAL Manual/Classes/Device.cs
[^out-desktop]: out/desktop-instance-smoke.txt
[^out-envmatrix]: out/desktop-instance-smoke-envmatrix.txt
[^out-baseline]: out/desktop-instance-smoke-nuget-baseline.txt
[^out-managed]: out/managed-loader-smoke.txt
[^out-callnull]: out/managed-loader-call-null.txt
[^out-inst-names]: out/instance-proc-names.txt
[^out-dev-names]: out/device-proc-names.txt
[^out-browser-js]: out/browser-run-js.txt
[^out-browser-uco]: out/browser-run-uco.txt
[^out-browser-callnull]: out/browser-run-js-callnull_1.txt
[^out-browser-build]: out/browser-publish-trimwarn.log
[^out-api-diff]: out/api-diff.txt
[^out-identity]: out/assembly-identity.txt
[^out-vulkanex]: out/libvulkanex-inspection.txt
[^brutal-import-resolver]: BRUTAL Brutal.Vulkan/ImportResolver.cs
[^brutal-pinvoke-gen]: BRUTAL VulkanApi.gen.cs (four LibraryImports)
[^brutal-pinvokeex-gen]: BRUTAL VulkanApiEx.gen.cs
[^brutal-vkdevice-gen]: BRUTAL VkDevice.gen.cs FunctionTable
[^brutal-handles]: BRUTAL generated handle structs
[^brutal-contributing]: BRUTAL CONTRIBUTING.md
[^nuget-vulkan]: Ahwoo.BRUTAL.Vulkan 2026.10.2
[^nuget-abstractions]: Ahwoo.BRUTAL.Vulkan.Abstractions 2026.10.2
[^exp01-log]: exp-01 console.release-verbose.log
[^exp01-sigprobes]: exp-01 console.sigprobes.log
[^vk-gipa]: Vulkan spec, vkGetInstanceProcAddr
