---
type: Code Analysis
title: Brutal.Vulkan binding architecture (loader, function tables, handles, structs)
description: How the open-source Brutal.Vulkan 2026.10.2 binding is built (static loader, four global P/Invokes, per-instance and per-device function-pointer tables, handle/struct/flag conventions) and what each design choice means for running it on browser-wasm with a C# Vulkan-on-WebGPU implementation.
tags: [brutal, vulkan, interop, function-pointers, wasm32, abi, loader]
sources:
  - id: import-resolver
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/ImportResolver.cs
    title: Brutal.Vulkan/ImportResolver.cs (static Vulkan() loader)
  - id: pinvoke-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/PInvoke/VulkanApi/VulkanApi.gen.cs
    title: The four [LibraryImport("vulkan")] global functions
  - id: pinvoke-ex
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/PInvoke/VulkanApi/VulkanApiEx.gen.cs
    title: Managed wrappers over the global functions (CreateInstanceInternal, Enumerate*)
  - id: functions-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Functions.gen.cs
    title: Function-pointer wrapper structs (814 types, delegate* unmanaged[Cdecl])
  - id: vkinstance-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Handles/VkInstance.gen.cs
    title: VkInstance handle + VkInstance.FunctionTable (105 loaded entries)
  - id: vkdevice-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Handles/VkDevice.gen.cs
    title: VkDevice handle + VkDevice.FunctionTable (697 loaded entries)
  - id: vkbuffer-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Handles/VkBuffer.gen.cs
    title: Example non-dispatchable handle (readonly struct with nint VkHandle)
  - id: devext-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Extensions/VkDeviceExtensions.gen.cs
    title: Generated device/queue/command-buffer extension methods (26,687 lines)
  - id: instext-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Extensions/VkInstanceExtensions.gen.cs
    title: Generated instance/physical-device extension methods (5,883 lines)
  - id: globals-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Globals.gen.cs
    title: 556 global using aliases (VkXxxFlags = VkFlags, KHR→core aliases, VkBool32 = Brutal.Bool32)
  - id: inline-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/InlineArrays.gen.cs
    title: 24 [InlineArray] types (e.g. VkMemoryType_32)
  - id: buffer-ci
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Structs/VkBufferCreateInfo.gen.cs
    title: Example struct (explicit layout, Size=56, sType initialiser)
  - id: instance-cs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Manual/Classes/Instance.cs
    title: Manual Instance class
  - id: physdev-cs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Manual/Classes/PhysicalDevice.cs
    title: Manual PhysicalDevice class
  - id: device-cs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Manual/Classes/Device.cs
    title: Manual Device class
  - id: queue-cs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Manual/Classes/Queue.cs
    title: Manual Queue class (Lock-guarded submit/present)
  - id: cmdbuf-cs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Manual/Classes/CommandBuffer.cs
    title: Manual CommandBuffer struct
  - id: wrapper-cs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Manual/Classes/VulkanHandleWrapper.cs
    title: VulkanHandleWrapper<THandle,TTable> interface (internal Handle member)
  - id: vkconst-cs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Manual/Constants/VK.cs
    title: VK.WHOLE_SIZE definition
  - id: bytesize-cs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Core/Types/ByteSize.cs
    title: Brutal.Core ByteSize (nuint) / ByteSize64 (ulong)
  - id: asminfo
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/asminfo-full.txt
    title: Metadata dump of Brutal.Vulkan.dll 2026.10.2 (NuGet) and KSA build
  - id: ksa-vulkan
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/Brutal.VulkanApi/Vulkan.cs
    title: KSA decompiled Brutal.VulkanApi.Vulkan (reference only)
  - id: sig-script
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/scripts/lower.py
    title: Script that counts function-pointer signatures after lowering to wasm value types
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:16:59Z }
status: draft
---

# One-paragraph answer

