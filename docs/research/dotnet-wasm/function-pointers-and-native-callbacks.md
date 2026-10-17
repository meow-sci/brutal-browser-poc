---
type: Research Note
title: Function pointers, UnmanagedCallersOnly and native resolution on .NET 10 browser-wasm (Mono)
description: How Mono browser-wasm in .NET 10 executes `delegate* unmanaged` calls, `[UnmanagedCallersOnly]` callbacks, P/Invokes and `NativeLibrary` lookups, which of them need build-time generated trampolines (and thus the wasm-tools workload and a native relink), and what that means for routing Brutal.Vulkan's function-pointer tables to a C# Vulkan-on-WebGPU implementation.
tags: [dotnet-wasm, mono, interpreter, function-pointers, unmanagedcallersonly, pinvoke, trampolines, nativelibrary, wasm-tools, brutal-vulkan]
sources:
  - id: interp-c
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/mono/mini/interp/interp.c
    title: Mono interpreter (release/10.0) — interp_create_method_pointer, ves_pinvoke_method, MINT_CALLI_NAT*
  - id: transform-c
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/mono/mini/interp/transform.c
    title: Mono interpreter IL transform (release/10.0) — calli lowering, interp_get_icall_sig, interp_type_as_ptr
  - id: aot-runtime-wasm-c
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/mono/mini/aot-runtime-wasm.c
    title: aot-runtime-wasm.c (release/10.0) — signature cookies and "CANNOT HANDLE INTERP ICALL SIG"
  - id: runtime-c
    resource: /Users/asherwin/.nuget/packages/microsoft.netcore.app.runtime.mono.browser-wasm/10.0.1/runtimes/browser-wasm/native/src/runtime.c
    title: Runtime pack src/runtime.c — get_native_to_interp, wasm_dl_load, wasm_dl_symbol (identical file also in 10.0.13 pack)
  - id: pinvoke-c
    resource: /Users/asherwin/.nuget/packages/microsoft.netcore.app.runtime.mono.browser-wasm/10.0.1/runtimes/browser-wasm/native/src/pinvoke.c
    title: Runtime pack src/pinvoke.c — wasm_dl_lookup_pinvoke_table, wasm_dl_get_native_to_interp
  - id: wasm-config-h
    resource: /Users/asherwin/.nuget/packages/microsoft.netcore.app.runtime.mono.browser-wasm/10.0.1/runtimes/browser-wasm/native/include/wasm/wasm-config.h
    title: Runtime pack include/wasm/wasm-config.h (DISABLE_THREADS; no WASM_SUPPORTS_DLOPEN)
  - id: pinvoke-collector
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/tasks/WasmAppBuilder/PInvokeCollector.cs
    title: WasmAppBuilder PInvokeCollector.cs (release/10.0) — what the build scans
  - id: pinvoke-table-gen
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/tasks/WasmAppBuilder/PInvokeTableGenerator.cs
    title: WasmAppBuilder PInvokeTableGenerator.cs (release/10.0) — pinvoke-table.h and native-to-interp thunks
  - id: interp-to-native-gen
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/tasks/WasmAppBuilder/InterpToNativeGenerator.cs
    title: WasmAppBuilder InterpToNativeGenerator.cs (release/10.0) — wasm_m2n_invoke.g.h
  - id: signature-mapper
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/tasks/WasmAppBuilder/SignatureMapper.cs
    title: WasmAppBuilder SignatureMapper.cs (release/10.0) — type to cookie char mapping
  - id: managed-to-native-gen
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/tasks/WasmAppBuilder/ManagedToNativeGenerator.cs
    title: WasmAppBuilder ManagedToNativeGenerator.cs (release/10.0)
  - id: wasmapp-common-targets
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.sdk/Sdk/WasmApp.Common.targets
    title: WasmApp.Common.targets from Microsoft.NET.Runtime.WebAssembly.Sdk 10.0.13 (also src/mono/wasm/build/WasmApp.Common.targets on GitHub)
  - id: browserwasmapp-targets
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.sdk/Sdk/BrowserWasmApp.targets
    title: BrowserWasmApp.targets from Microsoft.NET.Runtime.WebAssembly.Sdk 10.0.13 (also src/mono/browser/build/BrowserWasmApp.targets)
  - id: mono-toolchain-targets
    resource: /usr/local/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.mono.toolchain.current/10.0.101/WorkloadManifest.targets
    title: Mono toolchain workload manifest targets 10.0.101 (_WasmNativeWorkloadNeeded list)
  - id: sig-classification
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/sig-classification.txt
    title: Prebuilt-runtime trampoline inventory (from dotnet.native.js.symbols, runtime pack 10.0.13 and 10.0.1) and classification of Brutal.Vulkan signatures
  - id: classify-script
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/scripts/classify.py
    title: Script that lowers Brutal.Vulkan function-pointer signatures with Mono's interpreter rules
  - id: probe-build
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/proj/WasmProbe
    title: Throwaway wasmbrowser project built with SDK 10.0.100 without wasm-tools (build/publish logs, NETSDK1147 errors)
  - id: issue-106529
    resource: https://github.com/dotnet/runtime/issues/106529
    title: "UnmanagedCallersOnly methods crash at runtime without relink (open)"
  - id: issue-101434
    resource: https://github.com/dotnet/runtime/issues/101434
    title: "[mono][wasm] TrimMode=full is trimming UnmanagedCallersOnly (open)"
  - id: issue-104391
    resource: https://github.com/dotnet/runtime/issues/104391
    title: "[Mono/WASM] Marshal.GetFunctionPointerForDelegate crashes the runtime (open)"
  - id: issue-130739
    resource: https://github.com/dotnet/runtime/issues/130739
    title: "[wasm] reverse P/Invoke thunk key drops enclosing type for nested [UnmanagedCallersOnly] methods (open, 2026-07)"
  - id: issue-56145
    resource: https://github.com/dotnet/runtime/issues/56145
    title: "[WASM] callback from native code to .NET: Parsing function pointer types in signatures is not supported (closed 2026-07-20)"
  - id: ms-native-deps
    resource: https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-native-dependencies?view=aspnetcore-10.0
    title: ASP.NET Core Blazor WebAssembly native dependencies (.NET 10)
  - id: uno-webgpu-targets
    resource: https://github.com/unoplatform/uno/blob/master/src/Uno.UI.Composition.WebGpu/package/Uno.WinUI.Composition.WebGpu.targets
    title: Uno Platform WebGPU package targets (adds a static P/Invoke module "webgpu")
  - id: brutal-import-resolver
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/ImportResolver.cs
    title: Brutal.Vulkan ImportResolver.cs (static Vulkan() loader)
  - id: brutal-functions-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Functions.gen.cs
    title: Brutal.Vulkan Functions.gen.cs (814 delegate* unmanaged[Cdecl] wrappers)
  - id: brutal-binding-doc
    resource: /brutal/vulkan-binding-architecture.md
    title: Brutal.Vulkan binding architecture (sibling research note)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T15:57:04Z }
