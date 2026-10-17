---
type: Experiment Report
title: "exp-01: BRUTAL on .NET 10 browser-wasm without wasm-tools (smoke test, function pointers, signature coverage, interop cost)"
description: Measured, in headless Chromium, what of the BRUTAL NuGet packages works on stock .NET 10 browser-wasm (Mono interpreter, no relink/AOT). Covers the exact Brutal.Platform and Brutal.Vulkan loader failures, NativeLibrary/DllImport behavior, whether a C# "managed ICD" can be reached through BRUTAL's delegate* unmanaged tables, which of BRUTAL's 45 wasm32 Vulkan call signatures are callable, JSImport heap access, and per-call costs.
tags: [experiment, dotnet-wasm, mono, interpreter, brutal, brutal-vulkan, function-pointers, unmanagedcallersonly, icd, jsimport, jsexport, nativelibrary, performance, wasm-tools, headless, puppeteer]
sources:
  - id: project
    resource: ../../../projects/exp-01-dotnet-wasm-smoke/README.md
    title: exp-01 project README (layout, build/run/test commands)
  - id: results
    resource: ../../../projects/exp-01-dotnet-wasm-smoke/RESULTS.md
    title: exp-01 RESULTS (results table, probe table, perf table, verbatim build logs, sizes, raw console output)
  - id: program
    resource: ../../../projects/exp-01-dotnet-wasm-smoke/WasmSmoke/Program.cs
    title: Test harness and tests T01–T13 (C#)
  - id: mainjs
    resource: ../../../projects/exp-01-dotnet-wasm-smoke/WasmSmoke/wwwroot/main.js
    title: Host script (setModuleImports, Module.addFunction trampolines, runMain, rAF tick loop, T13e)
  - id: sigprobes
    resource: ../../../projects/exp-01-dotnet-wasm-smoke/tools/gen_sig_probes.py
    title: Generator for WasmSmoke/SigProbes.g.cs (one probe per distinct wasm32 signature of Brutal.Vulkan fnptr structs)
  - id: log-verbose
    resource: ../../../projects/exp-01-dotnet-wasm-smoke/test/console.release-verbose.log
    title: Console log, Release publish with UseSystemResourceKeys=false (full exception messages), Chrome for Testing 151
  - id: log-default
    resource: ../../../projects/exp-01-dotnet-wasm-smoke/test/console.log
    title: Console log, default Release publish (resource-key messages), Chrome for Testing 151
  - id: log-sigprobes
    resource: ../../../projects/exp-01-dotnet-wasm-smoke/test/console.sigprobes.log
    title: Console log of P01–P45 signature probes (one page load each)
  - id: log-extraprobes
    resource: ../../../projects/exp-01-dotnet-wasm-smoke/test/console.extraprobes.log
    title: Console log of P46–P49 (raw ulong, 64-bit enum, ByteSize64 value, float)
  - id: log-perf
    resource: ../../../projects/exp-01-dotnet-wasm-smoke/test/console.perf.log
    title: Console log of three isolated perf-only runs
  - id: log-edge
    resource: ../../../projects/exp-01-dotnet-wasm-smoke/test/console.edge.log
    title: Console log, Microsoft Edge 155 cross-check
  - id: aot-runtime-wasm-c
    resource: https://github.com/dotnet/dotnet/blob/fad253f51b461736dfd3cd9c15977bb7493becef/src/runtime/src/mono/mono/mini/aot-runtime-wasm.c#L186-L188
    title: aot-runtime-wasm.c at the exact runtime commit (runtimeBuildInfo.gitHash); line 188 is g_error("CANNOT HANDLE INTERP ICALL SIG %s\n", cookie)
  - id: pinvoke-c
    resource: /Users/asherwin/.nuget/packages/microsoft.netcore.app.runtime.mono.browser-wasm/10.0.1/runtimes/browser-wasm/native/src/pinvoke.c
    title: Runtime pack 10.0.1 src/pinvoke.c (wasm_dl_lookup_pinvoke_table, wasm_dl_get_native_to_interp)
  - id: dotnet-d-ts
    resource: /Users/asherwin/.nuget/packages/microsoft.netcore.app.runtime.mono.browser-wasm/10.0.1/runtimes/browser-wasm/native/dotnet.d.ts
    title: dotnet.d.ts, runtime pack 10.0.1 (RuntimeAPI, localHeapView*, getHeap*/setHeap*, IMemoryView, runMain vs runMainAndExit)
  - id: wasm-props
    resource: /Users/asherwin/.nuget/packages/microsoft.netcore.app.runtime.mono.browser-wasm/10.0.1/runtimes/browser-wasm/native/src/wasm-props.json
    title: Runtime pack wasm-props.json (emcc 3.1.56, PropertiesThatTriggerRelinking, exported runtime methods incl. addFunction)
  - id: mono-toolchain-targets
    resource: /usr/local/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.mono.toolchain.current/10.0.101/WorkloadManifest.targets
    title: Mono toolchain workload manifest targets 10.0.101 (_WasmNativeWorkloadNeeded, WasmNativeWorkloadAvailable)
  - id: wasm-browser-targets
    resource: /Users/asherwin/.nuget/packages/microsoft.net.sdk.webassembly.pack/10.0.1/build/Microsoft.NET.Sdk.WebAssembly.Browser.targets
    title: Microsoft.NET.Sdk.WebAssembly.Browser.targets 10.0.1 (SuppressTrimAnalysisWarnings default, line 47)
  - id: brutal-import-resolver
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/ImportResolver.cs
    title: Brutal.Vulkan ImportResolver.cs (static Vulkan() loader)
  - id: brutal-platform
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Core/Platform.cs
    title: Brutal.Core Platform.cs (static Platform() throws on unknown OS)
  - id: brutal-functions-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Functions.gen.cs
    title: Brutal.Vulkan Functions.gen.cs (814 delegate* unmanaged[Cdecl] wrapper structs)
  - id: brutal-vkinstance-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Handles/VkInstance.gen.cs
    title: VkInstance.FunctionTable(VkInstance, VkGetInstanceProcAddr) constructor
  - id: brutal-pinvoke-gen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/PInvoke/VulkanApi/VulkanApi.gen.cs
    title: Brutal.Vulkan global commands as [LibraryImport("vulkan")] (vkCreateInstance, vkEnumerateInstance*)
  - id: brutal-instance-cs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Manual/Classes/Instance.cs
    title: Brutal.Vulkan Instance class (uses Vulkan.CreateInstanceInternal and Vulkan.GetInstanceProcAddr)
  - id: brutal-vk-cs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Manual/Constants/VK.cs
    title: VK.WHOLE_SIZE = ByteSize.Of(unchecked((int)(~0UL)))
  - id: brutal-bytesize
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Core/Types/ByteSize.cs
    title: Brutal.Core ByteSize (nuint-backed) / ByteSize64 (ulong-backed)
  - id: fnptr-note
    resource: /dotnet-wasm/function-pointers-and-native-callbacks.md
    title: Sibling research note with source-level predictions for function pointers on Mono browser-wasm
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:13:55Z }
status: draft
---

