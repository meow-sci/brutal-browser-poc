# exp-01 RESULTS — .NET 10 browser-wasm smoke test for BRUTAL

Run on 2026-10-16. Every number and message below is copied from a captured log under `test/` or from a
build log; nothing is paraphrased inside code blocks. The raw logs are:

| log | content |
|---|---|
| `test/console.log` | default `dotnet publish -c Release` build (trimmed, so exception messages are **resource keys**): all passes + isolation runs |
| `test/console.release-verbose.log` | same code published with `-p:UseSystemResourceKeys=false` (**full exception messages**): all passes + isolation runs |
| `test/console.sigprobes.log` | P01..P45, one page load per signature probe |
| `test/console.extraprobes.log` | P46..P49 follow-up probes |
| `test/console.perf.log` | three isolated perf-only runs (`?tests=T13a,T13f,T13b2,T13b3,T13c,T13d,T13d2`) |
| `test/console.edge.log` | Microsoft Edge 155 cross-check (verbose build, crashing tests skipped) |

## Environment

| item | value |
|---|---|
| OS | macOS 27.0.1 (26A434), arm64 |
| .NET SDK | 10.0.100 (`/usr/local/share/dotnet`); workload set 10.0.101; installed workloads: `android`, `wasm-tools-net8`. The net10 `wasm-tools` workload is **not** installed |
| Runtime pack (NuGet) | `Microsoft.NETCore.App.Runtime.Mono.browser-wasm` **10.0.1**. `runtimeBuildInfo` = `{"productVersion":"10.0.1","gitHash":"fad253f51b461736dfd3cd9c15977bb7493becef","buildConfiguration":"Release","wasmEnableThreads":false,"wasmEnableSIMD":true,"wasmEnableExceptionHandling":true}`. The runtime pack's `wasm-props.json` says it was built with emcc 3.1.56 |
| WebAssembly SDK pack | `Microsoft.NET.Sdk.WebAssembly.Pack` 10.0.1 (auto-referenced) |
| Template | `Microsoft.NET.Runtime.WebAssembly.Templates.net10` 10.0.13 (`dotnet new wasmbrowser`) |
| BRUTAL | Core/Numerics/Maths/Memory/Collections/Strings 2026.10.1; Logging/Vulkan/Vulkan.Abstractions 2026.10.2 |
| Browser (primary) | **Chrome for Testing 151.0.7922.34** (Playwright cache: `~/Library/Caches/ms-playwright/chromium-1234/chrome-mac-arm64/Google Chrome for Testing.app`), `--headless=new`. Google Chrome itself is not installed on this machine |
| Browser (cross-check) | Microsoft Edge 155.0.4283.45 (`/Applications/Microsoft Edge.app`), `--headless=new` |
| Driver | Node 24.18.0, npm 12.0.2, `puppeteer-core` 25.12.0 (project-local; no browser download) |

## Results table