status: draft
---

# Bottom line (decision-relevant)

| Question | Answer for .NET 10 Mono browser-wasm | Evidence |
|---|---|---|
| Does `&UcoMethod` (an `[UnmanagedCallersOnly]` C# method) produce a callable native pointer **without** the `wasm-tools` workload (interpreter, prebuilt `dotnet.native.wasm`)? | **No.** The prebuilt runtime contains exactly 8 native→interp thunks, all for `System.Private.CoreLib` methods. User UCO methods have no thunk, so they fail. The source raises `PlatformNotSupportedException` ("No native to managed transition for method '…', missing [UnmanagedCallersOnly] attribute."). The observed symptom in the open issue is `RuntimeError: null function or function signature mismatch`. | [^runtime-c][^interp-c][^sig-classification][^issue-106529] |
| Does it work **with** `wasm-tools` and a native relink? | **Yes.** At relink time the build scans every app assembly for `[UnmanagedCallersOnly]` (and `[MonoPInvokeCallback]`) methods and emits one C thunk per method into `pinvoke-table.h`. | [^pinvoke-collector][^pinvoke-table-gen][^wasmapp-common-targets] |
| Can interpreted C# call a `delegate* unmanaged[Cdecl]<…>` pointer without relink? | **Only for some signatures.** (a) The fast path applies when the call has at most 6 parameters, every one pointer-like (pointers, `nint`/`nuint`, `int`, `bool`, `char`, any enum, or a 1-field struct of those), and the return is `void` or pointer-like. That path needs no table. (b) Otherwise the signature "cookie" must be in the prebuilt table of 52 cookies. (c) Anything else aborts the runtime with `CANNOT HANDLE INTERP ICALL SIG <cookie>`. UNVERIFIED by experiment: this comes from reading `transform.c`, and the indirect wrapper (`mono_marshal_get_native_func_wrapper_indirect`, built with `mono_mb_new` + `mono_mb_create_and_cache_full`) is assumed to be non-dynamic, so the fast path applies. | [^transform-c][^aot-runtime-wasm-c][^sig-classification] |
| Does relinking add trampolines for every `calli` signature in my code? | **No.** The generator collects signatures only from `[DllImport]` declarations (including the inner ones that `[LibraryImport]` generates), from delegate types marked `[UnmanagedFunctionPointer]`, and from icalls. It never scans `calli` sites. Missing signatures therefore need "anchor" declarations. | [^pinvoke-collector][^managed-to-native-gen][^interp-to-native-gen] |
| AOT (`RunAOTCompilation=true`)? | AOT'd code compiles `calli unmanaged` to a direct wasm `call_indirect` and compiles the UCO wrappers, so neither cookie table is consulted on AOT paths. AOT always implies `wasm-tools` plus relink, and the tables are still generated for interpreter fallback. UNVERIFIED: not exercised on this machine (no `wasm-tools`). | [^wasmapp-common-targets] |
| Can a pointer from `&Method` be stored and invoked later? | **Yes, once it exists.** With relink, `&UcoMethod` is the address (wasm table index) of a static generated C function. It stays valid for the lifetime of the module, and the thunk lazily initialises itself if native code calls it first. Caveats are listed below. | [^pinvoke-table-gen][^pinvoke-c] |
| `Marshal.GetFunctionPointerForDelegate`? | **Not usable** in the interpreter. Thunks exist only for UCO methods, and the open issue shows a crash (`memory access out of bounds … get_native_to_interp`). | [^issue-104391] |
| `NativeLibrary.Load("vulkan")` / `dlopen`? | **No `dlopen`.** `WASM_SUPPORTS_DLOPEN` is not defined for browser. `Load` succeeds only if `"vulkan"` names a P/Invoke module that was linked statically, and `GetExport` finds only symbols that some scanned `[DllImport]` referenced. | [^wasm-config-h][^runtime-c][^pinvoke-c] |

**Consequence for the PoC.** Brutal.Vulkan routes every Vulkan command through `delegate* unmanaged[Cdecl]` (814 wrapper types).[^brutal-functions-gen][^brutal-binding-doc] To point those pointers at C# `[UnmanagedCallersOnly]` methods, the project **must** install `wasm-tools` and relink (`WasmBuildNative=true`). It must also add signature anchors for the roughly 29 lowered signatures that are neither fast-path nor prebuilt. Without `wasm-tools`, the only fully working route is a **source build of Brutal.Vulkan that turns the unmanaged function pointers into managed `delegate*<…>`**. Managed `calli` stays inside the interpreter, needs no native transition, no trampolines and no workload, and is also the cheaper call (see [Options](#options-for-the-vulkan-on-webgpu-shim)).

# How Mono-wasm crosses the managed/native boundary

WebAssembly cannot emit machine code at run time the way a desktop JIT does (the jiterpreter compiles small wasm modules, but only for interpreter traces and AOT call thunks, see [/dotnet-wasm/performance-and-aot.md](/dotnet-wasm/performance-and-aot.md)). Mono therefore relies on **C trampolines generated at build time** for every call that crosses from interpreted IL into native code, or back.

## Interpreter → native (P/Invoke, `calli unmanaged`)

1. Roslyn emits `calli unmanaged cdecl` for a `delegate* unmanaged[Cdecl]` call. The interpreter replaces it with a call to a *native-func-wrapper* (`mono_marshal_get_native_func_wrapper_indirect`), which performs the GC transition. Inside that wrapper the actual `calli` is "native".[^transform-c]
2. The native `calli` lowers to one of three opcodes:[^transform-c][^interp-c]
   - **`MINT_CALLI_NAT_FAST`** when `interp_get_icall_sig` recognises the signature. That means 0–6 parameters, all *pointer-like*, and a `void` or pointer-like return. The call becomes a plain C call through a casted function pointer (`do_icall_wrapper`). **No table lookup.**
   - **`MINT_CALLI_NAT_DYNAMIC`** for dynamic methods only.
   - **`MINT_CALLI_NAT`** for everything else. It calls `ves_pinvoke_method`, which calls `mono_wasm_get_interp_to_native_trampoline(sig)`.
3. `mono_wasm_get_interp_to_native_trampoline` builds a **cookie**, one char per return/parameter: `V` void, `I` i32 (pointers, `nint`, `int`, `uint`, small ints, bool, char, int-based enums, 1-field structs, by-ref structs), `L` i64, `F` f32, `D` f64. Structs larger than one scalar are passed by pointer; a by-value struct *return* becomes `V` plus a leading `I`. The function then bsearches the generated `interp_to_native_invokes[]` table. The build-side mapping in `SignatureMapper` uses the same letters.[^signature-mapper] If nothing is found:[^aot-runtime-wasm-c]

   ```c
   g_error ("CANNOT HANDLE INTERP ICALL SIG %s\n", cookie);   // fatal: aborts the runtime
   ```

"Pointer-like" for the fast path is exactly `interp_type_as_ptr`: byref, `nint`/`nuint`, pointers, function pointers, object references, **`int` (I4) but not `uint` (U4)**, `bool`, `char`, any enum, and a struct of size ≤ 4 with one field that is itself pointer-like. `long`/`ulong`, `float`/`double`, `byte`/`short` and `uint` are **not** pointer-like on wasm32.[^transform-c]

> UNVERIFIED (source reading): `interp_type_as_ptr` returns TRUE for **every** enum, including `ulong`-based ones such as `VkPipelineStageFlags2 : ulong`. On wasm32 a fast-path call with such a parameter would be emitted as an i32 `call_indirect` against a callee that expects i64. The likely result is a "function signature mismatch" trap or a truncated value. This affects 4 Brutal.Vulkan wrappers (cookies `viil`, `vilii`).[^sig-classification] **Test these explicitly.**

## Native → interpreter (`[UnmanagedCallersOnly]` reverse P/Invoke)

- Taking `&Method` of a UCO method goes through a `NATIVE_TO_MANAGED` wrapper and `interp_create_method_pointer`. That function asks the host, via `mono_wasm_get_native_to_interp_trampoline`, for a C function with the **native** signature. If none exists it sets `PlatformNotSupportedException`:[^interp-c]
  `"No native to managed transition for method '%s', missing [UnmanagedCallersOnly] attribute."` (the message is misleading: the attribute *is* present; the thunk is what is missing).
- The host implementation (`get_native_to_interp` in `runtime.c`) builds the key `"{MethodName}#{paramCount}:{AssemblyName}:{Namespace}:{TypeName}"` and calls `wasm_dl_get_native_to_interp(token, key, …)`. That function bsearches `wasm_native_to_interp_table`, first by key+token, then by key only to tolerate trimming.[^runtime-c][^pinvoke-c]
- Each generated thunk (`wasm_native_to_interp_<Asm>_<Ns>_<Type>_<Method>`) has the real C signature. On first call it lazily resolves the managed wrapper (`mono_wasm_marshal_get_managed_wrapper`) and then enters the interpreter through the stored `InterpFtnDesc`.[^pinvoke-table-gen]
- A UCO with `EntryPoint = "name"` is additionally emitted with `__attribute__((export_name("name")))`. Its thunk is then a named wasm export, callable from C by that name and visible in `Module.wasmExports`.[^pinvoke-table-gen] UNVERIFIED: calling such exports directly from JS was not tested.

## What the prebuilt runtime contains (ground truth)

The `dotnet.native.js.symbols` map of the runtime pack lists the generated functions. Runtime packs 10.0.1 and 10.0.13 contain the same set:[^sig-classification]

- **52 interp→native cookies**: `dd ddd dddd ddi di ff fff ffff ffi i ii iii iiii iiiii iiiiii iiiiiii iiiiiiii iiiiiiiii iiiil iiil iiiliiii iil iili iiliiil iill iilli il ili js jsexport jsimport l li liiiii liiil lil lili lill v vdddddddddii vdi vdiii vi vii viid viii viiii viiiii viiiiii viiiiiii viiiil vl`.
- **8 native→interp thunks**, all in `System.Private.CoreLib`: `ComponentActivator.{GetFunctionPointer, LoadAssembly, LoadAssemblyAndGetFunctionPointer, LoadAssemblyBytes}`, `EventPipeEventProvider.Callback`, `CalendarData.EnumCalendarInfoCallback`, `ThreadPool.BackgroundJobHandler`, `TimerQueue.TimerHandler`.

So any **user** `[UnmanagedCallersOnly]` method is unreachable from native code unless the app relinks.

# What the relink generates (and what it does not)

`_GenerateManagedToNative` (in `WasmApp.Common.targets`) runs the `ManagedToNativeGenerator` task over `@(_WasmAssembliesInternal)` plus `mscorlib.dll`. It writes:[^wasmapp-common-targets][^managed-to-native-gen]

| File (in `obj/<cfg>/net10.0/wasm/for-build/` or `for-publish/`) | Content |
|---|---|
| `pinvoke-table.h` | the P/Invoke tables per module, plus `wasm_native_to_interp_ftndescs[]`, the thunks and `wasm_native_to_interp_table[]` |
| `wasm_m2n_invoke.g.h` | `wasm_invoke_<cookie>()` functions and `interp_to_native_invokes[]` |
| `icall-table.h`, `runtime-icall-table.h` | internal-call tables (used when `WasmLinkIcalls=true`) |
| `m2n_cache.txt` | incremental-build cache |

The link step (`_BrowserWasmLinkDotNet`) then compiles the runtime-pack sources `pinvoke.c`, `driver.c`, `runtime.c` and `corebindings.c` against these headers and links everything with `emcc` into a new `dotnet.native.wasm`/`dotnet.native.js`.[^browserwasmapp-targets]

Name check: there is **no** MSBuild property such as `WasmInterpToNativeSignatures`, and no user-facing knob for adding signatures. The only inputs are the scanned assemblies and the module list. Likewise there is no "Cannot find trampoline" message: the interpreter-side failure is `CANNOT HANDLE INTERP ICALL SIG …`, and the reverse-P/Invoke failure is the PNSE text quoted above.[^wasmapp-common-targets][^aot-runtime-wasm-c][^interp-c]

`PInvokeCollector.CollectPInvokes` decides what goes in:[^pinvoke-collector]

- methods with `MethodAttributes.PinvokeImpl`, i.e. `[DllImport]`, including the inner `[DllImport]` local function that the `[LibraryImport]` source generator emits. Each adds a table entry **and** a cookie;
- methods with `[UnmanagedCallersOnly]` or an attribute named `MonoPInvokeCallbackAttribute` add a native→interp thunk;
- delegate types carrying `[UnmanagedFunctionPointer]` add the cookie of their `Invoke` signature;
- icall signatures (`IcallTableGenerator`).

**Nothing scans `calli` instructions.** A `delegate* unmanaged[Cdecl]<…>` call whose signature is neither fast-path nor already present will still abort after a relink. The cheapest **signature anchor** is an unused delegate type per missing cookie:

```csharp
// SignatureAnchors.cs (browser build only). Never invoked; exists so the relink emits wasm_invoke_<cookie>.
using System.Runtime.InteropServices;
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Sig_vif(nint cmd, float width);            // e.g. vkCmdSetLineWidth
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Sig_viff(nint cmd, float a, float b);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Sig_vil(nint cmd, ulong x);
// … one per cookie listed in the Brutal.Vulkan analysis below
```

The alternative is a dummy `[DllImport("__Internal")]` with the same shape. That also creates a P/Invoke table entry, which needs the symbol to exist at link time (or `WasmAllowUndefinedSymbols=true`). UNVERIFIED: anchors were not built here because `wasm-tools` is not installed. The mechanism follows directly from the generator source.

Other generator rules that bite:

- **Unique callback keys.** Two UCO methods in the same type with the same name and parameter count fail the build: `Two callbacks with the same Name and number of arguments '…' are not supported.` Nested types with the same simple name across namespaces also collide (issue still open in 2026; the failure happens at build time).[^pinvoke-table-gen][^issue-130739] Give every UCO a unique `Type.Method` name.
- **Blittability.** Unless the assembly has `[DisableRuntimeMarshalling]`, UCO parameters and returns must be blittable or the build errors (`Parameter types of pinvoke callback method '…' needs to be blittable.`).[^pinvoke-collector] (Brutal.Vulkan itself has `[assembly: DisableRuntimeMarshalling]`.[^brutal-binding-doc])
- **Function-pointer types in signatures.** `MetadataLoadContext` historically could not decode them, hence the docs' advice to declare such parameters as `IntPtr`.[^ms-native-deps] The tracking issue closed on 2026-07-20, but whether that fix is in a 10.0.x servicing build is UNVERIFIED. Use `nint` for function-pointer parameters of UCO methods and P/Invokes in the browser build.[^issue-56145]
- **Trimming.** With `TrimMode=full`, ILLink can remove UCO methods that only native code references. The generator runs after ILLink in the nested publish, so the thunk silently disappears (issue open). The known workaround is to take the method's address in managed code (`(nint)(delegate* unmanaged<…>)&M`), which also roots it.[^issue-101434]

# When does a relink happen? (and the workload requirement)

- `WasmBuildNative` defaults (from `_SetWasmBuildNativeDefaults`):[^wasmapp-common-targets]
  - **Build**: `true` if `@(NativeFileReference)` is non-empty, if any "relink-triggering" property differs from its runtime-pack default, or if `RunAOTCompilation` and `RunAOTCompilationAfterBuild` are both `true`.
  - **Publish**: `true` if `RunAOTCompilation=true`; else if there are `NativeFileReference`s; else `false` when `PublishTrimmed != true`; else `true` in `Release`.
  - Relink-triggering properties (from the runtime pack's `wasm-props.json`) are `InvariantTimezone`, `InvariantGlobalization`, `WasmNativeStrip`, `WasmSingleFileBundle`, `WasmEnableSIMD`, `WasmEnableExceptionHandling`, `EnableDiagnostics`, `WasmProfilers`, `EmccMaximumHeapSize`, `EmccInitialHeapSize` and `RunAOTCompilation`.
- The workload is required whenever any of the following is set: `WasmEnableSIMD=false`, `WasmEnableExceptionHandling=false`, `InvariantTimezone=true`, `WasmNativeStrip=false`, `WasmNativeDebugSymbols=true`, `WasmSingleFileBundle=false` (sic), `EnableDiagnostics=true`, `WasmProfilers`, `RunAOTCompilation=true`, `WasmBuildNative=true`, `WasmGenerateAppBundle=true`, a non-WebAssembly-SDK project, `EmccInitialHeapSize` or `EmccMaximumHeapSize` (`_WasmNativeWorkloadNeeded` in the workload manifest targets).[^mono-toolchain-targets] Without the workload the SDK fails up front. Observed on this Mac with SDK 10.0.100 for `-p:WasmBuildNative=true`, `-p:RunAOTCompilation=true` (publish) and `-p:EmccMaximumHeapSize=…`:[^probe-build]

  ```text
  error NETSDK1147: To build this project, the following workloads must be installed: wasm-tools
  error NETSDK1147: To install these workloads, run the following command: dotnet workload restore
  ```

- A `NativeFileReference` without the workload only produces a warning: `@(NativeFileReference) is not empty, but the native references won't be linked in, because neither $(WasmBuildNative), nor $(RunAOTCompilation) are 'true'.`[^mono-toolchain-targets]
- **Nothing warns that a UCO method exists while relink is off.** The maintainers acknowledge this gap ("compiling code with a UCO basically means WasmBuildNative must be true"). The issue's workaround to force a relink is `<WasmNativeStrip>false</WasmNativeStrip>`; `<WasmBuildNative>true</WasmBuildNative>` is the explicit way.[^issue-106529]

# Storing and invoking pointers later — caveats

1. **Static only.** UCO methods are static. Carry instance state through a `GCHandle` passed as `void*`/`nint` "user data", or keep the state in static tables keyed by handle values (the natural fit for Vulkan handles).
2. **Exact wasm type match.** `call_indirect` traps on any mismatch (`RuntimeError: null function or function signature mismatch`). The thunk's C types come from `MapType`: `int`→`int32_t`, `uint`→`uint32_t`, `long`/`ulong`→`int64_t`/`uint64_t`, `float`, `double`, pointers and `nint`→`void*`, a 1-field struct → its field type, any other struct → `void*`.[^pinvoke-table-gen] The caller's cookie must lower to the same wasm types.
3. **Null pointers.** Brutal.Vulkan stores whatever `vkGet*ProcAddr` returns and only fails when a null entry is invoked.[^brutal-binding-doc] A null `calli` on wasm is a trap, not a managed exception.
4. **Exceptions.** Do not let managed exceptions escape a UCO method; catch inside and return a `VkResult`. UNVERIFIED: what Mono-wasm does if one escapes through the native frame.
5. **Re-entrancy.** Calls stay on the single browser thread. Interpreter → native → interpreter nesting is allowed, but nothing may block (see [/dotnet-wasm/main-loop-threading-and-pitfalls.md](/dotnet-wasm/main-loop-threading-and-pitfalls.md)).
6. **Lifetime.** Thunk addresses are valid until the runtime exits. Calling one after `exit()` is undefined. UNVERIFIED.

# `Marshal.GetFunctionPointerForDelegate` / `GetDelegateForFunctionPointer`

- `GetFunctionPointerForDelegate(new Action(...))` needs a native→interp thunk for an arbitrary delegate, which the build never generates. Issue #104391 reproduces `RuntimeError: memory access out of bounds` inside `get_native_to_interp`. The maintainer's comment: "even if we were to fix this, the testcase couldn't possible work on wasm interp nowadays … these thunks are generated … only for methods with `UnmanagedCallersOnly` attribute".[^issue-104391] **Do not use it in the browser build.**
- `GetDelegateForFunctionPointer<T>` turns a native pointer into a delegate whose `Invoke` performs a native call. It needs the cookie for `T`'s signature: fast path, prebuilt, or relink plus `[UnmanagedFunctionPointer]` on `T`. UNVERIFIED: not tested.

# P/Invoke (`[DllImport]` / `[LibraryImport]`) resolution on browser-wasm

- **Static only.** P/Invokes resolve against tables compiled into `dotnet.native.wasm`. The module list (`_WasmPInvokeModules`) is the **file name without extension of each `@(NativeFileReference)`** (unless it has `ScanForPInvokes="false"`), plus `libSystem.Native`, `libSystem.IO.Compression.Native` and `libSystem.Globalization.Native`. Two special module names are always accepted: `"__Internal"` and `"*"` ("static linking without specifying the module").[^wasmapp-common-targets][^pinvoke-table-gen]
- A `[DllImport("X")]` whose module is not in that list is **left out of the table**. Its cookie is still collected. At runtime the lookup fails. UNVERIFIED: the exact exception (`DllNotFoundException` vs `EntryPointNotFoundException`) was not observed.
- **`[WasmImportLinkage]`** on a `[DllImport("mod", EntryPoint="fn")]` adds `mod` as a module and declares the function `__attribute__((import_module("mod"), import_name("fn")))`, i.e. a real wasm import.[^pinvoke-collector][^pinvoke-table-gen] On browser this import has to be supplied when the module is instantiated. `dotnet.runtime.js` calls a user `instantiateWasm` from `withModuleConfig({ instantiateWasm })` if one is given. UNVERIFIED: whether Emscripten 3.1.56's link step accepts a non-`env` import module, and whether this works end-to-end. It would be a zero-glue way to bind JS functions as C ABI functions.
- **`NativeLibrary.Load` / `TryLoad` / `GetExport`.** `WASM_SUPPORTS_DLOPEN` is not defined in the browser build.[^wasm-config-h] `wasm_dl_load(name)` returns the P/Invoke table whose module name equals `name`, a sentinel for `System.Globalization.Native`, and otherwise `NULL`. `wasm_dl_symbol(handle, sym)` bsearches that table's import list.[^runtime-c] Consequences:
  - `NativeLibrary.Load("vulkan")` succeeds only if a P/Invoke module named `vulkan` was linked (for example `NativeFileReference Include="vulkan.c"`). UNVERIFIED: whether Mono's name probing (`libvulkan`, `vulkan.so`, …) also matches tables with prefixed or suffixed names.
  - `NativeLibrary.GetExport(h, "vkGetInstanceProcAddr")` succeeds only if some scanned `[DllImport("vulkan", EntryPoint="vkGetInstanceProcAddr")]` caused the symbol to enter the table. Brutal.Vulkan's four `[LibraryImport("vulkan")]` do not include `vkGetInstanceProcAddr`; it is fetched with `GetExport`.[^brutal-import-resolver]
- **`NativeLibrary.SetDllImportResolver`.** Mono calls the managed resolver (`MonoLoadLibraryCallbackStub`) and then looks the returned handle up or wraps it raw. Symbol lookup still goes through `wasm_dl_symbol`, which treats the handle as a P/Invoke table pointer.[^runtime-c] UNVERIFIED (source reading): any handle not obtained from `NativeLibrary.Load` on this runtime makes `wasm_dl_symbol` read arbitrary memory. Do not fake handles.
- **Brutal.Vulkan specifically.** `static Vulkan()` loads the library only under `IsWindows/IsMacOS/IsLinux` and throws `NullReferenceException("Failed to load Vulkan library")` otherwise. On browser this surfaces as `TypeInitializationException`, before any resolver logic runs.[^brutal-import-resolver][^brutal-binding-doc]

## Can `[LibraryImport("vulkan")]` be satisfied natively in the browser?

| Provider | How | Needs | Notes |
|---|---|---|---|
| C compiled by Emscripten | `<NativeFileReference Include="native/vulkan.c" />` (file name must be `vulkan`), exporting `vkCreateInstance` etc. | wasm-tools + relink | The C code can call back into C# through UCO thunks: `[UnmanagedCallersOnly(EntryPoint="cs_vkCreateInstance")]` is exported by that name.[^pinvoke-table-gen] Struct layouts must match BRUTAL's 64-bit explicit offsets, which matters only if C reads them.[^brutal-binding-doc] |
| Prebuilt `.a`/`.o` | `NativeFileReference` to a `vulkan.a` built with **Emscripten 3.1.56** | wasm-tools + relink | Same module-name rule.[^ms-native-deps] |
| Emscripten JS library | `EmccExtraLDFlags="--js-library vulkan_shim.js"` defines `vkCreateInstance` in JS, and the module name must reach `_WasmPInvokeModules` | wasm-tools + relink | `_WasmPInvokeModules` is internal. Uno Platform adds `<_WasmPInvokeModules Include="webgpu" />` in a target `BeforeTargets="_GenerateManagedToNative"` so `[DllImport("webgpu")]` binds to emdawnwebgpu's symbols.[^uno-webgpu-targets] `"__Internal"`/`"*"` are the supported spellings, but Brutal hard-codes `"vulkan"`. |
| Managed patch | Change `VulkanApiEx.gen.cs` / the loader in a source build to call the C# implementation directly | none | Simplest; recommended in [/brutal/vulkan-binding-architecture.md](/brutal/vulkan-binding-architecture.md).[^brutal-binding-doc] |

# Brutal.Vulkan signature analysis (prebuilt runtime vs fast path)

`classify.py` parses all 814 `delegate* unmanaged[Cdecl]<…>` wrappers (632 distinct C# signatures). It lowers each one with the interpreter's `interp_type_as_ptr` rules and the generator's cookie mapping. Handles are `nint` structs (`I`); `int` and `int`-based enums are `I` and pointer-like; `uint`, `VkFlags(uint)`, `ushort` are `I` but not pointer-like; `ByteSize64`, `VkFlags64`, `VkDeviceAddress`, `ulong` are `L`; `float` is `F`.[^classify-script][^sig-classification]

| Category (caller side, interpreter) | Wrappers | Distinct cookies |
|---|---|---|
| Fast path (`MINT_CALLI_NAT_FAST`), no table needed | **730** | 13 |
| Cookie already in the prebuilt runtime | 25 | 7 |
| **Needs relink + anchor** | **55** | **29**: `iiiiiiili iiillii lii liiii vi?i(=viii) vif viff vifff viif viiiiiiii viiiiiiiiii viiiiiiiiiii viiiiil viiiiilli viiiilii viiili viiiliiiil viil viili viilii viililii viilillillilliii viilli vil vilii viliiiii vilili villi villlii` |
| Fast path chosen although a parameter is a 64-bit enum (risk, see above) | 4 | 2 (`viil`, `vilii`) |

(`vi?i` is a `VkSamplerYCbCrConversion` spelling the script did not resolve; it is a handle, so the cookie is `viii`, which is prebuilt.) This covers **only the caller side**. Whenever the callee is a C# `[UnmanagedCallersOnly]` method, every one of the 814 entry points needs a native→interp thunk, which only a relink provides.

# Options for the Vulkan-on-WebGPU shim

| Option | Workload | How Brutal calls the C# implementation | Per-call path | Verdict |
|---|---|---|---|---|
| **A. UCO + relink** | `wasm-tools` (Emscripten 3.1.56 toolchain) | unchanged `delegate* unmanaged[Cdecl]`; `vkGet*ProcAddr` returns `(nint)&UcoMethod` | interp → native-func-wrapper (GC transition) → fast/cookie trampoline → wasm `call_indirect` → generated C thunk → interpreter entry | Works with stock NuGet `Brutal.Vulkan` (loader still needs patching). Needs anchor delegates for ~29 cookies. Two boundary crossings per Vulkan call. |
| **B. Managed function pointers** (source build) | none | rewrite `delegate* unmanaged[Cdecl]<…>` → `delegate*<…>` in `Functions.gen.cs`; UCO callbacks become plain static methods | interp `calli` → interpreted method | No trampolines and no relink. The cheapest call. Requires forking or regenerating Brutal.Vulkan ([/brutal/vulkan-binding-architecture.md](/brutal/vulkan-binding-architecture.md)). |
| **C. JS functions as callees** | none | `Module.addFunction(jsFn, sig)` (exported runtime method; the table is growable) returns a wasm table index used as the pointer | interp fast/cookie path → JS | Callee side needs no thunk; the caller side has the same cookie limits. Puts the translation in JS, against the plan. UNVERIFIED. |
| **D. Mono AOT** | `wasm-tools`, publish-only (`Release`, trimmed) | unchanged | AOT `call_indirect` → AOT-compiled UCO wrapper | Fastest stock-binding path; slow builds (minutes). Use for perf runs, not the inner loop. |
| **E. NativeAOT-LLVM** | experimental packages, Linux/Windows host | unchanged | native wasm calls | See [/dotnet-wasm/nativeaot-llvm-and-alternatives.md](/dotnet-wasm/nativeaot-llvm-and-alternatives.md). |

Recommended experiment order:
1. B in the interpreter, which proves the translation layer with zero native tooling.
2. A, which proves stock-binding compatibility once `wasm-tools` is installed.
3. D for performance.

See [/experiments/](/experiments/) for the smoke tests run by the companion agent.

## Open questions

- UNVERIFIED: that `delegate* unmanaged` calls from interpreted code with ≤6 pointer-like arguments really take `MINT_CALLI_NAT_FAST` without relink (source-derived; needs a smoke test calling e.g. a JS function added with `Module.addFunction`).
- UNVERIFIED: whether a fast-path `calli` with a `ulong`-based enum parameter traps or truncates on wasm32 (Brutal cookies `viil`, `vilii`).
- UNVERIFIED: the exact runtime symptom in .NET 10.0.x for `&UcoMethod` without relink. Is it PNSE "No native to managed transition…" or the `null function or function signature mismatch` trap seen in #106529 (.NET 9 era)?
- UNVERIFIED: anchor delegates (`[UnmanagedFunctionPointer]`) were not built here because `wasm-tools` is not installed. Confirm that `wasm_m2n_invoke.g.h` then contains `wasm_invoke_vif` etc.
- UNVERIFIED: whether the issue #56145 fix (function-pointer types in P/Invoke/UCO signatures) shipped in a 10.0.x servicing release.
- UNVERIFIED: `GetDelegateForFunctionPointer` behaviour; behaviour of `NativeLibrary.SetDllImportResolver` with foreign handles; exact exception for a P/Invoke to an unlinked module; Mono's library-name probing against table names.
- UNVERIFIED: `[WasmImportLinkage]` plus a user `instantiateWasm` as a JS-provided C ABI on browser with Emscripten 3.1.56.
- UNVERIFIED: escaping managed exceptions from UCO methods on wasm.
- UNVERIFIED: calling `[UnmanagedCallersOnly(EntryPoint=…)]` exports directly from JS via `Module.wasmExports`.

[^interp-c]: Mono interpreter interp.c (release/10.0)
[^transform-c]: Mono interpreter transform.c (release/10.0)
[^aot-runtime-wasm-c]: aot-runtime-wasm.c (release/10.0)
[^runtime-c]: Runtime pack native/src/runtime.c
[^pinvoke-c]: Runtime pack native/src/pinvoke.c
[^wasm-config-h]: Runtime pack native/include/wasm/wasm-config.h
[^pinvoke-collector]: WasmAppBuilder PInvokeCollector.cs (release/10.0)
[^pinvoke-table-gen]: WasmAppBuilder PInvokeTableGenerator.cs (release/10.0)
[^interp-to-native-gen]: WasmAppBuilder InterpToNativeGenerator.cs (release/10.0)
[^signature-mapper]: WasmAppBuilder SignatureMapper.cs (release/10.0)
[^managed-to-native-gen]: WasmAppBuilder ManagedToNativeGenerator.cs (release/10.0)
[^wasmapp-common-targets]: WasmApp.Common.targets (Microsoft.NET.Runtime.WebAssembly.Sdk 10.0.13)
[^browserwasmapp-targets]: BrowserWasmApp.targets (Microsoft.NET.Runtime.WebAssembly.Sdk 10.0.13)
[^mono-toolchain-targets]: microsoft.net.workload.mono.toolchain.current 10.0.101 WorkloadManifest.targets
[^sig-classification]: Prebuilt trampoline inventory and Brutal.Vulkan classification (scratchpad r1)
[^classify-script]: classify.py (scratchpad r1)
[^probe-build]: WasmProbe throwaway project builds (SDK 10.0.100, no wasm-tools)
[^issue-106529]: dotnet/runtime#106529
[^issue-101434]: dotnet/runtime#101434
[^issue-104391]: dotnet/runtime#104391
[^issue-130739]: dotnet/runtime#130739
[^issue-56145]: dotnet/runtime#56145
[^ms-native-deps]: Blazor WebAssembly native dependencies (Microsoft Learn, .NET 10)
[^uno-webgpu-targets]: Uno.WinUI.Composition.WebGpu.targets
[^brutal-import-resolver]: Brutal.Vulkan/ImportResolver.cs
[^brutal-functions-gen]: Brutal.Vulkan/Generated/Functions.gen.cs
[^brutal-binding-doc]: /brutal/vulkan-binding-architecture.md