# Purpose

Answer concrete feasibility questions with running code, before committing to an architecture that runs BRUTAL in the browser:

1. Do the open-source BRUTAL NuGet packages restore, build, trim and load on .NET 10 `browser-wasm`?
2. What exactly happens to BRUTAL's platform-dependent static constructors (`Brutal.Platform`, `Brutal.VulkanApi.Vulkan`)?
3. Can BRUTAL's `delegate* unmanaged[Cdecl]` Vulkan function tables be pointed at a C# "managed ICD" (Vulkan implemented in C# on top of WebGPU)?
   This must work without the `wasm-tools` workload, in the Mono interpreter.
4. Which alternatives work if not, and what does each boundary crossing cost?
5. What blocks AOT and native relink on this machine?

Related analysis: [/dotnet-wasm/function-pointers-and-native-callbacks.md](/dotnet-wasm/function-pointers-and-native-callbacks.md) (source-level predictions this experiment tests);
[/brutal/vulkan-loader-and-proc-addr-contract.md](/brutal/vulkan-loader-and-proc-addr-contract.md); [/brutal/vulkan-binding-architecture.md](/brutal/vulkan-binding-architecture.md);
[/dotnet-wasm/project-types-and-build-output.md](/dotnet-wasm/project-types-and-build-output.md); [/dotnet-wasm/js-interop-jsimport-jsexport.md](/dotnet-wasm/js-interop-jsimport-jsexport.md);
[/dotnet-wasm/performance-and-aot.md](/dotnet-wasm/performance-and-aot.md); sibling experiment [/experiments/exp-03-webgpu-jsimport.md](/experiments/exp-03-webgpu-jsimport.md).

