# Brutal.Vulkan.Browser

BRUTAL's `Brutal.Vulkan` bindings compiled from the pinned open-source tree (`$(BrutalSourceRoot)Brutal.Vulkan`, commit `a25cdf8`) so they can run on browser-wasm. The assembly is still named `Brutal.Vulkan` (version 1.0.0.0, namespace `Brutal.VulkanApi`): game code compiles unchanged against it or against the NuGet package.

## Flavors

| `BrutalManagedFnptr` | Function pointers | Defines | Use |
| --- | --- | --- | --- |
| `true` (default) | managed `delegate*<...>` (`transform.py` rewrites `delegate* unmanaged[Cdecl]<` and strips `[UnmanagedCallersOnly]`) | `BRUTAL_MANAGED_FNPTR` | the browser: an ICD written as plain `static` C# methods is called with a managed `calli`, no native transition, no wasm-tools |
| `false` | upstream `delegate* unmanaged[Cdecl]<...>` | none | verification only (`dotnet build -p:BrutalManagedFnptr=false`; outputs go to `bin/unmanaged`, `obj/unmanaged`) |

`Brutal.Vulkan.Abstractions.Browser` must be built with the same flavor; the flavor property flows through the project reference. Consumers of the managed flavor define `BRUTAL_MANAGED_FNPTR` themselves (it is not inherited) and must not reference the NuGet `Ahwoo.BRUTAL.Vulkan`, `Ahwoo.BRUTAL.Vulkan.Abstractions` or `Ahwoo.BRUTAL.Glfw` assets (`ExcludeAssets="all"`).

## How the loader hook works

`Patched/ImportResolver.cs` and `Patched/Generated/PInvoke/VulkanApi/VulkanApiEx.gen.cs` replace the upstream files (see `patches/brutal/`). Before the first touch of `Brutal.VulkanApi.Vulkan`, the host stores a `vkGetInstanceProcAddr` pointer:

```csharp
AppContext.SetData("BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR",
    (nint)(delegate*<VkInstance, byte*, PfnVkVoidFunction>)&MyIcd.GetInstanceProcAddr);   // managed flavor
```

The static constructor reads it (boxed `nint`, `long` or `ulong`) on any platform and resolves the four global commands with `GIPA(NULL, name)`. Without the key the managed flavor throws `PlatformNotSupportedException` (it has no native loader); the unmanaged flavor loads the native Vulkan library as upstream does (browser: throws with guidance). The ICD must return a typed function for every requested name (110 instance-level, 697 device-level); calling a NULL managed pointer aborts the runtime.

## Build

```sh
dotnet build src/Brutal.Vulkan.Browser -c Release                              # managed flavor
dotnet build src/Brutal.Vulkan.Browser -c Release -p:BrutalManagedFnptr=false   # unmanaged flavor
```

Needs `python3` on PATH (`-p:BrutalPython=...` to override) and the BRUTAL checkout (`-p:BrutalSourceRoot=...` or `BRUTAL_SRC`). The transformed copies are written to `obj/<config>/net10.0/brutal-src/` only when the transform inputs change. Mechanics: `patches/brutal/BrutalSource.targets`.

## Regenerating against a new BRUTAL commit

1. Update the BRUTAL checkout (`BrutalSourceRoot`). Build; fix `Patched/` copies by hand if upstream changed `ImportResolver.cs` or `VulkanApiEx.gen.cs`.
2. `patches/brutal/make-patches.sh` regenerates the patches from `Patched/` and runs `git apply --check` against the checkout.
3. `patches/brutal/tools/api-diff.sh <built Brutal.Vulkan.dll> <NuGet Brutal.Vulkan.dll>` must report no differences other than the calling convention of function-pointer types and the patch's private/internal additions.
4. `dotnet test tests/Brutal.Vulkan.Browser.Tests`; update the pinned counts (R1 count, 110/697 proc names) if the generator output changed.