`Brutal.Vulkan` is almost entirely managed C#. **1,955 `.cs` files** are mostly generated from the Vulkan registry: 1,486 structs, 371 enums, 56 handles, 814 function-pointer wrapper types and about 900 extension-method wrappers. Only **five native symbols** are ever bound directly. The first four are the global functions `vkCreateInstance`, `vkEnumerateInstanceExtensionProperties`, `vkEnumerateInstanceLayerProperties` and `vkEnumerateInstanceVersion`, bound through `[LibraryImport("vulkan")]`. The fifth is `vkGetInstanceProcAddr`, obtained with `NativeLibrary.GetExport` in a static constructor.[^import-resolver][^pinvoke-gen] Every other Vulkan entry point is fetched at runtime through `vkGetInstanceProcAddr`/`vkGetDeviceProcAddr`. The results are stored as `delegate* unmanaged[Cdecl]<…>` fields in two big function tables, `VkInstance.FunctionTable` with 105 entries and `VkDevice.FunctionTable` with 697. The tables live in unmanaged memory, and calls go through generated extension methods.[^vkinstance-gen][^vkdevice-gen] This makes it straightforward to put a C# implementation behind BRUTAL on the browser: replace the loader in the static constructor and hand out function pointers to your own implementation. The only real cost is ABI-level; see *Browser implications* below.

# Source layout

| Path (under `/Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/`) | Content | Size |
|---|---|---|
| `ImportResolver.cs` | `static Vulkan()` loader and `Vulkan.GetInstanceProcAddr` property[^import-resolver] | 50 lines |
| `AssemblyInfo.cs` | `[assembly: InternalsVisibleTo("Brutal.Vulkan.Abstractions")]` | 1 line |
| `BitFields.cs` | internal helpers over CommunityToolkit `BitHelper` for bit-field properties | 52 lines |
| `GlobalUsings.cs` | `System.Runtime.CompilerServices`, `System.Runtime.InteropServices`, `Brutal.Logging/Memory/Numerics/Pointers/Pointers.Extensions/Strings`, `CommunityToolkit.HighPerformance.Buffers` | 9 lines |
| `Generated/Functions.gen.cs` | **814** `readonly unsafe struct VkXxx(delegate* unmanaged[Cdecl]<…> handle)` types; each has an `Invoke` field and an `explicit operator VkXxx(PfnVkVoidFunction)`[^functions-gen] | 15,805 lines |
| `Generated/Handles/*.gen.cs` | 56 handle structs; `VkInstance` and `VkDevice` also define the nested `FunctionTable`[^vkinstance-gen][^vkdevice-gen] | 56 files |
| `Generated/Structs/**` | **1,486** structs, all `[StructLayout(LayoutKind.Explicit, Size = N)]` with 64-bit offsets; 49 under `Platforms/` (Win32, Metal, Wayland, Xcb, Xlib, Android) | 1,486 files |
| `Generated/Enums/**` | 371 enums, including `[Flags]` `VkXxxFlags` enums | 371 files |
| `Generated/Extensions/VkDeviceExtensions.gen.cs` | 735 `public static` wrappers for device, queue and command-buffer functions[^devext-gen] | 26,687 lines |
| `Generated/Extensions/VkInstanceExtensions.gen.cs` | 158 wrappers for instance and physical-device functions[^instext-gen] | 5,883 lines |
| `Generated/Extensions/Platforms/*.gen.cs` | Android, Metal, Wayland, Win32, Xcb, Xlib surface and handle extensions | 6 files |
| `Generated/PInvoke/VulkanApi/VulkanApi.gen.cs` | the 4 `[LibraryImport("vulkan")]` externs, in `public static partial class Vulkan.PInvoke`[^pinvoke-gen] | 98 lines |
| `Generated/PInvoke/VulkanApi/VulkanApiEx.gen.cs` | `CreateInstanceInternal`, `EnumerateInstanceExtensionProperties_Count/_Write`, `EnumerateInstanceLayerProperties_Count/_Write` and `EnumerateInstanceVersion`[^pinvoke-ex] | 290 lines |
| `Generated/Globals.gen.cs` | **556** `global using` aliases, e.g. `VkBool32 = Brutal.Bool32`, `VkAccessFlags2 = VkFlags64` and KHR-to-core struct aliases[^globals-gen] | 556 lines |
| `Generated/InlineArrays.gen.cs` | 24 `[InlineArray(N)]` structs such as `VkMemoryType_32` and `VkMemoryHeap_16`[^inline-gen] | 292 lines |
| `Manual/Classes/*` | `Instance`, `PhysicalDevice`, `Device`, `Queue`, `CommandBuffer`, `VkAllocator`, `VkResultError`, `VulkanHandleWrapper` | 8 files |
| `Manual/Partials/*`, `Manual/Structs/*`, `Manual/Constants/VK.cs` | `Vulkan.Log` and `Vulkan.CreateInstance`, `VkFlags`/`VkFlags64`, `VkDeviceAddress`, `VK.WHOLE_SIZE`, convenience constructors | 15 files |