# Setup

- macOS 27.0.1 arm64, .NET SDK 10.0.100 (workload set 10.0.101; installed workloads `android`, `wasm-tools-net8`; **no net10 `wasm-tools`**; nothing was installed).
- Runtime pack `Microsoft.NETCore.App.Runtime.Mono.browser-wasm` 10.0.1, built with emcc 3.1.56.[^wasm-props]
  `runtimeBuildInfo` reports `{"productVersion":"10.0.1","gitHash":"fad253f5…","wasmEnableThreads":false,"wasmEnableSIMD":true,"wasmEnableExceptionHandling":true}`.[^log-verbose]
- `dotnet new install Microsoft.NET.Runtime.WebAssembly.Templates.net10::10.0.13`, then `dotnet new wasmbrowser -n WasmSmoke`.
  Added `PackageReference`s to Ahwoo.BRUTAL.{Core, Numerics, Maths, Memory, Collections, Strings} 2026.10.1 and {Logging, Vulkan, Vulkan.Abstractions} 2026.10.2.[^project]
- Tests: `Program.cs` (T01–T13), 45 generated signature probes (P01–P45) and 4 follow-ups (P46–P49).[^program][^sigprobes]
  Builds: `dotnet publish -c Release` (default; trimmed; exception messages become resource keys), and the same with
  `-p:UseSystemResourceKeys=false -p:IntermediateOutputPath=obj/ReleaseVerbose/` (full messages).
- Driver: Node 24.18.0 + `puppeteer-core` 25.12.0 (project-local) against **Chrome for Testing 151.0.7922.34** with `--headless=new`.
  Google Chrome is not installed, so Playwright's cached build was used. Cross-checked on Microsoft Edge 155.0.4283.45.
  A wasm trap kills the .NET runtime, so the driver attributes each crash to the last `BEGIN <id>` line, re-runs the page with the crashed ids skipped, and finally runs each crashed id alone.[^project]

# Results table

