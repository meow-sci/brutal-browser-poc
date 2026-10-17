# Brutal.Vulkan.Browser.Tests

xunit tests (net10.0 desktop) for the browser flavor of the BRUTAL Vulkan bindings. No GPU, Vulkan SDK or browser needed; `python3` and `git` are used by two test classes (they log what they skipped when missing).

```sh
dotnet test tests/Brutal.Vulkan.Browser.Tests
```

| Class | What |
| --- | --- |
| `FlavorTests` | Reflection: every `Invoke` field is a function pointer and not an unmanaged one, `[UnmanagedCallersOnly]` is gone from the Abstractions callbacks, assembly identity, `DisableRuntimeMarshalling`, `InternalsVisibleTo`. |
| `ManagedIcdTests` | `Fakes/FlavorScenario` runs once per process against `Fakes/ManagedIcd` (plain static C# entry points): instance, physical device, device, queue, `DeviceEx.AllocateBuffer`, map/write/unmap, dispose; 110 instance-level and 697 device-level names requested; probes for 8, 10 and 15 arguments, floats, a 64-bit enum by value (0x100000002) and a 64-bit return. |
| `LoaderTests` | The loader hook. The static constructor runs once per loaded copy, so each test loads a fresh copy of `Brutal.Vulkan.dll` into a collectible `AssemblyLoadContext` and runs it with the AppContext key absent or set. Managed flavor: the referenced assembly's file. Unmanaged flavor: `unmanaged-flavor/Brutal.Vulkan.dll`, built by this project with `BrutalManagedFnptr=false`. The default-context copy is covered by `ManagedIcdTests` (the ICD sets the key before first use). Tests run serially (the key is process-wide). |
| `PatchTests` | The patches pass `git apply --check` against the pinned BRUTAL tree and reproduce `Patched/` exactly. |
| `TransformScriptTests` | `transform.py` rules, excludes, incremental writes, pruning, on synthetic and on the pinned sources. |

Notes: this assembly defines `BRUTAL_MANAGED_FNPTR` (the fake ICD takes `&Method` as `delegate*<...>`). `Vulkan` alone binds to the `Brutal.Vulkan` namespace inside `Brutal.Vulkan.Browser.Tests`, so the tests alias `Brutal.VulkanApi.Vulkan`.