# Loader (the only platform-specific code)

`ImportResolver.cs` declares `public partial class Vulkan`, which merges with the generated `public static partial class Vulkan`. Its static constructor does the following:[^import-resolver]

1. Lines 20–28: `OperatingSystem.IsWindows()` loads `"vulkan-1"` with `DllImportSearchPath.System32`. `IsMacOS()` loads **`"VulkanEx"`** with `LegacyBehavior`; the source comment says "hack which only works if SDK is installed". `IsLinux()` loads `"vulkan"`, which resolves to `libvulkan.so`.
2. Lines 30–31: `if (Dll == nint.Zero) throw new NullReferenceException("Failed to load Vulkan library")`. **On browser-wasm none of the three branches run, so the type initializer throws.** Any access to `Vulkan.*`, including `Vulkan.Log` and `Vulkan.CreateInstance`, then surfaces as `TypeInitializationException`. Code does not reach `Platform`, and the exception is not `PlatformNotSupportedException`.
3. Lines 34–37: `NativeLibrary.SetDllImportResolver(assembly, name => name == "vulkan" ? Dll : 0)` so the four `LibraryImport("vulkan")` externs bind to the same handle.
4. Lines 42–47: `GetInstanceProcAddr = new VkGetInstanceProcAddr((delegate* unmanaged[Cdecl]<VkInstance, byte*, PfnVkVoidFunction>)NativeLibrary.GetExport(Dll, "vkGetInstanceProcAddr"))`, throwing if it is null.

On macOS, `libVulkanEx.dylib` from the NuGet package exports only the four `vx*` helpers and links `@rpath/libvulkan.1.dylib`. `GetExport(VulkanEx, "vkGetInstanceProcAddr")` therefore succeeds only through `dlsym`'s dependency search, i.e. when a `libvulkan.1.dylib` (Vulkan SDK or MoltenVK loader) is findable. This is the "hack" the comment refers to. See [/brutal/vulkan-loader-and-proc-addr-contract.md](/brutal/vulkan-loader-and-proc-addr-contract.md).

The KSA build of the same class is the same apart from field order: `Log` is initialised inside the static constructor, and each of the 4 externs carries both `[DllImport("vulkan", ExactSpelling = true)]` and `[LibraryImport]`, because the decompiler shows the generator output.[^ksa-vulkan]

# The four global P/Invokes and their callers

| `Vulkan.PInvoke.` extern (line in `VulkanApi.gen.cs`)[^pinvoke-gen] | Managed wrapper (line in `VulkanApiEx.gen.cs`)[^pinvoke-ex] | Who calls it |
|---|---|---|
| `vkCreateInstance(VkInstanceCreateInfo*, VkAllocationCallbacks*, VkInstance*)` (25) | `internal Vulkan.CreateInstanceInternal(in VkInstanceCreateInfo, VkAllocator?)` (21); throws `VkResultError` on 8 error codes | `Instance` constructor, `Instance.cs:88`[^instance-cs] |
| `vkEnumerateInstanceExtensionProperties(byte*, int*, VkExtensionProperties*)` (50) | `Vulkan.EnumerateInstanceExtensionProperties_Count(ReadOnlySpan<byte> layer)` / `_Write(...)` (151/161) | application code only (nothing in Brutal.Vulkan or Abstractions) |
| `vkEnumerateInstanceLayerProperties(int*, VkLayerProperties*)` (73) | `Vulkan.EnumerateInstanceLayerProperties_Count()` / `_Write(span)` (231/241) | `VulkanHelpers.Validation` static ctor, `Brutal.Vulkan.Abstractions/VulkanHelpers.cs:65–66` |
| `vkEnumerateInstanceVersion(int*)` (94) | `Vulkan.EnumerateInstanceVersion(int*)` (264) | application code only |