Legend: **PASS**, **FAIL** (managed exception, caught), **CRASH** (fatal WebAssembly trap or runtime abort: the page's .NET runtime is dead afterwards).
Exception texts are from the verbose build. The default trimmed build prints resource keys instead (see "Default-build message variants" below).

| id | test | result | observation |
|---|---|---|---|
| T01 | env | PASS | `IsBrowser=True`, `OSArchitecture=Wasm`, `IntPtr.Size=4`, `Is64BitProcess=False`, `ProcessorCount=1`, `Vector128.IsHardwareAccelerated=True`, `PackedSimd.IsSupported=True`, `Vector<float>.Count=4`, `Stopwatch.Frequency=1000000000`, `FrameworkDescription='.NET 10.0.1'`, `IsDynamicCodeSupported=True`, `IsDynamicCodeCompiled=False`, `GC.TotalAvailableMemoryBytes=2147483648` |
| T02 | brutal-platform | FAIL (expected) | `System.TypeInitializationException: The type initializer for 'Brutal.Platform' threw an exception.` inner `System.PlatformNotSupportedException: Unsupported platform` |
| T03 | brutal-numerics | PASS | `float3.Transform((1,0,0), RotY(pi/2)*T(1,2,3)) = <0.99999994, 2, 2>`, identical to System.Numerics; `float4x4*float4`, `float3.Cross`, `sizeof(float4x4)=64`, Brutal.Maths `Random.NextSinglePointInBox` all work |
| T03b | brutal-collections-logging | PASS | `NativeList` add/size ok; `LogCategory.Make` ok |
| T03c | brutal-logging-console-provider | FAIL | `LogSystem.Initialize(b => b.AddConsole())` → `System.PlatformNotSupportedException: Operation is not supported on this platform.` (MEL console provider is browser-unsupported) |
| T03d | brutal-logging-custom-provider | PASS | `LogSystem.Initialize` with a custom `ILoggerProvider` → `LOG [Information] SmokeCustom: hello from Brutal.Logging via a custom ILoggerProvider (Console.WriteLine)` |
| T04 | brutal-memory | PASS | `Mem.AllocZeroed<byte>(1MB)` (zeroed), `Mem.Alloc(ByteSize)`/`Mem.Free`, `Mem.AllocAndCopy(float4)`, `NativeMemory.Alloc`, `NativeMemory.AlignedAlloc(…,256)` aligned, `Marshal.AllocHGlobal` all ok |
| T04b | memory-growth-256MB | PASS | 256 MB `NativeMemory.Alloc` + touch every 64 KB + free in 4–7 ms |
| T05 | brutal-vulkan-static-ctor | FAIL (expected) | `System.TypeInitializationException: The type initializer for 'Brutal.VulkanApi.Vulkan' threw an exception.` inner `System.NullReferenceException: Failed to load Vulkan library`. A second access and `Vulkan.Log` rethrow the same exception: the class is permanently unusable in that process |
| T05b | brutal-vulkan-types-without-ctor | PASS | `VkInstance`, `VkBufferCreateInfo`, enums and `VK` constants are usable without touching `Vulkan`. `Unsafe.SizeOf<VkBufferCreateInfo>=56`, field offsets 8/48 (the x64 explicit layout is kept on wasm32), `sizeof(VkBuffer)=4`, `sizeof(VkInstance)=4`, `sizeof(PfnVkVoidFunction)=4`, `VkInstanceCreateInfo` 64, `VkPhysicalDeviceProperties` 824. **`VK.WHOLE_SIZE` as `ByteSize64` = `0xFFFFFFFF`** (C value is `~0ULL`) |
| T06 | nativelibrary (survey) | PASS | `TryLoad(vulkan)=False`, `TryLoad(libvulkan.so.1)=False`, `TryLoad(__Internal)=False`, `TryLoad(libSystem.Native)=True h=0x93B68`; `Load(vulkan, asm, LegacyBehavior)` → `System.DllNotFoundException: vulkan`; `GetMainProgramHandle()` → `System.ComponentModel.Win32Exception: No error information`; `TryGetExport(0, …)` → `ArgumentNullException (Parameter 'handle')` |
| T06b | dllimport-vulkan-local | FAIL | `[DllImport("vulkan")] vkCreateInstance` → `System.DllNotFoundException: vulkan` |
| T06c | brutal-libraryimport-vkEnumerateInstanceVersion | FAIL | `Vulkan.PInvoke.vkEnumerateInstanceVersion` (BRUTAL's own `[LibraryImport("vulkan")]`) → `System.DllNotFoundException: vulkan`. Registering a resolver on Brutal.Vulkan succeeded; the resolver was called once with `"vulkan"`, and returning the `libSystem.Native` table handle gave `System.EntryPointNotFoundException: vkEnumerateInstanceVersion` |
| T06d | dllimport-known-native | FAIL | `[DllImport("libSystem.Native", EntryPoint="SystemNative_GetPid")]` → `System.EntryPointNotFoundException: SystemNative_GetPid` (not in the static pinvoke table) |
| T07a | fnptr-ldftn-value | PASS | **`&AddUco=0x0 &MyGetInstanceProcAddr=0x0 &Mixed=0x0`**: taking the address of any `[UnmanagedCallersOnly]` method yields a null pointer, and no exception is thrown |
| T07 | fnptr-basic | **CRASH** | `INFO T07 calling &AddUco=0x0 ...` then `[pageerror] RuntimeError: null function` |
| T08 | fnptr-vulkan-shape (managed ICD via C# UCO) | **CRASH** | `INFO T08 calling gipa.Invoke=0x0 ...` then `RuntimeError: null function` |
| T08b | BRUTAL `VkInstance.FunctionTable` from C# UCO gipa | **CRASH** | `RuntimeError: null function` |
| T08c | fnptr-js-addFunction | PASS | `Module.addFunction((a,b)=>a+b*100,'iii')` → table index 10611; C# `delegate* unmanaged[Cdecl]<int,int,int>` call returns 302 |
| T08d | BRUTAL `FunctionTable` from **JS** gipa | PASS | BRUTAL's generated `new VkInstance.FunctionTable(inst, gipa)` made 105 lookups into a JS `vkGetInstanceProcAddr`; `table.vkEnumeratePhysicalDevices` (JS) → `Success/Success count=1` |
| T08e | managed ICD via JS hop | PASS | C# calli → JS `addFunction` trampoline → C# `[JSExport]`. The FunctionTable constructor made 105 re-entrant C#→JS→C# lookups; `vkEnumeratePhysicalDevices` → `Success/Success count=1 pd=0xBEEF` |
| T09 | fnptr-mixed-args `(nint,int,long,float,double,int*)->long` | **CRASH** | `[MONO] /__w/1/s/src/runtime/src/mono/mono/mini/aot-runtime-wasm.c:188 <disabled>` then `[pageerror] ExitStatus: ExitStatus`. Line 188 is `g_error ("CANNOT HANDLE INTERP ICALL SIG %s\n", cookie);` (message text compiled out) |
| T09b | same signature, JS target | **CRASH** | identical abort: the missing piece is the interp→native trampoline for the signature, not the target |
| T10 | delegate-marshal | **CRASH** | `Marshal.GetFunctionPointerForDelegate` → `[pageerror] RuntimeError: memory access out of bounds` |
| T11a | jsimport-hasWebGpu | PASS | `navigator.gpu present=True` (headless) |
| T11b | jsimport-span-memoryview | PASS | `[JSMarshalAs<JSType.MemoryView>] Span<byte>`: JS `mv.slice()` sum = 32640 (correct) |
| T11c | jsimport-raw-pointer-heap | PASS | `[JSMarshalAs<JSType.Number>] nint ptr, int len`; JS reads `runtime.localHeapViewU8()[ptr+i]` → correct sum |
| T11d | heap API survey | PASS | `runtime.localHeapViewU8/getHeapU8/setHeapI32` = function; `Module.HEAPU8` = object; `Module.wasmExports.memory` = `WebAssembly.Memory`; `Module.addFunction`, `Module.UTF8ToString`, `Module._malloc` = function; heap ≈ 298 MB at that point; not SAB-backed; `crossOriginIsolated=false` |
| T11e | async JSImport + WebGPU adapter | PASS | `Task<string>` from a JS Promise; `requestAdapter()` in headless Chrome → `vendor='apple' architecture='metal-3' features=22 maxBufferSize=4294967292` |
| T12 | jsexport-tick | PASS | `runMain()` resolved with exit code 0 after `Main` returned; then 10 `requestAnimationFrame` frames called `[JSExport] Tick` (~16.7 ms/frame); `TICKS DONE` |
| T13a–f | perf | PASS (T13b CRASH) | see the perf table |
| T14 | AOT / native relink | FAIL (build) | `error NETSDK1147: To build this project, the following workloads must be installed: wasm-tools`, for both `RunAOTCompilation=true` and `WasmBuildNative=true` |
| P01–P45 | BRUTAL Vulkan signature probes | 17 PASS / 27 ABORT / 1 TRAP | see the signature probe table |
| P46 | raw `ulong` param `vijii` | **CRASH** | `CANNOT HANDLE INTERP ICALL SIG` abort |
| P47 | `VkPipelineStageFlags2 : ulong` param | PASS but **wrong value** | JS typed `viiii` was called and received `0x2` for `0x100000002`: **a 64-bit enum is passed as i32 and truncated** |
| P48 | `ByteSize64` param (`VkBindBufferMemory`) | PASS | JS received `0x123456789n`: i64 passed correctly via a scalar struct |
| P49 | `float` param `vif` | **CRASH** | `CANNOT HANDLE INTERP ICALL SIG` abort |

## Signature probes (P01–P45): which BRUTAL Vulkan calls can interpreted C# make?

`tools/vk_sigs.py` lowers every `delegate* unmanaged[Cdecl]<…>` in `Brutal.Vulkan/Generated/Functions.gen.cs` to a
wasm32 signature, with these rules:
- `i` (i32): handles, pointers, `nint`, `int`, int-backed enums, `Bool32`.
- `j` (i64): `long`/`ulong`, `ByteSize64`, `VkDeviceAddress`, `VkFlags64`, ulong-backed enums.
- `f`/`d`: `float`/`double`.

The result is **814 fnptr structs → 45 distinct signatures** (max 15 params, 0 by-value multi-field structs). Notably, BRUTAL types
`uint64_t timeout` parameters as `nint` (e.g. `VkWaitForFences`, `VkAcquireNextImageKHR`) and non-dispatchable handles as `nint`, so on wasm32 they are 32-bit.

For each signature, `tools/gen_sig_probes.py` emitted a probe that:
1. creates a JS function with `Module.addFunction(fn, sig)`;
2. wraps it in the **real BRUTAL struct** of a representative command (`(VkCmdDraw)new PfnVkVoidFunction(...)`);
3. calls `.Invoke(default, …)` from interpreted C#.

Each probe ran in its own page load.

| probe | wasm32 sig (ret first; i=i32 j=i64 f=f32) | # BRUTAL fnptr structs | representative struct used | result |
|---|---|---:|---|---|
| P01 | `vii` | 166 | VkDestroyInstance | PASS |
| P02 | `viii` | 153 | VkGetPhysicalDeviceFormatProperties | PASS |
| P03 | `iiiii` | 119 | VkCreateDevice | PASS |
| P04 | `iiii` | 100 | VkEnumeratePhysicalDevices | PASS |
| P05 | `viiii` | 63 | VkGetDeviceQueue | PASS |
| P06 | `iii` | 62 | VkGetInstanceProcAddr | PASS |
| P07 | `iiiiii` | 18 | VkWaitForFences | PASS |
| P08 | `viiiiii` | 17 | VkCmdCopyBufferToImage | PASS |
| P09 | `viiiii` | 16 | VkCmdCopyBuffer | PASS |
| P10 | `vi` | 12 | VkCmdEndRenderPass | PASS |
| P11 | `viiiiiii` | 9 | VkCmdCopyImage | PASS |
| P12 | `iiiiiii` | 9 | VkCreateComputePipelines | PASS |
| P13 | `viijijii` | 8 | VkCmdDrawIndirectCount | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P14 | `jii` | 6 | VkGetBufferDeviceAddress | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P15 | `iiiiiiii` | 5 | VkGetPhysicalDeviceImageFormatProperties | PASS |
| P16 | `viiiiiiii` | 5 | VkGetPhysicalDeviceSparseImageFormatProperties | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P17 | `viijji` | 4 | VkCmdUpdateBuffer | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P18 | `viij` | 4 | VkCmdDispatchIndirect | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P19 | `viijii` | 4 | VkCmdDrawIndirect | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P20 | `ii` | 3 | VkQueueWaitIdle | PASS |
| P21 | `vijii` | 3 | VkCmdWriteTimestamp2 | TRAP `RuntimeError: function signature mismatch` |
| P22 | `iiiij` | 2 | VkBindBufferMemory | PASS |
| P23 | `vif` | 2 | VkCmdSetLineWidth | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P24 | `viiiji` | 2 | VkCmdUpdateMemoryKHR | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P25 | `iiiiiiiii` | 2 | VkGetPhysicalDeviceExternalImageFormatPropertiesNV | PASS |
| P26 | `iiijjii` | 1 | VkMapMemory | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P27 | `iiiiiiiji` | 1 | VkGetQueryPoolResults | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P28 | `viiiiiiiiii` | 1 | VkCmdPipelineBarrier | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P29 | `viiiiijji` | 1 | VkCmdCopyQueryPoolResults | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P30 | `viiiiiiiiiii` | 1 | VkCmdWaitEvents | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P31 | `vifff` | 1 | VkCmdSetDepthBias | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P32 | `viff` | 1 | VkCmdSetDepthBounds | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P33 | `viiji` | 1 | VkCmdBindIndexBuffer | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P34 | `vij` | 1 | VkCmdTraceRaysIndirect2KHR | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P35 | `viiiijii` | 1 | VkCmdDrawIndirectByteCountEXT | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P36 | `viiijiiiij` | 1 | VkCmdBuildAccelerationStructureNV | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P37 | `viijijjijjijjiii` | 1 | VkCmdTraceRaysNV | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P38 | `vijiji` | 1 | VkCmdWriteBufferMarker2AMD | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P39 | `viif` | 1 | VkSetDeviceMemoryPriorityEXT | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P40 | `vijiiiii` | 1 | VkCmdCopyMemoryToImageIndirectNV | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P41 | `vijji` | 1 | VkCmdDecompressMemoryIndirectCountNV | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P42 | `vijjjii` | 1 | VkCmdDecompressMemoryIndirectCountEXT | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P43 | `viiiiij` | 1 | VkCmdTraceRaysIndirectKHR | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P44 | `jiiii` | 1 | VkGetRayTracingShaderGroupStackSizeKHR | ABORT `[MONO] aot-runtime-wasm.c:188 <disabled>` (= `CANNOT HANDLE INTERP ICALL SIG`) |
| P45 | `v` | 1 | PfnVkVoidFunction | PASS |

**17 of 45 signatures callable, covering 757 of 814 BRUTAL Vulkan fnptr structs; 57 structs (28 signatures) abort or trap.**

Practical impact for a renderer: `vkCmdDraw`/`vkCmdDrawIndexed`/`vkCmdCopyBuffer`/`vkQueueSubmit`/`vkCreate*`/`vkCmdBeginRendering` etc. are callable.
**`vkCmdBindDescriptorSets` (`viiiiiiii`, same signature as P16), `vkCmdPipelineBarrier` (P28), `vkMapMemory` (P26), `vkCmdBindIndexBuffer` (P33), `vkCmdSetLineWidth`/`vkCmdSetDepthBias`, `vkCmdDrawIndirect`, `vkCmdUpdateBuffer`/`vkCmdFillBuffer` and `vkGetBufferDeviceAddress` abort the runtime.**
`vkCmdWriteTimestamp2` (`VkPipelineStageFlags2 : ulong`) traps with `function signature mismatch`. P47 shows the reason: the 64-bit enum is passed as an i32.

Follow-up probes (raw lines from `test/console.extraprobes.log`):

```
[+   318ms] [driver] ===== single: http://127.0.0.1:58442/index.html?tests=P46
[+   451ms] [console.error] [MONO] /__w/1/s/src/runtime/src/mono/mono/mini/aot-runtime-wasm.c:188 <disabled>
[+   451ms] [pageerror] ExitStatus: ExitStatus
[+  2452ms] [driver] single finished: CRASH (attributed to P46)
[+   287ms] [driver] ===== single: http://127.0.0.1:58484/index.html?tests=P47
[+   419ms] [console.log] TEST P47 64-bit enum (VkPipelineStageFlags2 : ulong) vs JS sig viiii: PASS JS (typed viiii) received args: 0x11, 0x2, 0x33, 0x4 (sent 0x11, 0x100000002, 0x33, 4)
[+   715ms] [driver] single finished: TICKS DONE
[+   288ms] [driver] ===== single: http://127.0.0.1:58499/index.html?tests=P48
[+   420ms] [console.log] TEST P48 i64 value correctness via ByteSize64 (VkBindBufferMemory, iiiij): PASS ret=Success; JS received args: 0x11, 0x22, 0x33, 0x123456789n (sent 0x11, 0x22, 0x33, 0x123456789)
[+   723ms] [driver] single finished: TICKS DONE
[+   298ms] [driver] ===== single: http://127.0.0.1:58514/index.html?tests=P49
[+   428ms] [console.error] [MONO] /__w/1/s/src/runtime/src/mono/mono/mini/aot-runtime-wasm.c:188 <disabled>
[+   428ms] [pageerror] ExitStatus: ExitStatus
[+  2429ms] [driver] single finished: CRASH (attributed to P49)
```

## Performance (T13, ns per call, Mono interpreter, no AOT)

1,000,000 iterations each after a 20,000-iteration warm-up. T13d2 used 100,000 iterations. T13e is measured in JS
(JS → C# `[JSExport]`); all others are measured in C# with `Stopwatch`. Multiple values in one cell are separate passes within the same run.

| run | T13a | T13f | T13b2 | T13b3 | T13c | T13d | T13d2 | T13e |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Chrome 151, default publish, full suite (`console.log`, final pass) | 9.2 | 12.1 | 26.4 | 174.8 | 103.3 | 144.8 | 220.0 | 134.7 |
| Chrome 151, verbose publish, full suite (`console.release-verbose.log`, final pass) | 9.0 | 12.1 | 26.5 | 172.7 | 105.3 | 137.6 | 217.0 | 133.4 |
| Chrome 151, default publish, perf-only isolated run 1 (`console.perf.log`) | 22.8 | 30.6 | 45.9 | 191.3 | 154.3 | 205.2 | 279.0 | 135.5 |
| Chrome 151, default publish, perf-only isolated run 2 (`console.perf.log`) | 22.1 | 29.1 | 43.7 | 192.5 | 155.4 | 202.3 | 279.0 | 138.5 |
| Chrome 151, default publish, perf-only isolated run 3 (`console.perf.log`) | 21.8 | 30.3 | 44.7 | 195.9 | 154.0 | 203.1 | 272.0 | 135.2 |
| Edge 155, verbose publish, full suite minus crashers (`console.edge.log`) | 8.5 | 11.4 | 25.6 | 241.9 | 128.0 | 164.6 | 455.0 | 202.6 |

Columns:
- **T13a**: direct static call.
- **T13f**: managed `delegate*<int,int,int>`.
- **T13b2**: `delegate* unmanaged[Cdecl]` → JS `addFunction` function.
- **T13b3**: `delegate* unmanaged[Cdecl]` → JS trampoline → C# `[JSExport]`.
- **T13c**: `[JSImport]` int→int.
- **T13d**: `[JSImport]` with a 64-byte `Span<byte>` (MemoryView, JS reads `.byteLength`).
- **T13d2**: same, but JS `.slice()` copies and sums.
- **T13e**: JS → C# `[JSExport]` int→int.
- **T13b** (`delegate* unmanaged` → C# UCO) could not be measured: it crashes (`RuntimeError: null function`).

The cheapest calls (T13a/T13f/T13b2/T13c/T13d) are about 1.4–2.5× slower when the perf tests run alone right after start-up than
inside the full suite, which has run about 0.7 s of other code first. A plausible cause is V8 tiering of `dotnet.native.wasm` and/or the Mono
jiterpreter warming up. This was not investigated. Treat the full-suite rows as warmed-up numbers.

## T14: AOT / native relink attempts (no workload installed, nothing was installed)

```
$ dotnet publish -c Release -p:RunAOTCompilation=true -o ../out/aot-attempt
  Determining projects to restore...
/usr/local/share/dotnet/sdk/10.0.100/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.ImportWorkloads.targets(38,5): error NETSDK1147: To build this project, the following workloads must be installed: wasm-tools [/Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-01-dotnet-wasm-smoke/WasmSmoke/WasmSmoke.csproj]
/usr/local/share/dotnet/sdk/10.0.100/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.ImportWorkloads.targets(38,5): error NETSDK1147: To install these workloads, run the following command: dotnet workload restore [/Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-01-dotnet-wasm-smoke/WasmSmoke/WasmSmoke.csproj]
```

```
$ dotnet publish -c Release -p:WasmBuildNative=true -o ../out/native-attempt
  Determining projects to restore...
/usr/local/share/dotnet/sdk/10.0.100/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.ImportWorkloads.targets(38,5): error NETSDK1147: To build this project, the following workloads must be installed: wasm-tools [/Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-01-dotnet-wasm-smoke/WasmSmoke/WasmSmoke.csproj]
/usr/local/share/dotnet/sdk/10.0.100/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.ImportWorkloads.targets(38,5): error NETSDK1147: To install these workloads, run the following command: dotnet workload restore [/Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-01-dotnet-wasm-smoke/WasmSmoke/WasmSmoke.csproj]
```

`NativeFileReference` added to a scratch copy of the csproj (`<NativeFileReference Include="vkshim.c" />`), with no `WasmBuildNative` set:

```
$ dotnet publish -c Release -o out
  Determining projects to restore...
  Restored <scratch>/nfr/WasmSmoke.csproj (in 112 ms).
  WasmSmoke -> <scratch>/nfr/bin/Release/net10.0/WasmSmoke.dll
/usr/local/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.mono.toolchain.current/10.0.101/WorkloadManifest.targets(124,5): warning : @(NativeFileReference) is not empty, but the native references won't be linked in, because neither $(WasmBuildNative), nor $(RunAOTCompilation) are 'true'. NativeFileReference=vkshim.c [<scratch>/nfr/WasmSmoke.csproj]
  Optimizing assemblies for size. This process might take a while.
  Publishing without optimizations. Although it's optional for Blazor, we strongly recommend using `wasm-tools` workload! You can install it by running `dotnet workload install wasm-tools` from the command line.
  WasmSmoke -> <scratch>/nfr/out/
```

`InvariantGlobalization=true` (listed in the runtime pack's `PropertiesThatTriggerRelinking`, but not in the SDK's `_WasmNativeWorkloadNeeded` list) **does** publish without the workload. It drops the 3 `icudt*.dat` files, giving 5,716,676 B raw / 2,208,685 B gz / 1,787,725 B br in total (measured on a scratch copy of a slightly earlier revision of the test code, so `WasmSmoke.wasm` differs by about 1 KB; compare with 8,428,867 B raw / 2,437,183 B br for the final default publish below):

```
$ dotnet publish -c Release -p:InvariantGlobalization=true -o out-inv
  Determining projects to restore...
  All projects are up-to-date for restore.
  WasmSmoke -> <scratch>/nfr/bin/Release/net10.0/WasmSmoke.dll
  Optimizing assemblies for size. This process might take a while.
  Publishing without optimizations. Although it's optional for Blazor, we strongly recommend using `wasm-tools` workload! You can install it by running `dotnet workload install wasm-tools` from the command line.
  WasmSmoke -> <scratch>/nfr/out-inv/
```

The SDK decides via the workload manifest
`/usr/local/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.mono.toolchain.current/10.0.101/WorkloadManifest.targets`.
`_WasmNativeWorkloadNeeded` becomes true for `RunAOTCompilation`, `WasmBuildNative`, `WasmEnableSIMD=false`,
`WasmEnableExceptionHandling=false`, `InvariantTimezone`, `WasmNativeStrip=false`, `WasmNativeDebugSymbols`, `WasmSingleFileBundle=false`,
`EnableDiagnostics`, `WasmProfilers`, `EmccInitialHeapSize`/`EmccMaximumHeapSize`, `WasmGenerateAppBundle`. In this build
`WasmNativeWorkloadAvailable="false"`.

## Scaffold (task 1)

```
$ dotnet new install Microsoft.NET.Runtime.WebAssembly.Templates.net10::10.0.13
The colon separator "::" has been deprecated in favor of the at symbol "@" for separating the package from the version in dotnet new install. In your case, this means Microsoft.NET.Runtime.WebAssembly.Templates.net10@10.0.13 instead of Microsoft.NET.Runtime.WebAssembly.Templates.net10::10.0.13.
The following template packages will be installed:
   Microsoft.NET.Runtime.WebAssembly.Templates.net10::10.0.13
Success: Microsoft.NET.Runtime.WebAssembly.Templates.net10::10.0.13 installed the following templates:
Template Name            Short Name   Language  Tags
-----------------------  -----------  --------  -----------------------
Wasi Console App         wasiconsole  [C#]      Wasi/WasiConsole
WebAssembly Browser App  wasmbrowser  [C#]      Web/WebAssembly/Browser
WebAssembly Console App  wasmconsole  [C#]      Web/WebAssembly/Console
$ dotnet new wasmbrowser -n WasmSmoke -o projects/exp-01-dotnet-wasm-smoke/WasmSmoke
The template "WebAssembly Browser App" was created successfully.
```

Before the install, `dotnet new list wasm` only showed `blazorwasm`. The install added `wasmbrowser`, `wasmconsole` and `wasiconsole`.

Generated `WasmSmoke.csproj` (verbatim):

```xml
<Project Sdk="Microsoft.NET.Sdk.WebAssembly">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>
  </PropertyGroup>

  <ItemGroup>
    <StaticWebAssetFingerprintPattern Include="JS" Pattern="*.js" Expression="#[.{fingerprint}]!" />
  </ItemGroup>
</Project>
```

Generated `wwwroot/index.html` (verbatim):

```html
<!DOCTYPE html>
<!--  Licensed to the .NET Foundation under one or more agreements. -->
<!-- The .NET Foundation licenses this file to you under the MIT license. -->
<html>

<head>
  <title>WasmSmoke</title>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <link rel="preload" id="webassembly" />
  <script type="importmap"></script>
  <script type='module' src="main#[.{fingerprint}].js"></script>
</head>

<body>
  <h1>Stopwatch</h1>
  <p>
    Time elapsed in .NET is <span id="time"><i>loading...</i></span>
  </p>
  <p>
    <button id="pause">Pause</button>
    <button id="reset">Reset</button>
  </p>
</body>

</html>
```

Generated `wwwroot/main.js` (verbatim). It uses `dotnet.withApplicationArguments("start").create()`, `setModuleImports`, `getAssemblyExports`
and finally `await runMain()`. It does **not** use `dotnet.run()`, and `exit` is never called. The template `Program.cs` keeps
`Main` alive with an infinite `while (true) { …; await Task.Delay(1000); }` loop:

```js
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { dotnet } from './_framework/dotnet.js'

const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet
    .withApplicationArguments("start")
    .create();

setModuleImports('main.js', {
    dom: {
        setInnerText: (selector, time) => document.querySelector(selector).innerText = time
    }
});

const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);

document.getElementById('reset').addEventListener('click', e => {
    exports.StopwatchSample.Reset();
    e.preventDefault();
});

const pauseButton = document.getElementById('pause');
pauseButton.addEventListener('click', e => {
    const isRunning = exports.StopwatchSample.Toggle();
    pauseButton.innerText = isRunning ? 'Pause' : 'Start';
    e.preventDefault();
});

// run the C# Main() method and keep the runtime process running and executing further API calls
await runMain();
```

The template also generates `Properties/AssemblyInfo.cs` (`[assembly:System.Runtime.Versioning.SupportedOSPlatform("browser")]`) and
`Properties/launchSettings.json` (`applicationUrl: https://localhost:7136;http://localhost:5016`).

Changes made for this experiment:
- csproj: + 9 BRUTAL `PackageReference`s, `Nullable`/`ImplicitUsings` disabled.
- `Program.cs` replaced by the test harness.
- `main.js` replaced. It keeps `create()` + `setModuleImports` + `runMain()`, and adds JS imports, `addFunction` trampolines, the T13e benchmark and the T12 rAF loop.
- `index.html` text simplified; the placeholders are kept.
- Added `SigProbes.g.cs` and `ExtraProbes.cs`.

### Build output layout (.NET 10)

- The build does **not** create an `AppBundle/` folder. `dotnet build` writes `bin/<cfg>/net10.0/wwwroot/_framework/` with fingerprinted files
  (`dotnet.<hash>.js`, `dotnet.native.<hash>.wasm`, `dotnet.runtime.<hash>.js`, `<Assembly>.<hash>.wasm` = **Webcil** assemblies, plus `.gz`).
  `index.html`/`main.js` are served from the project `wwwroot` through static web assets (`WasmSmoke.staticwebassets.runtime.json`).
  `dotnet publish` produces a self-contained static site in `bin/Release/net10.0/publish/wwwroot/` (with `.gz` and `.br`).
- **Neither a `blazor.boot.json` nor a separate `dotnet.boot.js` is emitted.** The boot config JSON is embedded inside `dotnet.<hash>.js`
  between `/*json-start*/` and `/*json-end*/`. It contains `"mainAssemblyName": "WasmSmoke"`, `resources.wasmNative`, `coreAssembly`, `icu`, and so on.
- Build-time tasks are `ComputeWasmBuildAssets`, `ConvertDllsToWebCil` and `GenerateWasmBootJson` from `Microsoft.NET.Sdk.WebAssembly.Pack` 10.0.1.
  **`WasmAppBuilder` does not appear in the build log at all**: there is no native step, and `dotnet.native.wasm` (3,001,957 B) is copied
  unchanged from the runtime pack.
- A plain `dotnet build` / `dotnet publish` does **not** require `wasm-tools`. Publish prints this message (verbatim), but no warning:
  `Publishing without optimizations. Although it's optional for Blazor, we strongly recommend using `wasm-tools` workload! You can install it by running `dotnet workload install wasm-tools` from the command line.`

Excerpts from `dotnet build -v:d` of the unmodified template:

```
Found WebAssemblySdk pack 'Microsoft.NET.Sdk.WebAssembly.Pack@10.0.1'
Added Microsoft.NET.Sdk.WebAssembly.Pack@10.0.1 for build-time targets
/usr/local/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.mono.toolchain.current/10.0.101/WorkloadManifest.targets(41,5): message : Property reassignment: $(WasmNativeWorkloadAvailable)="false" (previous value: "") at /usr/local/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.mono.toolchain.current/10.0.101/WorkloadManifest.targets (41,5)
/usr/local/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.mono.toolchain.current/10.0.101/WorkloadManifest.targets(42,5): message : Property reassignment: $(WasmNativeWorkload)="false" (previous value: "net8.0") at /usr/local/share/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.mono.toolchain.current/10.0.101/WorkloadManifest.targets (42,5)
Using "ComputeWasmBuildAssets" task from assembly "/Users/asherwin/.nuget/packages/microsoft.net.sdk.webassembly.pack/10.0.1/build/../tools/net10.0/Microsoft.NET.Sdk.WebAssembly.Pack.Tasks.dll"
Using "ConvertDllsToWebCil" task from assembly "/Users/asherwin/.nuget/packages/microsoft.net.sdk.webassembly.pack/10.0.1/build/../tools/net10.0/Microsoft.NET.Sdk.WebAssembly.Pack.Tasks.dll"
Using "GenerateWasmBootJson" task from assembly "/Users/asherwin/.nuget/packages/microsoft.net.sdk.webassembly.pack/10.0.1/build/../tools/net10.0/Microsoft.NET.Sdk.WebAssembly.Pack.Tasks.dll"
Converting to Webcil: input /Users/asherwin/.nuget/packages/microsoft.netcore.app.runtime.mono.browser-wasm/10.0.1/runtimes/browser-wasm/lib/net10.0/System.Console.dll
```

Template Debug build (`dotnet build -v:m`, unmodified template):

```
  Determining projects to restore...
  Restored /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-01-dotnet-wasm-smoke/WasmSmoke/WasmSmoke.csproj (in 2.05 sec).
  WasmSmoke -> /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-01-dotnet-wasm-smoke/WasmSmoke/bin/Debug/net10.0/WasmSmoke.dll
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:04.36
```

## BRUTAL packages (task 2)

Restore succeeded with **0 warnings / 0 errors**. The prerelease transitive dependency `Microsoft.Extensions.ObjectPool 11.0.0-preview.5.26302.115`
(from Brutal.Strings) restored without any special setting; no prerelease opt-in was needed, because the package's own dependency names the exact prerelease version.

```
Installed Microsoft.Extensions.ObjectPool 11.0.0-preview.5.26302.115 from https://api.nuget.org/v3/index.json to /Users/asherwin/.nuget/packages/microsoft.extensions.objectpool/11.0.0-preview.5.26302.115 with content hash CQvL+zywyWbD5bmS3Fr7f6ObcYII7xizBz2GJGqnsgRDTZM42+CG4FkiJ2PnBJIHn0aEV6Sk5DyDv2WEOoKeTw==.
Restored /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-01-dotnet-wasm-smoke/WasmSmoke/WasmSmoke.csproj (in 863 ms).
0 Warning(s)
0 Error(s)
```

```
   Top-level Package                                        Requested      Resolved 
   > Ahwoo.BRUTAL.Collections                               2026.10.1      2026.10.1
   > Ahwoo.BRUTAL.Core                                      2026.10.1      2026.10.1
   > Ahwoo.BRUTAL.Logging                                   2026.10.2      2026.10.2
   > Ahwoo.BRUTAL.Maths                                     2026.10.1      2026.10.1
   > Ahwoo.BRUTAL.Memory                                    2026.10.1      2026.10.1
   > Ahwoo.BRUTAL.Numerics                                  2026.10.1      2026.10.1
   > Ahwoo.BRUTAL.Strings                                   2026.10.1      2026.10.1
   > Ahwoo.BRUTAL.Vulkan                                    2026.10.2      2026.10.2
   > Ahwoo.BRUTAL.Vulkan.Abstractions                       2026.10.2      2026.10.2
   > Microsoft.DotNet.HotReload.WebAssembly.Browser   (A)   [10.0.100, )   10.0.100 
   > Microsoft.NET.ILLink.Tasks                       (A)   [10.0.0, )     10.0.0   
   > Microsoft.NET.Sdk.WebAssembly.Pack               (A)   [10.0.1, )     10.0.1   

   Transitive Package                                           Resolved                  
   > CommunityToolkit.HighPerformance                           8.4.2                     
   > Microsoft.Extensions.Configuration                         10.0.0                    
   > Microsoft.Extensions.Configuration.Abstractions            10.0.0                    
   > Microsoft.Extensions.Configuration.Binder                  10.0.0                    
   > Microsoft.Extensions.DependencyInjection                   10.0.0                    
   > Microsoft.Extensions.DependencyInjection.Abstractions      10.0.0                    
   > Microsoft.Extensions.Logging                               10.0.0                    
   > Microsoft.Extensions.Logging.Abstractions                  10.0.0                    
   > Microsoft.Extensions.Logging.Configuration                 10.0.0                    
   > Microsoft.Extensions.Logging.Console                       10.0.0                    
   > Microsoft.Extensions.ObjectPool                            11.0.0-preview.5.26302.115
   > Microsoft.Extensions.Options                               10.0.0                    
   > Microsoft.Extensions.Options.ConfigurationExtensions       10.0.0                    
   > Microsoft.Extensions.Primitives                            10.0.0                    

(A) : Auto-referenced package.
```

Package contents: all `lib/net10.0` only. `Ahwoo.BRUTAL.Vulkan` additionally ships `runtimes/{osx-arm64,win-x64,linux-x64}/native/(lib)VulkanEx.*`;
these are not copied to the browser-wasm output. Core/Numerics/Strings also ship Roslyn analyzers.
The compiled `Brutal.Vulkan.dll` contains the strings `vulkan-1`, `VulkanEx`, `vulkan` and `Failed to load Vulkan library`, which matches `ImportResolver.cs`.

Build with BRUTAL + the test code (`dotnet build -c Release -v:m`):

```
  Determining projects to restore...
  Restored /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-01-dotnet-wasm-smoke/WasmSmoke/WasmSmoke.csproj (in 112 ms).
  WasmSmoke -> /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-01-dotnet-wasm-smoke/WasmSmoke/bin/Release/net10.0/WasmSmoke.dll
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:01.27
```

Publish (`dotnet publish -c Release -v:m`):

```
  Determining projects to restore...
  Restored /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-01-dotnet-wasm-smoke/WasmSmoke/WasmSmoke.csproj (in 112 ms).
  WasmSmoke -> /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-01-dotnet-wasm-smoke/WasmSmoke/bin/Release/net10.0/WasmSmoke.dll
  Optimizing assemblies for size. This process might take a while.
  Publishing without optimizations. Although it's optional for Blazor, we strongly recommend using `wasm-tools` workload! You can install it by running `dotnet workload install wasm-tools` from the command line.
  WasmSmoke -> /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-01-dotnet-wasm-smoke/WasmSmoke/bin/Release/net10.0/publish/
```

Trimmer: a publish with `-p:SuppressTrimAnalysisWarnings=false -p:TrimmerSingleWarn=false`, with ILLink forced to re-run
("Optimizing assemblies for size."), produced **0 warnings**. The WebAssembly SDK suppresses trim analysis warnings by default
(`Microsoft.NET.Sdk.WebAssembly.Browser.targets:47`). Trimming removed `Brutal.Vulkan.Abstractions` and `CommunityToolkit.HighPerformance`
completely (unused), shrank `Brutal.Vulkan` from 1,559,317 B to 215,829 B, and shrank `Brutal.Numerics` from 1,072,405 B to 134,933 B.

### Sizes

Default `dotnet publish -c Release` (`bin/Release/net10.0/publish/wwwroot`, trimmed, interpreter, with ICU):

| file | bytes | .gz | .br |
|---|---:|---:|---:|
| _framework/dotnet.native.muve7a13r4.wasm | 3,001,957 | 1,207,839 | 976,573 |
| _framework/System.Private.CoreLib.3yfhh5etzr.wasm | 1,418,517 | 551,153 | 445,240 |
| _framework/icudt_no_CJK.lfu7j35m59.dat | 1,107,168 | 347,783 | 222,250 |
| _framework/icudt_CJK.tjcz0u77k5.dat | 956,416 | 359,741 | 248,840 |
| _framework/icudt_EFIGS.tptq2av103.dat | 550,832 | 220,093 | 143,983 |
| _framework/Brutal.Vulkan.sq3dem5idr.wasm | 215,829 | 75,128 | 53,186 |
| _framework/dotnet.runtime.0j6ezsi0n0.js | 198,479 | 57,287 | 47,846 |
| _framework/dotnet.native.cs8mcre4gh.js | 145,050 | 34,952 | 30,468 |
| _framework/Brutal.Numerics.xxbth47eiu.wasm | 134,933 | 44,455 | 29,735 |
| _framework/WasmSmoke.j9ggz3u6vu.wasm | 89,877 | 32,608 | 27,208 |
| _framework/System.Private.Xml.iwkzah6w0r.wasm | 71,445 | 10,308 | 7,586 |
| _framework/dotnet.so5iu6unl3.js | 48,349 | 15,036 | 13,251 |
| _framework/Microsoft.Extensions.DependencyInjection.2b47rl6evf.wasm | 45,845 | 22,376 | 19,202 |
| _framework/Microsoft.Extensions.Logging.Console.2kfwy3qgea.wasm | 44,821 | 21,233 | 18,000 |
| _framework/System.Runtime.InteropServices.JavaScript.dpv1rgnoof.wasm | 43,285 | 19,694 | 16,733 |
| _framework/System.Text.Json.n4f8hebchc.wasm | 38,677 | 15,190 | 12,774 |
| _framework/System.Text.Encodings.Web.hd0wbmhgzz.wasm | 26,901 | 9,963 | 8,609 |
| _framework/Microsoft.Extensions.Logging.d0tffiyiw1.wasm | 23,317 | 11,343 | 9,889 |
| _framework/Microsoft.Extensions.Logging.Abstractions.1w5zm9yyyx.wasm | 20,245 | 9,869 | 8,534 |
| _framework/Brutal.Strings.iazrftv6eu.wasm | 19,733 | 8,664 | 7,369 |
| _framework/System.Diagnostics.DiagnosticSource.92jrc7l839.wasm | 19,733 | 9,113 | 7,941 |
| _framework/System.Collections.Concurrent.7o12t90rd0.wasm | 17,173 | 8,374 | 7,517 |
| _framework/Microsoft.Extensions.Options.k1rlu91s0i.wasm | 16,661 | 8,158 | 7,086 |
| _framework/Microsoft.Extensions.DependencyInjection.Abstractions.p37ss99oer.wasm | 15,637 | 6,829 | 5,917 |
| _framework/System.Console.0adtvg2pwr.wasm | 15,637 | 7,854 | 6,574 |
| _framework/Microsoft.Extensions.Configuration.ktmksw221j.wasm | 15,637 | 7,784 | 6,774 |
| _framework/Brutal.Core.jjlqcbi47e.wasm | 15,125 | 7,339 | 6,334 |
| _framework/System.Linq.5mnb8a5q84.wasm | 11,541 | 5,480 | 4,831 |
| _framework/Brutal.Logging.1zy4q6xw2o.wasm | 9,493 | 4,502 | 3,899 |
| _framework/Microsoft.Extensions.Primitives.pekkj2grdp.wasm | 8,981 | 3,886 | 3,366 |
| _framework/Microsoft.Extensions.Logging.Configuration.ywe64bdz5n.wasm | 8,469 | 3,717 | 3,294 |
| _framework/Brutal.Collections.n9v9df3rux.wasm | 7,957 | 3,779 | 3,300 |
| main.s4xmwrl3ig.js | 7,740 | 2,844 | 2,477 |
| _framework/Microsoft.Extensions.ObjectPool.h82y1eghcb.wasm | 7,445 | 3,465 | 2,957 |
| _framework/Microsoft.Extensions.Configuration.Abstractions.6ngwwmy0ma.wasm | 7,445 | 2,983 | 2,602 |
| _framework/Microsoft.Extensions.Configuration.Binder.bki7ll1vls.wasm | 6,933 | 2,647 | 2,265 |
| _framework/Microsoft.Extensions.Options.ConfigurationExtensions.qt215lrhtr.wasm | 5,909 | 2,559 | 2,202 |
| _framework/Brutal.Memory.j0gb5xjpxc.wasm | 5,909 | 2,473 | 2,156 |
| _framework/System.Memory.80nhi58gzj.wasm | 5,909 | 2,371 | 2,079 |
| _framework/System.IO.Pipelines.3i2gckl6xn.wasm | 5,909 | 2,553 | 2,168 |
| _framework/Brutal.Maths.k1usr15xwr.wasm | 5,397 | 2,180 | 1,890 |
| _framework/System.ComponentModel.u61y5gga6s.wasm | 4,885 | 1,945 | 1,721 |
| index.html | 1,666 | 691 | 557 |
| **total (43 files)** | **8,428,867** | **3,178,241** | **2,437,183** |

Untrimmed Debug build (`bin/Debug/net10.0/wwwroot`, 207 files incl. 198 `.wasm` assemblies): **30,392,601 B raw / 11,324,595 B gz**.
Biggest items: `System.Private.CoreLib` 4,869,913; `dotnet.native.wasm` 3,001,957; `Brutal.Vulkan` 1,559,317; `Brutal.Numerics` 1,072,405.

## Serving (task 4)

- `dotnet run -c Release` starts the dev host `WasmAppHost --use-staticwebassets` (verbatim output below). `GET http://localhost:5016/` → 200 text/html.
- For headless runs, `test/serve.mjs` serves the publish folder with explicit MIME types (`.wasm` → `application/wasm`).
  `python3 -I -m http.server` would also work here: Python 3.9.6's `mimetypes` maps `.wasm` to `application/wasm`. It was not used.
- The dotnet.js runtime printed **no warnings** about missing browser features in any run. The only non-test console error was a 404 for `/favicon.ico`.

```
$ dotnet run -c Release --launch-profile WasmSmoke
Using launch settings from /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-01-dotnet-wasm-smoke/WasmSmoke/Properties/launchSettings.json...
WasmAppHost --use-staticwebassets --runtime-config /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-01-dotnet-wasm-smoke/WasmSmoke/bin/Release/net10.0/WasmSmoke.runtimeconfig.json
warn: Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServer[8]
      The ASP.NET Core developer certificate is not trusted. For information about trusting the ASP.NET Core developer certificate, see https://aka.ms/aspnet/https-trust-dev-cert
App url: http://localhost:5016/
App url: https://localhost:7136/
Debug at url: http://localhost:5016/_framework/debug
Debug at url: https://localhost:7136/_framework/debug
[probe] GET http://localhost:5016/ -> 200 text/html 1666 bytes
[probe] GET http://localhost:5016/_framework/dotnet.js -> 200 text/javascript 82551 bytes
```

## Raw console output

### Verbose Release build: final complete pass + every isolation run (`test/console.release-verbose.log`, verbatim from "pass 8" to end)

Passes 1–7 are identical up to the crash points listed in the table. See the log file.

```
[+ 15872ms] [driver] ===== pass 8: http://127.0.0.1:58249/index.html?skip=T07,T08,T08b,T09,T09b,T10,T13b
[+ 15978ms] [console.log] HOST runtime created in 42 ms; runtimeBuildInfo={"productVersion":"10.0.1","gitHash":"fad253f51b461736dfd3cd9c15977bb7493becef","buildConfiguration":"Release","wasmEnableThreads":false,"wasmEnableSIMD":true,"wasmEnableExceptionHandling":true}
[+ 15994ms] [console.log] SMOKE START args=[start,skip=T07,T08,T08b,T09,T09b,T10,T13b]
[+ 16003ms] [console.log] BEGIN T01
[+ 16006ms] [console.log] TEST T01 env: PASS IsBrowser=True; IsWasi=False; OSDescription='Browser'; OSArchitecture=Wasm; ProcessArchitecture=Wasm; FrameworkDescription='.NET 10.0.1'; RuntimeIdentifier=browser-wasm; IntPtr.Size=4; ProcessorCount=1; Is64BitProcess=False; Vector128.IsHardwareAccelerated=True; Vector.IsHardwareAccelerated=True; Vector<float>.Count=4; PackedSimd.IsSupported=True; Stopwatch.Frequency=1000000000; Stopwatch.IsHighResolution=True; Environment.Version=10.0.1; IsDynamicCodeSupported=True; IsDynamicCodeCompiled=False; ThreadId=1; GC.TotalAvailableMemoryBytes=2147483648
[+ 16006ms] [console.log] BEGIN T02
[+ 16014ms] [console.log] TEST T02 brutal-platform: FAIL System.TypeInitializationException: The type initializer for 'Brutal.Platform' threw an exception. | inner System.PlatformNotSupportedException: Unsupported platform
[+ 16015ms] [console.log] BEGIN T03
[+ 16020ms] [console.log] TEST T03 brutal-numerics: PASS float3.Transform((1,0,0), RotY(pi/2)*T(1,2,3)) = <0.99999994, 2, 2> (System.Numerics=<0.99999994, 2, 2>); float4x4*float4 = <-4.371139E-08, 0, 1, 2>; cross(X,Y)=<0, 0, 1>; sizeof(float4x4)=64; Random(42).NextSinglePointInBox(2)=<1.3362129, 0.2818146, 0.25103658>
[+ 16020ms] [console.log] BEGIN T03b
[+ 16023ms] [console.log] TEST T03b brutal-collections-logging: PASS NativeList: ok Size=12 B Capacity=16 B; LogCategory.Make: ok Name=Smoke LogSystem.IsEnabled=False
[+ 16023ms] [console.log] BEGIN T03c
[+ 16046ms] [console.log] TEST T03c brutal-logging-console-provider: FAIL System.PlatformNotSupportedException: Operation is not supported on this platform.
[+ 16046ms] [console.log] BEGIN T03d
[+ 16051ms] [console.log] LOG [Information] SmokeCustom: hello from Brutal.Logging via a custom ILoggerProvider (Console.WriteLine)
[+ 16051ms] [console.log] TEST T03d brutal-logging-custom-provider: PASS LogSystem.Initialize(custom provider) ok; IsEnabled=True (expect a 'LOG [Information] SmokeCustom' line above)
[+ 16051ms] [console.log] BEGIN T04
[+ 16069ms] [console.log] TEST T04 brutal-memory: PASS Mem.AllocZeroed<byte>(1MB) at 0x1BD4538 zeroed=True sum=133693440; Mem.Alloc(1MB) at 0x1BD4538 last=0xAB; Mem.AllocAndCopy(float4).W=4; NativeMemory.Alloc ok v=7; AlignedAlloc(256) aligned=True; AllocHGlobal ok v=0x1234567
[+ 16069ms] [console.log] BEGIN T04b
[+ 16073ms] [console.log] TEST T04b memory-growth-256MB: PASS 256MB NativeMemory.Alloc at 0x1D7F6C0 touched+freed in 4.2 ms
[+ 16073ms] [console.log] BEGIN T05
[+ 16074ms] [console.log] INFO T05 second access to Vulkan.GetInstanceProcAddr: EX System.TypeInitializationException: The type initializer for 'Brutal.VulkanApi.Vulkan' threw an exception. | inner System.NullReferenceException: Failed to load Vulkan library
[+ 16074ms] [console.log] INFO T05 access to Vulkan.Log (static field of same class): EX System.TypeInitializationException: The type initializer for 'Brutal.VulkanApi.Vulkan' threw an exception. | inner System.NullReferenceException: Failed to load Vulkan library
[+ 16074ms] [console.log] TEST T05 brutal-vulkan-static-ctor: FAIL System.TypeInitializationException: The type initializer for 'Brutal.VulkanApi.Vulkan' threw an exception. | inner System.NullReferenceException: Failed to load Vulkan library
[+ 16074ms] [console.log] BEGIN T05b
[+ 16076ms] [console.log] TEST T05b brutal-vulkan-types-without-ctor: PASS VkInstance(0x1234).VkHandle=0x1234 IsNull=False NullHandle.IsNull=True; Unsafe.SizeOf<VkBufferCreateInfo>=56 (expect 56); offsetof(Next)=8 offsetof(QueueFamilyIndices)=48 (expect 8/48: explicit x64 layout kept on wasm32); bci.sType=12 (BufferCreateInfo=12) Size=65536 Usage=VertexBufferBit; sizeof(VkBuffer)=4 (expect 4 on wasm32; C ABI non-dispatchable handle is 8); sizeof(VkInstance)=4 sizeof(PfnVkVoidFunction)=4; Unsafe.SizeOf<VkInstanceCreateInfo>=64 Unsafe.SizeOf<VkPhysicalDeviceProperties>=824; Marshal.SizeOf<VkBufferCreateInfo>=56; VK.MAX_EXTENSION_NAME_SIZE=256 VK.WHOLE_SIZE as ByteSize64=0xFFFFFFFF (C VK_WHOLE_SIZE=~0ULL=0xFFFFFFFFFFFFFFFF); VkResult.Success=0 VkResult.ErrorDeviceLost=-4
[+ 16076ms] [console.log] BEGIN T06
[+ 16077ms] [console.log] TEST T06 nativelibrary: PASS TryLoad(vulkan)=False h=0x0; TryLoad(libvulkan.so.1)=False h=0x0; TryLoad(__Internal)=False h=0x0; TryLoad(libSystem.Native)=True h=0x93B68; Load(vulkan, asm, LegacyBehavior)=EX System.DllNotFoundException: vulkan; GetMainProgramHandle()=EX System.ComponentModel.Win32Exception: No error information; TryGetExport(main,malloc)=EX System.ArgumentNullException: Value cannot be null. (Parameter 'handle'); TryGetExport(main,mono_wasm_load_runtime)=EX System.ArgumentNullException: Value cannot be null. (Parameter 'handle'); TryGetExport(main,vkGetInstanceProcAddr)=EX System.ArgumentNullException: Value cannot be null. (Parameter 'handle')
[+ 16077ms] [console.log] BEGIN T06b
[+ 16078ms] [console.log] TEST T06b dllimport-vulkan-local: FAIL System.DllNotFoundException: vulkan
[+ 16078ms] [console.log] BEGIN T06c
[+ 16078ms] [console.log] INFO T06c SetDllImportResolver(Brutal.Vulkan, ...) -> ok
[+ 16078ms] [console.log] INFO T06c vkEnumerateInstanceVersion after resolver -> EX System.EntryPointNotFoundException: vkEnumerateInstanceVersion; resolverCalls=1 names=[vulkan;]
[+ 16079ms] [console.log] TEST T06c brutal-libraryimport-vkEnumerateInstanceVersion: FAIL System.DllNotFoundException: vulkan
[+ 16079ms] [console.log] BEGIN T06d
[+ 16079ms] [console.log] TEST T06d dllimport-known-native-SystemNative_GetPid: FAIL System.EntryPointNotFoundException: SystemNative_GetPid
[+ 16079ms] [console.log] BEGIN T07a
[+ 16079ms] [console.log] TEST T07a fnptr-ldftn-value (no call): PASS &AddUco=0x0 &MyGetInstanceProcAddr=0x0 &Mixed=0x0 (wasm function-table indices; 0 would mean no native-to-interp thunk)
[+ 16079ms] [console.log] BEGIN T08c
[+ 16082ms] [console.log] TEST T08c fnptr-js-addFunction: PASS Module.addFunction -> table index 10611; C# calli unmanaged[Cdecl] f(2,3)=302
[+ 16083ms] [console.log] BEGIN T08d
[+ 16085ms] [console.log] TEST T08d brutal-FunctionTable-from-JS-gipa (JS ICD): PASS JS gipa idx=10613; BRUTAL VkInstance.FunctionTable filled from JS; vkEnumeratePhysicalDevices (JS) -> Success/Success count=1 pd=0x7EB6; jsGipaCalls=105 distinctNames=105 jsEnumCalls=2
[+ 16085ms] [console.log] BEGIN T08e
[+ 16086ms] [console.log] TEST T08e fnptr-js-trampoline-to-JSExport (managed ICD via JS hop): PASS gipa trampoline idx=10614, enumPD trampoline idx=10615; BRUTAL FunctionTable ctor -> 105 managed gipa lookups (re-entrant C#->JS->C#); vkEnumeratePhysicalDevices -> Success/Success count=1 pd=0xBEEF
[+ 16086ms] [console.log] BEGIN T11a
[+ 16086ms] [console.log] TEST T11a jsimport-hasWebGpu: PASS navigator.gpu present=True
[+ 16086ms] [console.log] BEGIN T11b
[+ 16086ms] [console.log] TEST T11b jsimport-span-memoryview: PASS SumBytes(Span<byte>[256]) = 32640 (expected 32640)
[+ 16086ms] [console.log] BEGIN T11c
[+ 16088ms] [console.log] TEST T11c jsimport-raw-pointer-heap: PASS SumPtr(0x188DF90, 1000) via runtime.localHeapViewU8() = 126516 (expected 126516)
[+ 16088ms] [console.log] BEGIN T11d
[+ 16088ms] [console.log] TEST T11d jsimport-heap-api-survey: PASS runtime.localHeapViewU8=function runtime.getHeapU8=function runtime.setHeapI32=function Module.HEAPU8=object Module.wasmExports=object Module.wasmExports.memory=[object WebAssembly.Memory] Module.wasmMemory=undefined Module.addFunction=function Module.UTF8ToString=function Module._malloc=function heapBytes=299368448 SharedArrayBuffer-backed=false crossOriginIsolated=false wasmEnableSIMD=true wasmEnableThreads=false wasmEnableEH=true
[+ 16088ms] [console.log] BEGIN T11e
[+ 16093ms] [console.log] TEST T11e jsimport-async-webgpu-adapter: PASS adapter vendor='apple' architecture='metal-3' device='' description='' features=22 maxBufferSize=4294967292
[+ 16093ms] [console.log] BEGIN T13a
[+ 16102ms] [console.log] TEST T13a perf-direct-static-call: PASS direct static call: 9.0 ns/call (total 9 ms, acc=500000500000)
[+ 16102ms] [console.log] BEGIN T13f
[+ 16115ms] [console.log] TEST T13f perf-managed-fnptr (delegate*<int,int,int>): PASS delegate*<int,int,int> (managed calli) -> C# static: 12.1 ns/call (total 12 ms, acc=500000500000)
[+ 16115ms] [console.log] BEGIN T13b2
[+ 16144ms] [console.log] TEST T13b2 perf-fnptr-to-JS-addFunction: PASS delegate* unmanaged[Cdecl] -> JS addFunction (a+b*100): 26.5 ns/call (total 26 ms, acc=500099500000)
[+ 16144ms] [console.log] BEGIN T13b3
[+ 16324ms] [console.log] TEST T13b3 perf-fnptr-to-JS-trampoline-to-JSExport: PASS delegate* unmanaged[Cdecl] -> JS trampoline -> [JSExport] C#: 172.7 ns/call (total 173 ms, acc=500000500000)
[+ 16324ms] [console.log] BEGIN T13c
[+ 16434ms] [console.log] TEST T13c perf-jsimport-int: PASS JSImport int->int: 105.3 ns/call (total 105 ms, acc=499999500000)
[+ 16435ms] [console.log] BEGIN T13d
[+ 16578ms] [console.log] TEST T13d perf-jsimport-span64: PASS JSImport Span<byte>[64] as MemoryView (JS reads .byteLength): 137.6 ns/call (total 138 ms, acc=64000000)
[+ 16578ms] [console.log] BEGIN T13d2
[+ 16606ms] [console.log] TEST T13d2 perf-jsimport-span64-copy: PASS JSImport Span<byte>[64] as MemoryView (JS .slice() copy + sum), 100k iters: 217.0 ns/call (total 22 ms, acc=6400000)
[+ 16606ms] [console.log] SMOKE MAIN DONE pass=24 fail=6 (T12 runs from JS rAF after Main returns)
[+ 16607ms] [console.log] HOST runMain resolved: exitCode=0 after 672 ms
[+ 16745ms] [console.log] TEST T13e perf-jsexport (JS->C# [JSExport] int->int, 1M): PASS 133.4 ns/call (total 133 ms, acc=500000500000)
[+ 16892ms] [console.log] TEST T12 jsexport-tick: PASS ticks=10 first=829.0ms last=979.0ms span=150.0ms (~16.67 ms/frame); runtime alive after Main returned
[+ 16892ms] [console.log] TICKS DONE
[+ 16892ms] [driver] pass 8 finished: TICKS DONE
[+ 16901ms] [driver] crashed test ids: ["T07","T08","T08b","T09","T09b","T10","T13b"]
[+ 16901ms] [driver] ===== isolation T07: http://127.0.0.1:58249/index.html?tests=T07
[+ 17002ms] [console.log] HOST runtime created in 43 ms; runtimeBuildInfo={"productVersion":"10.0.1","gitHash":"fad253f51b461736dfd3cd9c15977bb7493becef","buildConfiguration":"Release","wasmEnableThreads":false,"wasmEnableSIMD":true,"wasmEnableExceptionHandling":true}
[+ 17019ms] [console.log] SMOKE START args=[start,tests=T07]
[+ 17028ms] [console.log] BEGIN T07
[+ 17031ms] [console.log] INFO T07 calling &AddUco=0x0 ...
[+ 17031ms] [pageerror] RuntimeError: null function
    at $func298 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:128941)
    at $func222 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:117402)
    at $func213 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:60393)
    at $func256 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:121206)
    at $func3231 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:989138)
    at $func2553 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:815252)
    at $func2552 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:815140)
    at $func1907 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:659170)
    at $func298 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:129053)
    at $func222 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:117402)
[+ 19032ms] [driver] isolation T07 finished: CRASH (attributed to T07)
[+ 19045ms] [driver] ===== isolation T08: http://127.0.0.1:58249/index.html?tests=T08
[+ 19151ms] [console.log] HOST runtime created in 42 ms; runtimeBuildInfo={"productVersion":"10.0.1","gitHash":"fad253f51b461736dfd3cd9c15977bb7493becef","buildConfiguration":"Release","wasmEnableThreads":false,"wasmEnableSIMD":true,"wasmEnableExceptionHandling":true}
[+ 19168ms] [console.log] SMOKE START args=[start,tests=T08]
[+ 19176ms] [console.log] BEGIN T08
[+ 19179ms] [console.log] INFO T08 calling gipa.Invoke=0x0 ...
[+ 19180ms] [pageerror] RuntimeError: null function
    at $func298 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:128941)
    at $func222 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:117402)
    at $func213 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:60393)
    at $func256 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:121206)
    at $func3231 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:989138)
    at $func2553 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:815252)
    at $func2552 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:815140)
    at $func1907 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:659170)
    at $func298 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:129053)
    at $func222 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:117402)
[+ 21180ms] [driver] isolation T08 finished: CRASH (attributed to T08)
[+ 21192ms] [driver] ===== isolation T08b: http://127.0.0.1:58249/index.html?tests=T08b
[+ 21302ms] [console.log] HOST runtime created in 42 ms; runtimeBuildInfo={"productVersion":"10.0.1","gitHash":"fad253f51b461736dfd3cd9c15977bb7493becef","buildConfiguration":"Release","wasmEnableThreads":false,"wasmEnableSIMD":true,"wasmEnableExceptionHandling":true}
[+ 21319ms] [console.log] SMOKE START args=[start,tests=T08b]
[+ 21327ms] [console.log] BEGIN T08b
[+ 21330ms] [pageerror] RuntimeError: null function
    at $func298 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:128941)
    at $func222 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:117402)
    at $func213 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:60393)
    at $func256 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:121206)
    at $func3231 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:989138)
    at $func2553 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:815252)
    at $func2552 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:815140)
    at $func1907 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:659170)
    at $func298 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:129053)
    at $func222 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:117402)
[+ 23331ms] [driver] isolation T08b finished: CRASH (attributed to T08b)
[+ 23336ms] [driver] ===== isolation T09: http://127.0.0.1:58249/index.html?tests=T09
[+ 23439ms] [console.log] HOST runtime created in 42 ms; runtimeBuildInfo={"productVersion":"10.0.1","gitHash":"fad253f51b461736dfd3cd9c15977bb7493becef","buildConfiguration":"Release","wasmEnableThreads":false,"wasmEnableSIMD":true,"wasmEnableExceptionHandling":true}
[+ 23457ms] [console.log] SMOKE START args=[start,tests=T09]
[+ 23466ms] [console.log] BEGIN T09
[+ 23468ms] [console.error] [MONO] /__w/1/s/src/runtime/src/mono/mono/mini/aot-runtime-wasm.c:188 <disabled>
Error
    at Mc (http://127.0.0.1:58249/_framework/dotnet.runtime.0j6ezsi0n0.js:3:172283)
    at http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:wasm-function[158]:0xac63
    at http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:wasm-function[656]:0x43dbd
    at http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:wasm-function[580]:0x4246d
    at http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:wasm-function[582]:0x42530
    at http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:wasm-function[583]:0x42563
    at http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:wasm-function[213]:0xef6d
    at http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:wasm-function[256]:0x1d975
    at http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:wasm-function[3231]:0xf17d1
    at http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:wasm-function[2553]:0xc7093
[+ 23468ms] [pageerror] ExitStatus: ExitStatus
[+ 25470ms] [driver] isolation T09 finished: CRASH (attributed to T09)
[+ 25476ms] [driver] ===== isolation T09b: http://127.0.0.1:58249/index.html?tests=T09b
[+ 25580ms] [console.log] HOST runtime created in 41 ms; runtimeBuildInfo={"productVersion":"10.0.1","gitHash":"fad253f51b461736dfd3cd9c15977bb7493becef","buildConfiguration":"Release","wasmEnableThreads":false,"wasmEnableSIMD":true,"wasmEnableExceptionHandling":true}
[+ 25597ms] [console.log] SMOKE START args=[start,tests=T09b]
[+ 25605ms] [console.log] BEGIN T09b
[+ 25609ms] [console.error] [MONO] /__w/1/s/src/runtime/src/mono/mono/mini/aot-runtime-wasm.c:188 <disabled>
Error
    at Mc (http://127.0.0.1:58249/_framework/dotnet.runtime.0j6ezsi0n0.js:3:172283)
    at http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:wasm-function[158]:0xac63
    at http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:wasm-function[656]:0x43dbd
    at http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:wasm-function[580]:0x4246d
    at http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:wasm-function[582]:0x42530
    at http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:wasm-function[583]:0x42563
    at http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:wasm-function[213]:0xef6d
    at http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:wasm-function[256]:0x1d975
    at http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:wasm-function[3231]:0xf17d1
    at http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:wasm-function[2553]:0xc7093
[+ 25609ms] [pageerror] ExitStatus: ExitStatus
[+ 27609ms] [driver] isolation T09b finished: CRASH (attributed to T09b)
[+ 27622ms] [driver] ===== isolation T10: http://127.0.0.1:58249/index.html?tests=T10
[+ 27735ms] [console.log] HOST runtime created in 42 ms; runtimeBuildInfo={"productVersion":"10.0.1","gitHash":"fad253f51b461736dfd3cd9c15977bb7493becef","buildConfiguration":"Release","wasmEnableThreads":false,"wasmEnableSIMD":true,"wasmEnableExceptionHandling":true}
[+ 27752ms] [console.log] SMOKE START args=[start,tests=T10]
[+ 27761ms] [console.log] BEGIN T10
[+ 27763ms] [pageerror] RuntimeError: memory access out of bounds
    at $func89 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:26440)
    at $func251 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:119931)
    at $func3206 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:983240)
    at invoke_vi (http://127.0.0.1:58249/_framework/dotnet.native.cs8mcre4gh.js:8:142979)
    at $mono_llvm_cpp_catch_exception (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:1108196)
    at $func3432 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:1062055)
    at $func3205 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:983049)
    at $func3203 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:982738)
    at $func2567 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:823133)
    at $func2227 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:743433)
[+ 29763ms] [driver] isolation T10 finished: CRASH (attributed to T10)
[+ 29777ms] [driver] ===== isolation T13b: http://127.0.0.1:58249/index.html?tests=T13b
[+ 29887ms] [console.log] HOST runtime created in 42 ms; runtimeBuildInfo={"productVersion":"10.0.1","gitHash":"fad253f51b461736dfd3cd9c15977bb7493becef","buildConfiguration":"Release","wasmEnableThreads":false,"wasmEnableSIMD":true,"wasmEnableExceptionHandling":true}
[+ 29904ms] [console.log] SMOKE START args=[start,tests=T13b]
[+ 29912ms] [console.log] BEGIN T13b
[+ 29914ms] [pageerror] RuntimeError: null function
    at $func298 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:128941)
    at $func222 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:117402)
    at $func213 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:60393)
    at $func256 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:121206)
    at $func3231 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:989138)
    at $func2553 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:815252)
    at $func2552 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:815140)
    at $func1907 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:659170)
    at $func298 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:129053)
    at $func222 (http://127.0.0.1:58249/_framework/dotnet.native.muve7a13r4.wasm:1:117402)
[+ 31914ms] [driver] isolation T13b finished: CRASH (attributed to T13b)
```

### Default-build message variants (`test/console.log`, final pass, TEST/INFO/LOG lines only)

The default trimmed publish (`UseSystemResourceKeys=true`) shows resource keys instead of messages, e.g.
`TypeInitialization_Type, Brutal.Platform`, `Arg_PlatformNotSupported`, `ArgumentNull_Generic Arg_ParamName_Name, handle`:

```
[+ 15980ms] [console.log] TEST T01 env: PASS IsBrowser=True; IsWasi=False; OSDescription='Browser'; OSArchitecture=Wasm; ProcessArchitecture=Wasm; FrameworkDescription='.NET 10.0.1'; RuntimeIdentifier=browser-wasm; IntPtr.Size=4; ProcessorCount=1; Is64BitProcess=False; Vector128.IsHardwareAccelerated=True; Vector.IsHardwareAccelerated=True; Vector<float>.Count=4; PackedSimd.IsSupported=True; Stopwatch.Frequency=1000000000; Stopwatch.IsHighResolution=True; Environment.Version=10.0.1; IsDynamicCodeSupported=True; IsDynamicCodeCompiled=False; ThreadId=1; GC.TotalAvailableMemoryBytes=2147483648
[+ 15981ms] [console.log] TEST T02 brutal-platform: FAIL System.TypeInitializationException: TypeInitialization_Type, Brutal.Platform | inner System.PlatformNotSupportedException: Unsupported platform
[+ 15987ms] [console.log] TEST T03 brutal-numerics: PASS float3.Transform((1,0,0), RotY(pi/2)*T(1,2,3)) = <0.99999994, 2, 2> (System.Numerics=<0.99999994, 2, 2>); float4x4*float4 = <-4.371139E-08, 0, 1, 2>; cross(X,Y)=<0, 0, 1>; sizeof(float4x4)=64; Random(42).NextSinglePointInBox(2)=<1.3362129, 0.2818146, 0.25103658>
[+ 15990ms] [console.log] TEST T03b brutal-collections-logging: PASS NativeList: ok Size=12 B Capacity=16 B; LogCategory.Make: ok Name=Smoke LogSystem.IsEnabled=False
[+ 16013ms] [console.log] TEST T03c brutal-logging-console-provider: FAIL System.PlatformNotSupportedException: Arg_PlatformNotSupported
[+ 16018ms] [console.log] LOG [Information] SmokeCustom: hello from Brutal.Logging via a custom ILoggerProvider (Console.WriteLine)
[+ 16018ms] [console.log] TEST T03d brutal-logging-custom-provider: PASS LogSystem.Initialize(custom provider) ok; IsEnabled=True (expect a 'LOG [Information] SmokeCustom' line above)
[+ 16035ms] [console.log] TEST T04 brutal-memory: PASS Mem.AllocZeroed<byte>(1MB) at 0x1AF8040 zeroed=True sum=133693440; Mem.Alloc(1MB) at 0x1AF8040 last=0xAB; Mem.AllocAndCopy(float4).W=4; NativeMemory.Alloc ok v=7; AlignedAlloc(256) aligned=True; AllocHGlobal ok v=0x1234567
[+ 16040ms] [console.log] TEST T04b memory-growth-256MB: PASS 256MB NativeMemory.Alloc at 0x1CB7DC0 touched+freed in 4.3 ms
[+ 16041ms] [console.log] INFO T05 second access to Vulkan.GetInstanceProcAddr: EX System.TypeInitializationException: TypeInitialization_Type, Brutal.VulkanApi.Vulkan | inner System.NullReferenceException: Failed to load Vulkan library
[+ 16041ms] [console.log] INFO T05 access to Vulkan.Log (static field of same class): EX System.TypeInitializationException: TypeInitialization_Type, Brutal.VulkanApi.Vulkan | inner System.NullReferenceException: Failed to load Vulkan library
[+ 16041ms] [console.log] TEST T05 brutal-vulkan-static-ctor: FAIL System.TypeInitializationException: TypeInitialization_Type, Brutal.VulkanApi.Vulkan | inner System.NullReferenceException: Failed to load Vulkan library
[+ 16043ms] [console.log] TEST T05b brutal-vulkan-types-without-ctor: PASS VkInstance(0x1234).VkHandle=0x1234 IsNull=False NullHandle.IsNull=True; Unsafe.SizeOf<VkBufferCreateInfo>=56 (expect 56); offsetof(Next)=8 offsetof(QueueFamilyIndices)=48 (expect 8/48: explicit x64 layout kept on wasm32); bci.sType=12 (BufferCreateInfo=12) Size=65536 Usage=VertexBufferBit; sizeof(VkBuffer)=4 (expect 4 on wasm32; C ABI non-dispatchable handle is 8); sizeof(VkInstance)=4 sizeof(PfnVkVoidFunction)=4; Unsafe.SizeOf<VkInstanceCreateInfo>=64 Unsafe.SizeOf<VkPhysicalDeviceProperties>=824; Marshal.SizeOf<VkBufferCreateInfo>=56; VK.MAX_EXTENSION_NAME_SIZE=256 VK.WHOLE_SIZE as ByteSize64=0xFFFFFFFF (C VK_WHOLE_SIZE=~0ULL=0xFFFFFFFFFFFFFFFF); VkResult.Success=0 VkResult.ErrorDeviceLost=-4
[+ 16045ms] [console.log] TEST T06 nativelibrary: PASS TryLoad(vulkan)=False h=0x0; TryLoad(libvulkan.so.1)=False h=0x0; TryLoad(__Internal)=False h=0x0; TryLoad(libSystem.Native)=True h=0x93B68; Load(vulkan, asm, LegacyBehavior)=EX System.DllNotFoundException: vulkan; GetMainProgramHandle()=EX System.ComponentModel.Win32Exception: No error information; TryGetExport(main,malloc)=EX System.ArgumentNullException: ArgumentNull_Generic Arg_ParamName_Name, handle; TryGetExport(main,mono_wasm_load_runtime)=EX System.ArgumentNullException: ArgumentNull_Generic Arg_ParamName_Name, handle; TryGetExport(main,vkGetInstanceProcAddr)=EX System.ArgumentNullException: ArgumentNull_Generic Arg_ParamName_Name, handle
[+ 16046ms] [console.log] TEST T06b dllimport-vulkan-local: FAIL System.DllNotFoundException: vulkan
[+ 16046ms] [console.log] INFO T06c SetDllImportResolver(Brutal.Vulkan, ...) -> ok
[+ 16046ms] [console.log] INFO T06c vkEnumerateInstanceVersion after resolver -> EX System.EntryPointNotFoundException: vkEnumerateInstanceVersion; resolverCalls=1 names=[vulkan;]
[+ 16046ms] [console.log] TEST T06c brutal-libraryimport-vkEnumerateInstanceVersion: FAIL System.DllNotFoundException: vulkan
[+ 16046ms] [console.log] TEST T06d dllimport-known-native-SystemNative_GetPid: FAIL System.EntryPointNotFoundException: SystemNative_GetPid
[+ 16047ms] [console.log] TEST T07a fnptr-ldftn-value (no call): PASS &AddUco=0x0 &MyGetInstanceProcAddr=0x0 &Mixed=0x0 (wasm function-table indices; 0 would mean no native-to-interp thunk)
[+ 16051ms] [console.log] TEST T08c fnptr-js-addFunction: PASS Module.addFunction -> table index 10611; C# calli unmanaged[Cdecl] f(2,3)=302
[+ 16053ms] [console.log] TEST T08d brutal-FunctionTable-from-JS-gipa (JS ICD): PASS JS gipa idx=10613; BRUTAL VkInstance.FunctionTable filled from JS; vkEnumeratePhysicalDevices (JS) -> Success/Success count=1 pd=0x7EB6; jsGipaCalls=105 distinctNames=105 jsEnumCalls=2
[+ 16054ms] [console.log] TEST T08e fnptr-js-trampoline-to-JSExport (managed ICD via JS hop): PASS gipa trampoline idx=10614, enumPD trampoline idx=10615; BRUTAL FunctionTable ctor -> 105 managed gipa lookups (re-entrant C#->JS->C#); vkEnumeratePhysicalDevices -> Success/Success count=1 pd=0xBEEF
[+ 16054ms] [console.log] TEST T11a jsimport-hasWebGpu: PASS navigator.gpu present=True
[+ 16055ms] [console.log] TEST T11b jsimport-span-memoryview: PASS SumBytes(Span<byte>[256]) = 32640 (expected 32640)
[+ 16057ms] [console.log] TEST T11c jsimport-raw-pointer-heap: PASS SumPtr(0x17B3910, 1000) via runtime.localHeapViewU8() = 126516 (expected 126516)
[+ 16057ms] [console.log] TEST T11d jsimport-heap-api-survey: PASS runtime.localHeapViewU8=function runtime.getHeapU8=function runtime.setHeapI32=function Module.HEAPU8=object Module.wasmExports=object Module.wasmExports.memory=[object WebAssembly.Memory] Module.wasmMemory=undefined Module.addFunction=function Module.UTF8ToString=function Module._malloc=function heapBytes=298582016 SharedArrayBuffer-backed=false crossOriginIsolated=false wasmEnableSIMD=true wasmEnableThreads=false wasmEnableEH=true
[+ 16061ms] [console.log] TEST T11e jsimport-async-webgpu-adapter: PASS adapter vendor='apple' architecture='metal-3' device='' description='' features=22 maxBufferSize=4294967292
[+ 16071ms] [console.log] TEST T13a perf-direct-static-call: PASS direct static call: 9.2 ns/call (total 9 ms, acc=500000500000)
[+ 16084ms] [console.log] TEST T13f perf-managed-fnptr (delegate*<int,int,int>): PASS delegate*<int,int,int> (managed calli) -> C# static: 12.1 ns/call (total 12 ms, acc=500000500000)
[+ 16113ms] [console.log] TEST T13b2 perf-fnptr-to-JS-addFunction: PASS delegate* unmanaged[Cdecl] -> JS addFunction (a+b*100): 26.4 ns/call (total 26 ms, acc=500099500000)
[+ 16295ms] [console.log] TEST T13b3 perf-fnptr-to-JS-trampoline-to-JSExport: PASS delegate* unmanaged[Cdecl] -> JS trampoline -> [JSExport] C#: 174.8 ns/call (total 175 ms, acc=500000500000)
[+ 16404ms] [console.log] TEST T13c perf-jsimport-int: PASS JSImport int->int: 103.3 ns/call (total 103 ms, acc=499999500000)
[+ 16555ms] [console.log] TEST T13d perf-jsimport-span64: PASS JSImport Span<byte>[64] as MemoryView (JS reads .byteLength): 144.8 ns/call (total 145 ms, acc=64000000)
[+ 16584ms] [console.log] TEST T13d2 perf-jsimport-span64-copy: PASS JSImport Span<byte>[64] as MemoryView (JS .slice() copy + sum), 100k iters: 220.0 ns/call (total 22 ms, acc=6400000)
[+ 16724ms] [console.log] TEST T13e perf-jsexport (JS->C# [JSExport] int->int, 1M): PASS 134.7 ns/call (total 135 ms, acc=500000500000)
[+ 16872ms] [console.log] TEST T12 jsexport-tick: PASS ticks=10 first=836.2ms last=986.3ms span=150.1ms (~16.68 ms/frame); runtime alive after Main returned
[+ 17000ms] [console.log] INFO T07 calling &AddUco=0x0 ...
[+ 19147ms] [console.log] INFO T08 calling gipa.Invoke=0x0 ...
```

## Notes on method

- A first attempt to publish the verbose variant to `../out/release-verbose` **without** a separate `IntermediateOutputPath` silently reused
  ILLink's cached output from the default publish. Its messages were still resource keys. The final verbose build uses
  `-p:IntermediateOutputPath=obj/ReleaseVerbose/`, and `System.Private.CoreLib` was checked to contain the full message text.
- One `--probe-sigs` invocation exited with code 1 without writing its log. An immediate re-run completed normally, and the cause was not investigated.
  The probe results were identical across the three complete probe runs performed during the session.