PASS = ran and correct; FAIL = managed exception (caught); **CRASH** = fatal wasm trap or runtime abort (the page's runtime is dead).
Exception texts are from the full-message build[^log-verbose]; the default build prints resource keys, e.g. `TypeInitialization_Type, Brutal.Platform`.[^log-default]

| id | test | result | observation |
|---|---|---|---|
| T01 | env | PASS | wasm32: `IntPtr.Size=4`, `Is64BitProcess=False`, `ProcessorCount=1`, `Vector128.IsHardwareAccelerated=True`, `PackedSimd.IsSupported=True`, `Stopwatch.Frequency=1000000000`, `.NET 10.0.1`, `IsDynamicCodeCompiled=False`, `GC.TotalAvailableMemoryBytes=2147483648` |
| T02 | `Brutal.Platform.CurrentFamily` | FAIL (expected) | `System.TypeInitializationException: The type initializer for 'Brutal.Platform' threw an exception.` → inner `System.PlatformNotSupportedException: Unsupported platform` |
| T03 | Brutal.Numerics / Maths | PASS | `float4x4`/`float3` transform equals System.Numerics (`<0.99999994, 2, 2>`) |
| T03b–d | Collections, Logging | PASS / FAIL / PASS | `NativeList` and `LogCategory` work. `LogSystem.Initialize(b => b.AddConsole())` → `System.PlatformNotSupportedException: Operation is not supported on this platform.` A custom `ILoggerProvider` works |
| T04/T04b | Brutal.Memory | PASS | `Mem.AllocZeroed<T>`, `Mem.Alloc/Free`, `Mem.AllocAndCopy`, `NativeMemory.*`, `AllocHGlobal` ok; 256 MB alloc + touch in 4–7 ms |
| T05 | `Brutal.VulkanApi.Vulkan.GetInstanceProcAddr` | FAIL (expected) | `System.TypeInitializationException: The type initializer for 'Brutal.VulkanApi.Vulkan' threw an exception.` → inner `System.NullReferenceException: Failed to load Vulkan library`; permanent (2nd access and `Vulkan.Log` rethrow) |
| T05b | Vulkan types without the static class | PASS | handles/structs/enums/`VK` constants usable; `Unsafe.SizeOf<VkBufferCreateInfo>=56`, offsets 8/48 kept; `sizeof(VkBuffer)=4`; `VK.WHOLE_SIZE` as `ByteSize64` = `0xFFFFFFFF` |
| T06 | NativeLibrary survey | PASS | `TryLoad("vulkan")`/`("libvulkan.so.1")`/`("__Internal")` = False; `TryLoad("libSystem.Native")` = True (h=0x93B68); `Load("vulkan", asm, LegacyBehavior)` → `System.DllNotFoundException: vulkan`; `GetMainProgramHandle()` → `System.ComponentModel.Win32Exception: No error information` |
| T06b | `[DllImport("vulkan")] vkCreateInstance` | FAIL | `System.DllNotFoundException: vulkan` |
| T06c | BRUTAL `Vulkan.PInvoke.vkEnumerateInstanceVersion` | FAIL | `System.DllNotFoundException: vulkan`; a resolver set on Brutal.Vulkan is invoked (`"vulkan"`), and mapping it to the libSystem.Native table gives `System.EntryPointNotFoundException: vkEnumerateInstanceVersion` |
| T06d | `[DllImport("libSystem.Native")] SystemNative_GetPid` | FAIL | `System.EntryPointNotFoundException: SystemNative_GetPid` |
| T07a | `&UcoMethod` value | PASS | **`&AddUco=0x0 &MyGetInstanceProcAddr=0x0 &Mixed=0x0`** (no exception) |
| T07 | fnptr → C# `[UnmanagedCallersOnly]` | **CRASH** | `RuntimeError: null function` |
| T08 | Vulkan-shaped managed ICD (C# UCO gipa in BRUTAL's `VkGetInstanceProcAddr`) | **CRASH** | `INFO T08 calling gipa.Invoke=0x0 ...` → `RuntimeError: null function` |
| T08b | BRUTAL `VkInstance.FunctionTable` from C# UCO gipa | **CRASH** | `RuntimeError: null function` |
| T08c | fnptr → JS (`Module.addFunction`) | PASS | C# `delegate* unmanaged[Cdecl]<int,int,int>` on table index 10611 returns 302 |
| T08d | BRUTAL `FunctionTable` from a **JS** gipa | PASS | 105 gipa lookups into JS; `table.vkEnumeratePhysicalDevices` (JS) → `Success/Success count=1` |
| T08e | managed ICD via JS hop (C# → JS trampoline → C# `[JSExport]`) | PASS | 105 re-entrant lookups; `vkEnumeratePhysicalDevices` → `Success`, handle round-trips (`pd=0xBEEF`) |
| T09/T09b | `(nint,int,long,float,double,int*)->long` to C# / to JS | **CRASH** | `[MONO] /__w/1/s/src/runtime/src/mono/mono/mini/aot-runtime-wasm.c:188 <disabled>` then `ExitStatus`; line 188 = `g_error ("CANNOT HANDLE INTERP ICALL SIG %s\n", cookie)`[^aot-runtime-wasm-c] |
| T10 | `Marshal.GetFunctionPointerForDelegate` | **CRASH** | `RuntimeError: memory access out of bounds` |
| T11a–e | JSImport | PASS | `navigator.gpu` present; `Span<byte>` as `JSType.MemoryView`; raw `nint` + `runtime.localHeapViewU8()`; `Task<string>` from a Promise; headless WebGPU adapter `vendor='apple' architecture='metal-3'` |
| T12 | `[JSExport] Tick` from rAF after `Main` returned | PASS | `runMain()` resolved (exit 0); runtime alive; 10 frames at ~16.7 ms |
| T13 | perf | PASS (T13b CRASH) | see Key findings |
| T14 | AOT / relink | FAIL (build) | `error NETSDK1147: To build this project, the following workloads must be installed: wasm-tools` |
| P01–P45 | 45 distinct wasm32 signatures of 814 BRUTAL fnptr structs, called via the real BRUTAL structs | 17 PASS, 27 ABORT, 1 TRAP | 17 signatures = 757/814 structs callable. See the table in RESULTS.md[^results] |
| P47 | `VkPipelineStageFlags2 : ulong` argument | PASS, **wrong value** | callee typed `(i32,i32,i32,i32)` received `0x2` for `0x100000002` |
| P48 | `ByteSize64` argument | PASS | callee received `0x123456789n` |

# Key findings

- **The C#-only "managed ICD" does not work on the stock runtime.** Taking `&M` of any `[UnmanagedCallersOnly]` method returns `0x0`, silently.
  Calling it is a fatal `RuntimeError: null function` that kills the runtime: T07, T08 and T08b all crash, and so does BRUTAL's own
  `VkGetInstanceProcAddr` wrapping a C# gipa.[^log-verbose] The sibling note predicted a `PlatformNotSupportedException` ("No native to managed
  transition…") at `&M`. That was **not** observed: no exception, just a null pointer.[^fnptr-note] The prebuilt runtime's native→interp table cannot contain app methods:
  `wasm_dl_get_native_to_interp` only searches the generated table.[^pinvoke-c]
- **BRUTAL's function-pointer machinery itself works on wasm.** It can be fed from JS with no workload.
  `Module.addFunction` gives real wasm-table function pointers that C# can call. BRUTAL's generated `new VkInstance.FunctionTable(instance, gipa)`
  filled all 105 instance-level slots from a JS `vkGetInstanceProcAddr` (T08d).[^brutal-vkinstance-gen] A JS trampoline that forwards into a C# `[JSExport]`
  gives a working **managed ICD reached through a JS hop**. It is re-entrant (C# → JS → C# inside one call) and handles round-trip (T08e).[^mainjs]
- **Signature coverage is the hard limit, whatever the target (C# or JS).** Interpreted `calli unmanaged` needs an interp→native path for the exact
  signature. Only **17 of BRUTAL's 45 distinct wasm32 signatures** have one in the stock runtime. They cover 757 of 814 fnptr structs.[^brutal-functions-gen]
  The other 28 signatures (57 structs) abort with `CANNOT HANDLE INTERP ICALL SIG`.[^log-sigprobes][^aot-runtime-wasm-c] The aborting set includes commands
  a renderer needs:
  - `vkCmdBindDescriptorSets` (`viiiiiiii`)
  - `vkCmdPipelineBarrier` (`viiiiiiiiii`)
  - `vkMapMemory` (`iiijjii`)
  - `vkCmdBindIndexBuffer` (`viiji`)
  - `vkCmdSetLineWidth`/`vkCmdSetDepthBias` (float args)
  - `vkCmdDrawIndirect*`
  - `vkCmdUpdateBuffer`/`vkCmdFillBuffer`
  - `vkGetBufferDeviceAddress`

  Every pass/abort matches the sibling note's model: fast path for ≤6 pointer-like params, else one of 52 prebuilt cookies.[^fnptr-note]
  The newer struct-pointer variants land on callable signatures (`vkCmdBindDescriptorSets2`/`vkCmdPipelineBarrier2`/`vkCmdPushConstants2`/`vkCmdCopyBuffer2` = `vii`,
  `vkMapMemory2` = `iiii`, `vkUnmapMemory2` = `iii`). `vkCmdBindIndexBuffer2` (`viijji`) still aborts. These "2"-variant signatures come from `tools/vk_sigs.py`;
  only their signature classes were probed.
- **64-bit enums are silently truncated.** A `ulong`-backed BRUTAL enum (`VkPipelineStageFlags2`) passed through BRUTAL's fnptr reached the callee as an i32:
  `0x100000002` became `0x2` (P47). With an i64-typed callee the result is `RuntimeError: function signature mismatch` (P21).[^log-extraprobes] This confirms the sibling note's UNVERIFIED source-reading prediction that the interpreter fast path treats every enum as pointer-sized.[^fnptr-note]
  i64 values carried by scalar structs work (`ByteSize64` → `0x123456789n`, P48), but only on signatures that exist (`iiiij` does; `vijii` with a raw `ulong` aborts, P46).
- **Exact BRUTAL static-constructor failures.**
  - `Platform`: `TypeInitializationException` → `PlatformNotSupportedException: Unsupported platform`.[^brutal-platform] Within BRUTAL's sources it is only
    consumed by Brutal.ImGui (`VaList`) and Brutal.OpenAl (`NativeLoader`), not by the 9 tested packages.
  - `Vulkan`: `TypeInitializationException` → `NullReferenceException: Failed to load Vulkan library`. It is sticky, so `Vulkan.Log`, `Vulkan.CreateInstance` and
    therefore BRUTAL's `Instance` class are unusable.[^brutal-import-resolver][^brutal-instance-cs] All other Brutal.Vulkan types (handles, structs, enums, fnptr structs,
    `FunctionTable`s, `VK` constants) work as long as code never touches `Vulkan`.
- **Native loading.** There is no dynamic loading, and BRUTAL's 4 global commands (`[LibraryImport("vulkan")]`: `vkCreateInstance`,
  `vkEnumerateInstanceExtensionProperties`/`LayerProperties`/`Version`) fail with `DllNotFoundException: vulkan`.[^brutal-pinvoke-gen]
  `NativeLibrary.SetDllImportResolver(Brutal.Vulkan, …)` *can* be registered, because the failed static ctor never registered its own. The resolver is invoked,
  but it can only hand back statically linked pinvoke tables (`TryLoad("libSystem.Native")` = 0x93B68), so the result is `EntryPointNotFoundException: vkEnumerateInstanceVersion`.
  `GetMainProgramHandle()` throws `Win32Exception: No error information`.
- **ABI/layout.** BRUTAL's explicit layouts keep their x64 offsets on wasm32 (`VkBufferCreateInfo` = 56 B, `QueueFamilyIndices` @48), but pointers/handles are 4 bytes.
  Non-dispatchable handles and `uint64_t timeout` are `nint` (32-bit here). `VK.WHOLE_SIZE` is `ByteSize`(nuint) = **`0xFFFFFFFF`** instead of `~0ULL`.[^brutal-vk-cs][^brutal-bytesize]
  BRUTAL structs are therefore **not** wasm32-C-ABI compatible. A managed or JS ICD that reads BRUTAL's own layouts is fine, but a C Vulkan implementation
  compiled to wasm32 could not consume them directly.
- **JS interop facts.**
  - `runtime.localHeapViewU8()` (and `getHeapU8`/`setHeapI32`, …) on the object returned by `dotnet.create()` reads wasm memory from a raw `nint`.
    `Module.HEAPU8`, `Module.wasmExports.memory`, `Module.addFunction` and `Module.UTF8ToString` are also present.[^dotnet-d-ts]
  - `[JSMarshalAs<JSType.MemoryView>] Span<byte>` works, as does `Task<string>` from a Promise.
  - `navigator.gpu` and a Metal-backed adapter are available in headless Chrome 151.
  - `runMain()` keeps the runtime alive after `Main` returns: a rAF loop calling `[JSExport]` works. The template uses `create()` + `runMain()`, not `dotnet.run()`, and never calls `exit`.
- **Per-call cost** (interpreter, ns/call, warmed full-suite run on Chrome; isolated cold runs are 1.4–2.5× higher for the cheap rows; Edge similar):[^results][^log-perf][^log-edge]

  | call | ns/call |
  |---|---:|
  | direct static call (T13a) | 9.2 |
  | managed `delegate*<int,int,int>` (T13f) | 12.1 |
  | `delegate* unmanaged` → JS `addFunction` fn (T13b2) | 26.4 |
  | `[JSImport]` int→int (T13c) | 103.3 |
  | JS → C# `[JSExport]` int→int (T13e) | 134.7 |
  | `[JSImport]` 64-byte `Span<byte>` MemoryView (T13d / with JS `.slice()` copy T13d2) | 144.8 / 220 |
  | `delegate* unmanaged` → JS trampoline → C# `[JSExport]` (T13b3) | 174.8 |
  | `delegate* unmanaged` → C# UCO (T13b) | n/a (crashes) |

- **Build facts.**
  - The interpreter build and publish need no workload. Publish only prints the message "Publishing without optimizations… we strongly recommend using `wasm-tools` workload!".
  - `RunAOTCompilation=true` and `WasmBuildNative=true` fail with **`error NETSDK1147: To build this project, the following workloads must be installed: wasm-tools`**.
  - A `NativeFileReference` is silently not linked (warning: "the native references won't be linked in, because neither $(WasmBuildNative), nor $(RunAOTCompilation) are 'true'").[^mono-toolchain-targets]
  - `InvariantGlobalization=true` works without the workload.
  - Restore: 0 warnings (the prerelease `Microsoft.Extensions.ObjectPool 11.0.0-preview.5` needed no opt-in).
  - Trimming with analysis warnings enabled: 0 warnings.[^wasm-browser-targets]
  - Sizes: trimmed Release publish 8.43 MB raw / 3.18 MB gz / 2.44 MB br (43 files). `dotnet.native.wasm` = 3.0 MB (0.98 MB br); trimmed `Brutal.Vulkan` = 216 KB (from 1.56 MB). Untrimmed Debug: 30.4 MB.
  - .NET 10 emits no `AppBundle/`, no `blazor.boot.json` and no `dotnet.boot.js`: the boot JSON is inlined in `dotnet.<hash>.js` between `/*json-start*/` and `/*json-end*/`. Assemblies are Webcil `.wasm`.[^results]

# Implications for the architecture

1. **A pure-C# managed ICD behind BRUTAL's unmodified `delegate* unmanaged` tables is not viable on the stock runtime.**
   The UCO pointers are null, and 28 signatures abort regardless of target. Choose one of:
   - **(a) No workload, JS-hosted entry points.** Fill BRUTAL's `FunctionTable`s from `Module.addFunction` functions.
     Either implement the commands in JS directly on WebGPU (a "JS ICD", ~26 ns per C#→JS call), or forward to C# via `[JSExport]`
     (a "managed ICD through a JS hop", ~175 ns per call). Restrict the renderer to commands whose signatures are callable,
     favouring the Vulkan 1.3/1.4 struct-pointer "2" variants. The remaining gaps need emulation or avoidance, notably `vkCmdBindIndexBuffer`, float-arg dynamic state, indirect draws and 64-bit flag enums.
   - **(b) Install `wasm-tools` and relink.** According to source reading in the sibling note, this generates native→interp thunks for app UCO methods,
     and missing interp→native cookies can be anchored with dummy `[UnmanagedFunctionPointer]` delegates or `[DllImport]`s.[^fnptr-note]
     This is **unverified here**, and the P47 64-bit-enum truncation is on the fast path, which a relink probably does not change.
   - **(c) Source-build or fork Brutal.Vulkan for the browser** with managed `delegate*<…>` tables. This was the fastest measured (12 ns), needs no trampolines or workload, and has no signature limits.
     It diverges from "use the NuGet packages as-is".
2. **BRUTAL's `Vulkan` static class and `Instance` class cannot be used in the browser as shipped.** A browser path must either construct
   `VkInstance.FunctionTable`/`VkDevice.FunctionTable` directly with its own gipa (shown to work), or get an upstream change: an injectable loader,
   e.g. a browser branch in `static Vulkan()` or a `Vulkan.Initialize(gipa)` hook plus non-P/Invoke global commands. The 4 global commands can never resolve without relink plus a native shim.
3. Pure-managed BRUTAL (Core types, Numerics/Maths, Memory, Collections, Strings, Logging with a custom provider) works unchanged. Avoid `Brutal.Platform` and the MEL console provider.
4. Any ICD must interpret BRUTAL's wasm32 values: 32-bit handles, 32-bit `timeout`, and `VK.WHOLE_SIZE == 0xFFFFFFFF`. A C Vulkan-on-WebGPU library compiled for wasm32 would need a translation shim for BRUTAL's x64-offset structs.
5. Per-frame interop budget: ~100–200 ns per JS crossing in the interpreter. Batching, as exp-03 does with a command stream, matters more than the choice between JSImport and fnptr.

# Open questions

- With `wasm-tools` installed: does `&UcoMethod` become non-null, does T08/T08b pass, and do signature anchors make all 45 BRUTAL signatures callable? What do AOT and the T13 numbers look like?
- Is the 64-bit-enum truncation (P47) a Mono fast-path bug that persists under relink and AOT? Should it be reported upstream?
- Can BRUTAL upstream accept a pluggable loader (browser branch in `static Vulkan()`, managed global commands), so the `Instance`/`Device` classes work in the browser?
- Why are isolated cold runs 1.4–2.5× slower than warmed runs? Is it V8 tier-up of `dotnet.native.wasm` or jiterpreter thresholds? How long is warm-up in a real frame loop?
- Does T08e's re-entrancy hold under deep or recursive call patterns (e.g. a callback into C# that calls more Vulkan commands), and under `[JSExport]` exceptions?

[^project]: exp-01 project README
[^results]: exp-01 RESULTS
[^program]: Test harness and tests T01–T13
[^mainjs]: Host script main.js
[^sigprobes]: Signature probe generator
[^log-verbose]: Console log, full-message Release build
[^log-default]: Console log, default Release build
[^log-sigprobes]: Signature probe console log
[^log-extraprobes]: P46–P49 console log
[^log-perf]: Isolated perf runs
[^log-edge]: Edge 155 cross-check
[^aot-runtime-wasm-c]: aot-runtime-wasm.c line 188 at runtime commit fad253f5
[^pinvoke-c]: Runtime pack src/pinvoke.c
[^dotnet-d-ts]: dotnet.d.ts 10.0.1
[^wasm-props]: Runtime pack wasm-props.json
[^mono-toolchain-targets]: Mono toolchain WorkloadManifest.targets 10.0.101
[^wasm-browser-targets]: Microsoft.NET.Sdk.WebAssembly.Browser.targets 10.0.1
[^brutal-import-resolver]: Brutal.Vulkan ImportResolver.cs
[^brutal-platform]: Brutal.Core Platform.cs
[^brutal-functions-gen]: Brutal.Vulkan Functions.gen.cs (814 fnptr structs)
[^brutal-vkinstance-gen]: VkInstance.FunctionTable constructor
[^brutal-pinvoke-gen]: Brutal.Vulkan [LibraryImport("vulkan")] global commands
[^brutal-instance-cs]: Brutal.Vulkan Instance class
[^brutal-vk-cs]: VK.WHOLE_SIZE definition
[^brutal-bytesize]: Brutal.Core ByteSize / ByteSize64
[^fnptr-note]: Function pointers, UnmanagedCallersOnly and native resolution on .NET 10 browser-wasm (Mono)