`Vulkan.PInvoke` is `public`, so applications can call these externs directly as well.

# Function-pointer representation

Every Vulkan command is a wrapper struct around a C# 9 unmanaged function pointer:[^functions-gen]

```csharp
public readonly unsafe struct VkDestroyInstance(delegate* unmanaged[Cdecl]<VkInstance, VkAllocationCallbacks*, void> handle)
{
    public readonly delegate* unmanaged[Cdecl]<VkInstance, VkAllocationCallbacks*, void> Invoke = handle;
    public static explicit operator VkDestroyInstance(PfnVkVoidFunction func) => new(( delegate* unmanaged[Cdecl]<...>)func.Invoke);
}
public readonly unsafe struct PfnVkVoidFunction(delegate* unmanaged[Cdecl]<void> handle) { public readonly delegate* unmanaged[Cdecl]<void> Invoke = handle; } // Functions.gen.cs:15701
```

* The file has **814** such types and **632 distinct C# signatures**. After lowering every parameter to a wasm value type (pointer, `nint` or handle to i32, `int`/enum/`Bool32`/`VkFlags` to i32, `ulong`/`ByteSize64`/`VkFlags64`/64-bit enums to i64, `float`/`double` to f32/f64), only **about 51 distinct wasm-level signatures** remain.[^sig-script] This number matters if the calls must cross a native boundary on Mono-wasm, which needs one interp-to-native trampoline per lowered signature. See `/dotnet-wasm/`.
* `uint64_t` timeouts are bound as **`nint`**: `vkWaitForFences`, `vkAcquireNextImageKHR` and the generated `WaitForFences(..., nint timeout)` wrappers at `VkDeviceExtensions.gen.cs:1173`, `8329` and elsewhere. `StagingPool.Wait()` passes `-1`.[^devext-gen] `nint` is 32-bit on wasm32.
* `VkDeviceSize` is bound as `ByteSize64` (a `ulong` record struct). `uint32_t` counts and versions are bound as **`int`**, e.g. `VkPhysicalDeviceProperties.ApiVersion`.

# Function tables

| Table | Built where | Filled by | Count | Memory |
|---|---|---|---|---|
| `VkInstance.FunctionTable` | `Instance` ctor: `_pInvoke = Mem.AllocAndCopy(new VkInstance.FunctionTable(Handle, Vulkan.GetInstanceProcAddr))` (`Instance.cs:90`)[^instance-cs] | ctor at `VkInstance.gen.cs:152`: stores `getProc`, then calls `getProc.Invoke(instance, "<name>"u8.AsPointer())` once per field[^vkinstance-gen] | 105 lookups (+ the stored `vkGetInstanceProcAddr`) | native heap (`Brutal.Memory.Mem`), freed in `Dispose` |
| `VkDevice.FunctionTable` | `Device` ctor: `getDeviceProcAddr = physicalDevice.Instance.GetProcAddr("vkGetDeviceProcAddr"u8)` then `Mem.AllocAndCopy(new VkDevice.FunctionTable(Handle, (VkGetDeviceProcAddr)getDeviceProcAddr))` (`Device.cs:84–85`)[^device-cs] | ctor at `VkDevice.gen.cs:738`: stores `getProc`, then one `vkGetDeviceProcAddr(device, name)` per field, including the 4 `vx*` names at the end[^vkdevice-gen] | 697 lookups (+ stored `vkGetDeviceProcAddr`) | native heap |

* **Every entry is loaded unconditionally**, whether or not its extension or core version is enabled. Nothing checks for null at load time; a null result is stored as it is. A Vulkan implementation may therefore **return NULL for anything it does not implement**. Only an *invocation* of a null entry fails, as a null `calli`.
* `PhysicalDevice.PInvoke` returns `ref Instance.PInvoke`. `Queue.PInvoke` returns `ref Device.PInvoke`. `CommandBuffer` is a `struct` that copies `device._pInvoke` (a `Ptr<VkDevice.FunctionTable>`, which is why `Device._pInvoke` is `internal`).[^physdev-cs][^queue-cs][^cmdbuf-cs] Calls therefore make one pointer hop into the native table plus a `calli`.
* **No per-call logging in Brutal.Vulkan.** The generated Vulkan wrappers contain no `Log.Trace()`; 0 occurrences in `VkDeviceExtensions.gen.cs`. The per-P/Invoke `Log.Trace()` pattern exists in Glfw (154 call sites) and the Sdl packages (about 1,500), not in Vulkan.[^devext-gen] See [/brutal/windowing-input-audio-apis.md](/brutal/windowing-input-audio-apis.md).

