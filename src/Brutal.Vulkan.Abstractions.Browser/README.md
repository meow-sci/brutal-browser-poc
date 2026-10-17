# Brutal.Vulkan.Abstractions.Browser

`Brutal.Vulkan.Abstractions` (`DeviceEx`, `BufferEx`, `VulkanHelpers`, ...) compiled from the pinned BRUTAL tree (`$(BrutalSourceRoot)Brutal.Vulkan.Abstractions`) against `Brutal.Vulkan.Browser`. The assembly is named `Brutal.Vulkan.Abstractions`, version 1.0.0.0.

Why it is rebuilt: the NuGet Abstractions assembly was compiled against unmanaged function-pointer field types (`calli unmanaged cdecl`). Against the managed flavor of `Brutal.Vulkan` those no longer bind (`MissingFieldException`), so the Abstractions must be built from source in the same flavor.

The transform (`patches/brutal/transform.py`) rewrites the one `delegate* unmanaged[Cdecl]<` (the `VkDebugUtilsMessengerCallbackEXT` alias in `VulkanHelpers.cs`) and strips the three `[UnmanagedCallersOnly]` attributes of `VulkanHelpers.DebugUtilsMessengerCallbacks.Quiet/Default/Verbose`, so `&VulkanHelpers.DebugUtilsMessengerCallbacks.Quiet` is a valid managed function pointer. No source patch is needed.

```sh
dotnet build src/Brutal.Vulkan.Abstractions.Browser -c Release                              # managed flavor
dotnet build src/Brutal.Vulkan.Abstractions.Browser -c Release -p:BrutalManagedFnptr=false   # unmanaged flavor (also rebuilds Brutal.Vulkan.Browser)
```

See `src/Brutal.Vulkan.Browser/README.md` for the flavors, the loader hook and how to regenerate against a new BRUTAL commit.