# Wrapper and extension-method conventions

* `VulkanHandleWrapper<THandle, TTable>` is an **interface** whose `Handle { get; init; }` member is **`internal`** (`VulkanHandleWrapper.cs:7`). Generated wrappers are extension methods on that interface. Examples: `CreateBuffer(this VulkanHandleWrapper<VkDevice, VkDevice.FunctionTable> handle, in VkBufferCreateInfo, VkAllocator?)` and `GetProperties(this VulkanHandleWrapper<VkPhysicalDevice, VkInstance.FunctionTable>)`.[^wrapper-cs][^devext-gen] Command-buffer wrappers are generic `Draw<T>(this T handle, …) where T : VulkanHandleWrapper<VkCommandBuffer, VkDevice.FunctionTable>`, which avoids boxing the `CommandBuffer` struct.
* Because the interface member is internal, an implementation outside `Brutal.Vulkan` needs the `InternalsVisibleTo`. `Brutal.Vulkan.Abstractions` (`DescriptorPoolEx`, `DescriptorSetLayoutEx`) implements it this way. Abstractions also calls `…Internal` helpers such as `AllocateCommandBuffersInternal`, `FreeCommandBuffersInternal`, `CmdExecuteCommandsInternal`, `FreeDescriptorSetsInternal` and `GetQueueInternal`.
* Return codes: each wrapper `switch`es on `VkResult` and **throws `VkResultError`** for the documented error codes. Examples: `vkCreateBuffer` throws on OutOfHostMemory, OutOfDeviceMemory, InvalidOpaqueCaptureAddress, Unknown and ValidationFailed. `AcquireNextImageKHR` and `PresentKHR` throw on **`ErrorOutOfDateKHR`** but return `SuboptimalKHR`, `Timeout` and `NotReady`.[^devext-gen] An implementation that signals a resize should prefer `VK_SUBOPTIMAL_KHR`, or apps must catch `VkResultError`.
* Enumeration functions are split into `X_Count(...)` and `X_Write(..., Span<T>)`, e.g. `GetQueueFamilyProperties_Count/_Write` and `EnumerateDeviceExtensionProperties_Count/_Write`, so the implementation sees the standard two-call idiom.
* `Queue.Submit`, `Submit2`, `PresentKHR`, `WaitIdle`, `BindSparse` and the debug-label calls are wrapped in a per-queue `System.Threading.Lock` (`Queue.cs:98`).[^queue-cs] This is harmless single-threaded.

# Handles, structs, flags, strings

| Concept | Representation | Note for wasm32 / a C# implementation |
|---|---|---|
| Dispatchable and non-dispatchable handles | `public readonly unsafe struct VkBuffer : IVulkanHandle { public nint VkHandle { get; } … }` for **all 56** handle types[^vkbuffer-gen] | In C, non-dispatchable handles are `uint64_t` even on 32-bit; here they are `nint` (4 bytes on wasm32). Fine when both sides share these types; **incompatible** with a C/C++ `wasm32` Vulkan ABI |
| Structs | `[StructLayout(LayoutKind.Explicit, Size = N)]`, `[FieldOffset]` computed for 64-bit. Example: `VkBufferCreateInfo` Size=56, `void* Next` at 8, `int* QueueFamilyIndices` at 48. 1,486/1,486 structs use explicit layout[^buffer-ci] | On wasm32 a pointer occupies 4 of its 8 reserved bytes. Size and offsets stay identical in C# on both sides, so a C# implementation reads them correctly. A C implementation compiled for wasm32 would disagree on every offset after the first pointer |
| `sType` | `[FieldOffset(0)] internal readonly VkStructureType sType = VkStructureType.BufferCreateInfo;` set by the struct's parameterless primary ctor `VkBufferCreateInfo()`[^buffer-ci] | `default(VkBufferCreateInfo)` has `sType == 0`; only `new()` initialises it. The implementation can validate `sType` |
| `pNext` | plain `public void* Next` | no typed chain builder; the implementation must walk `sType`/`pNext` |
| Fixed arrays | `fixed byte DeviceName[256]` (116 fixed buffers) and 24 `[InlineArray]` types such as `VkMemoryType_32`[^inline-gen] | blittable |
| Flags | real `[Flags] enum VkBufferUsageFlags : int` and similar; 64-bit `…Flags2 : ulong`; `VkFlags(uint)` and `VkFlags64(ulong)` record structs for flag types without bits; `Globals.gen.cs` aliases like `global using VkBufferUsageFlags = VkFlags` that lose to the same-named enum inside `namespace Brutal.VulkanApi`[^globals-gen] | blittable |
| Booleans | `Brutal.Bool32` (4-byte struct with implicit conversion to and from `bool`) | blittable |
| Sizes | `VkDeviceSize` is `ByteSize64` (`ulong`). `VK.WHOLE_SIZE` is a **`ByteSize`** (`nuint`) built as `ByteSize.Of(unchecked((int)~0UL))`[^vkconst-cs][^bytesize-cs] | **On wasm32, `WHOLE_SIZE` becomes `0x0000_0000_FFFF_FFFF` once widened to `ByteSize64`.** The implementation must treat `0xFFFFFFFF` as `VK_WHOLE_SIZE` |
| Strings in | `Instance`/`Device` UTF-8-encode `HashSet<string>` extension and layer names into one `stackalloc` buffer and build `byte**` (`Instance.cs:36–86`, `Device.cs:37–74`)[^instance-cs][^device-cs]; proc names are `"vkXxx"u8` literals | null-terminated UTF-8, as in C |
| Allocation callbacks | `VkAllocator` wraps `VkAllocationCallbacks` in a `PtrOwner` and converts implicitly to `VkAllocationCallbacks*` | the implementation can ignore it |

# Assembly identity and the KSA build

* NuGet `Brutal.Vulkan.dll` 2026.10.2 has AssemblyVersion **1.0.0.0** with no public key, `[assembly: DisableRuntimeMarshalling]`, IVT to `Brutal.Vulkan.Abstractions`, a single ModuleRef `vulkan`, and 2,958 types. It references `Brutal.Core`, `Logging`, `Numerics`, `Memory` and `Strings` (all 1.0.0.0) and `CommunityToolkit.HighPerformance` 8.4.0.0.[^asminfo]
* The KSA build of `Brutal.Vulkan.dll` has AssemblyVersion **2026.9.0.0** and 2,730 types. It references **`Brutal.Core.Common`, `Brutal.Core.Logging`, `Brutal.Core.Numerics`, `Brutal.Core.Memory` and `Brutal.Core.Strings`**, KSA's private split of the core libraries.[^asminfo] Its function tables request **101 instance and 634 device** names. Every one of those names is also requested by the open-source tree, which adds 4 instance and 63 device names from newer registry extensions: GPA sessions (AMD), shader instrumentation (ARM), `*2KHR`/`*2EXT` indirect and transform-feedback variants, `vkCmdCopyMemoryKHR` and similar. Both builds request the 4 `vx*` names. See [/brutal/building-from-source.md](/brutal/building-from-source.md) and [/brutal/ksa-bootstrap-reference.md](/brutal/ksa-bootstrap-reference.md).

# Browser implications (decision-relevant)

1. **The loader must change.** `static Vulkan()` throws on browser, and no NuGet-level knob avoids it. Fork `ImportResolver.cs` in a source build ([/brutal/building-from-source.md](/brutal/building-from-source.md)) so that `OperatingSystem.IsBrowser()` sets `GetInstanceProcAddr` to the C# implementation's entry point and skips `NativeLibrary.Load` and `SetDllImportResolver`.
2. **The four `[LibraryImport("vulkan")]` externs** must also resolve. On Mono-wasm, P/Invokes are bound at build time to statically linked native symbols, and `NativeLibrary.Load` is unavailable (UNVERIFIED in detail; see `/dotnet-wasm/`). Options: (a) patch `CreateInstanceInternal` and the `Enumerate*` wrappers in `VulkanApiEx.gen.cs` to call the C# implementation directly under `IsBrowser()`; (b) provide a tiny C stub library named `vulkan` that forwards into managed code. Option (a) is simpler.
3. **Function pointers.** The generated code calls everything through `delegate* unmanaged[Cdecl]`. A C# implementation can hand out `&Method` addresses of `[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]` static methods, but on Mono-wasm each call then goes interpreter → native trampoline → wasm table → UCO wrapper → interpreter. That needs build-time generated thunks for every signature used (about 51 lowered signatures) and costs a round trip per call. UNVERIFIED for .NET 10 Mono; see `/dotnet-wasm/`. A mechanical **alternative for a source build** is to rewrite `delegate* unmanaged[Cdecl]<` to managed `delegate*<` in `Functions.gen.cs`, so every Vulkan call is a plain managed `calli` into the C# implementation. Struct sizes are unaffected because function pointers are pointer-sized either way. The swap also touches callback types such as `PfnVkDebugUtilsMessengerCallbackEXT` and Abstractions' `VulkanHelpers.DebugUtilsMessengerCallbacks.*`, which are `[UnmanagedCallersOnly]`.
4. **Struct layout is not a blocker for a C# implementation.** A C# implementation compiled into the same wasm module shares these exact struct definitions. Only a C/C++ implementation would hit the 64-bit offsets, the `nint` handles and the `nint` timeouts.
5. **Watch values that depend on `nint`/`nuint`:** `VK.WHOLE_SIZE`, timeouts of `-1` or `nint.MaxValue`, and `ByteSize` arithmetic, all of which become 32-bit.

# Open questions

* UNVERIFIED: whether Mono browser-wasm (.NET 10) generates interp-to-native trampolines for `calli` through `delegate* unmanaged` whose target is a managed `[UnmanagedCallersOnly]` method, and what each call costs. This decides between options 3a (keep unmanaged) and 3b (rewrite to managed function pointers).
* UNVERIFIED: whether `NativeLibrary.SetDllImportResolver` is a no-op or throws on browser-wasm. It does not matter if the loader is patched to skip it.
* UNVERIFIED: how the macOS `VulkanEx` path behaves when only MoltenVK is installed without the LunarG loader. This matters for the desktop reference app on macOS.

[^import-resolver]: Brutal.Vulkan/ImportResolver.cs
[^pinvoke-gen]: Generated/PInvoke/VulkanApi/VulkanApi.gen.cs
[^pinvoke-ex]: Generated/PInvoke/VulkanApi/VulkanApiEx.gen.cs
[^functions-gen]: Generated/Functions.gen.cs
[^vkinstance-gen]: Generated/Handles/VkInstance.gen.cs
[^vkdevice-gen]: Generated/Handles/VkDevice.gen.cs
[^vkbuffer-gen]: Generated/Handles/VkBuffer.gen.cs
[^devext-gen]: Generated/Extensions/VkDeviceExtensions.gen.cs
[^instext-gen]: Generated/Extensions/VkInstanceExtensions.gen.cs
[^globals-gen]: Generated/Globals.gen.cs
[^inline-gen]: Generated/InlineArrays.gen.cs
[^buffer-ci]: Generated/Structs/VkBufferCreateInfo.gen.cs
[^instance-cs]: Manual/Classes/Instance.cs
[^physdev-cs]: Manual/Classes/PhysicalDevice.cs
[^device-cs]: Manual/Classes/Device.cs
[^queue-cs]: Manual/Classes/Queue.cs
[^cmdbuf-cs]: Manual/Classes/CommandBuffer.cs
[^wrapper-cs]: Manual/Classes/VulkanHandleWrapper.cs
[^vkconst-cs]: Manual/Constants/VK.cs
[^bytesize-cs]: Brutal.Core/Types/ByteSize.cs
[^asminfo]: Metadata dump of Brutal.Vulkan.dll (NuGet and KSA)
[^ksa-vulkan]: KSA decompiled Brutal.VulkanApi/Vulkan.cs
[^sig-script]: lower.py signature-lowering script
