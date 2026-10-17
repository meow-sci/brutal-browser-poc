# exp-06 RESULTS: managed function-pointer flavor of Brutal.Vulkan

Generated 2026-10-16T16:57:45Z from the files in `out/`. Machine: macOS 27.0.1 arm64 (Apple M4 Pro), .NET SDK 10.0.100, no
`wasm-tools` workload, Python 3 (stdlib), Chrome for Testing 151.0.7922.34 and Edge 155.0.4283.45 (both headless).
BRUTAL source at `a25cdf8`, read-only. NuGet: Core/Collections/Memory/Numerics/Strings 2026.10.1, Logging 2026.10.2,
CommunityToolkit.HighPerformance 8.4.2. For the negative check only: Ahwoo.BRUTAL.Vulkan(.Abstractions) 2026.10.2.

## 1. Summary

| Check | Desktop CoreCLR (JIT) | Browser-wasm, Mono interpreter (Chrome 151 x3, Edge 155 x1) |
|---|---|---|
| Managed flavor scenario (`STEP` + `PROBE` lines) | **52/52 PASS** | **52/52 PASS** on all 4 page loads |
| 64-bit enum by value (`VkPipelineStageFlags2` 0x100000002) | intact | intact (exp-01: truncated to 0x2 on the unmanaged calli path) |
| 8-arg (`vkCmdBindDescriptorSets`), 10-arg (`vkCmdPipelineBarrier`), 15-arg (`vkCmdTraceRaysNV`, 7 x `ByteSize64` by value) | intact | intact (exp-01: > 6 args aborts with "CANNOT HANDLE INTERP ICALL SIG") |
| floats by value / `float*` | intact | intact |
| 64-bit struct return (`VkDeviceAddress`) | intact | intact |
| `uint64_t timeout` of `vkWaitForFences` (typed `nint` by BRUTAL) | 0x100000002 | **0x2**: truncated by the C# type before the call. Not a call-path issue. |
| ICD -> app callback (`PfnVkDebugUtilsMessengerCallbackEXT`), app method and Abstractions' `Quiet` | PASS | PASS |
| Managed exception thrown inside the ICD | caught by the caller's `try/catch`; runtime keeps working | same |
| `VkResult` error from the ICD | `VkResultError` thrown by the generated wrapper | same |
| Call through a NULL table entry (`vkCmdDraw`) | **SIGSEGV, exit 139**, not catchable | **Mono assertion `object.c:2033`, runtime abort**, not catchable |
| ILLink (Release publish, trim analysis warnings on) | n/a | 0 warnings. All 41 `&ManagedIcd.*` targets kept, and the published (trimmed) app is the one that passed. |
| Unmanaged flavor from the same projects | exp-04 UCO ICD: `RESULT: OK`; MoltenVK native: `RESULT: OK` | not applicable |

## 2. Build

All builds report 0 warnings and 0 errors.

```
managed: clean build 3.2 s ( 0 Warning(s)  0 Error(s) ); incremental no-op 1.0 s ( 0 Warning(s)  0 Error(s) )
unmanaged: clean build 2.7 s ( 0 Warning(s)  0 Error(s) ); incremental no-op 0.7 s ( 0 Warning(s)  0 Error(s) )
```

* The managed flavor's clean build includes `transform.py`, which takes about 0.3–0.45 s for 2,000 files. On an
  incremental no-op build the script rewrites nothing ("0 written, 1953 unchanged"), and MSBuild reports
  `Skipping target "CoreCompile"` for both projects.
* Browser app: `dotnet build` and `dotnet publish -p:SuppressTrimAnalysisWarnings=false -p:TrimmerSingleWarn=false`
  give 0 warnings (`out/browser-build.log`, `out/browser-publish.log`). The only message is the informational
  "Publishing without optimizations … recommend using `wasm-tools`".

### Compile problems met (none of them in BRUTAL code under the transform)

| Where | Error | Fix |
|---|---|---|
| `src/Brutal.Vulkan/Patched/ImportResolver.cs` (managed flavor, first attempt: `out/build-managed-flavor-first-attempt.log`) | `warning CS0414: The field 'Vulkan.UseProcAddrForGlobalCommands' is assigned but its value is never used` | Field and assignment wrapped in `#if !BRUTAL_MANAGED_FNPTR`. The managed flavor never takes the P/Invoke branch. The native `Dll` field is guarded the same way. |
| `shared/*.cs` (mine) | `CS1061: 'VkPhysicalDeviceProperties2' does not contain a definition for 'SType'` | `sType` is `internal readonly` at `[FieldOffset(0)]`, so it is read through a pointer. |
| `shared/FlavorScenario.cs` (mine) | `IL2026: StackFrame.GetMethod() … RequiresUnreferencedCode` | Replaced with `DiagnosticMethodInfo.Create(frame)`. |
| `apps/DesktopUnmanagedSmoke.csproj` (mine) | `MSB4025: An XML comment cannot contain '--'` | Reworded the comment. |
| User code with R5 off (`-p:BrutalStripUco=false`, `out/build-strip-uco-off.log`) | `CS8786: Calling convention of 'VulkanHelpers.DebugUtilsMessengerCallbacks.Quiet(…)' is not compatible with 'Default'.` | Rule R5 strips `[UnmanagedCallersOnly]`. The Abstractions library itself compiles either way. |

No Brutal.Vulkan or Brutal.Vulkan.Abstractions file failed to compile under the transform. There are no explicit casts
to `delegate* unmanaged`, no `Marshal.GetFunctionPointerForDelegate` and no `[UnmanagedFunctionPointer]` anywhere in
either tree. The only interop-flavoured code in Abstractions is the 3 `[UnmanagedCallersOnly]` debug callbacks and the
`VkDebugUtilsMessengerCallbackEXT` alias, both covered by R1/R5.

## 3. Transform evidence

```
# grep evidence over the generated (transformed) copies, 2026-10-16T16:52:04Z
## src/Brutal.Vulkan/obj/managed/generated/src
files: 1953
grep -rE 'delegate\*\s*unmanaged' -c (files with >0): 0
total 'delegate* unmanaged' occurrences: 0
total 'UnmanagedCallersOnly' occurrences: 0
total 'delegate*<' occurrences: 2441
## src/Brutal.Vulkan.Abstractions/obj/managed/generated/src
files: 47
grep -rE 'delegate\*\s*unmanaged' -c (files with >0): 0
total 'delegate* unmanaged' occurrences: 0
total 'UnmanagedCallersOnly' occurrences: 0
total 'delegate*<' occurrences: 1
## upstream (read-only BRUTAL a25cdf8) for comparison
/Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan: delegate* unmanaged=2442 UnmanagedCallersOnly=0 delegate*<=0
/Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions: delegate* unmanaged=1 UnmanagedCallersOnly=3 delegate*<=0
## patched files (compiled as-is in both flavors; #if BRUTAL_MANAGED_FNPTR inside)
9
6:#if BRUTAL_MANAGED_FNPTR 13:#else 20:#endif 37:#if !BRUTAL_MANAGED_FNPTR 40:#endif 49:#if !BRUTAL_MANAGED_FNPTR 51:#endif 87:#if !BRUTAL_MANAGED_FNPTR 89:#endif 93:#if BRUTAL_MANAGED_FNPTR 100:#else 138:#endif 155:#if !BRUTAL_MANAGED_FNPTR 158:#endif 170:#if !BRUTAL_MANAGED_FNPTR 173:#endif 185:#if !BRUTAL_MANAGED_FNPTR 188:#endif 199:#if !BRUTAL_MANAGED_FNPTR 202:#endif 
## diff upstream vs generated Functions.gen.cs (stat)
2441
```

Per-rule reports: `out/transform-report-Brutal.Vulkan.txt`, `out/transform-report-Brutal.Vulkan.Abstractions.txt`.

## 4. IL evidence (`tools/FnptrInspect`)

| Assembly | fnptr fields | `calli` call sites | P/Invokes | UCO methods |
|---|---|---|---|---|
| NuGet Brutal.Vulkan 2026.10.2 | CDecl = 814 | CDecl = 1606 | 4 | 0 |
| NuGet Brutal.Vulkan.Abstractions 2026.10.2 | – | **CDecl = 2** (`PushConstants`, `BindDescriptorSet`) | 0 | 3 |
| source, unmanaged flavor: Brutal.Vulkan / Abstractions | CDecl = 814 / – | CDecl = 1614 / 2 | 4 / 0 | 0 / 3 |
| source, **managed flavor**: Brutal.Vulkan / Abstractions | **Default = 814** / – | **Default = 1614** / **Default = 2** | 4 / 0 | 0 / **0** |
| browser publish, trimmed: Brutal.Vulkan / Abstractions | Default = 812 / – | Default = 846 / none | **0** / 0 | 0 / 0 |

* 1614 = 1606 generated `.Invoke(` sites + 8 in the patch: 4 `GetInstanceProcAddr(NULL, …)` lookups in the static
  constructor and 4 `GlobalCommands`.
* The managed flavor still contains the 4 `[LibraryImport("vulkan")]` stubs. Nothing references them, and the
  browser trim removes them.
* Full output: `out/fnptr-inspect-desktop.txt`, `out/fnptr-inspect-browser-trimmed.txt`.

## 5. Counts (task 2)

* **814** function-pointer wrapper structs in `Generated/Functions.gen.cs`. Each has `Invoke` and a ctor, and all but
  `PfnVkVoidFunction` (813) have an `explicit operator X(PfnVkVoidFunction)`. They use **632** distinct C#
  signatures.
* Parameter-count histogram: {0: 1, 1: 15, 2: 237, 3: 259, 4: 191, 5: 45, 6: 29, 7: 24, 8: 9, 9: 1, 10: 1, 11: 1, 15: 1}.
  The widest are `vkCmdPipelineBarrier` (10), `vkCmdWaitEvents` (11) and `vkCmdTraceRaysNV` (15).
* 40 signatures take a 64-bit-ish value by value (`ByteSize64`, `VkDeviceAddress`, `*Flags2` enums). 5 take `float` by
  value.
* `.Invoke(` call sites: **1606 in `Brutal.Vulkan/Generated`**. They split into function-table construction (802) and
  wrapper calls (804):

  | File | Sites | Kind |
  |---|---|---|
  | `Handles/VkDevice.gen.cs` | 697 | `getProc.Invoke` lookups in the table constructor |
  | `Handles/VkInstance.gen.cs` | 105 | `getProc.Invoke` lookups in the table constructor |
  | `Extensions/VkDeviceExtensions.gen.cs` | 683 | wrapper calls |
  | `Extensions/VkInstanceExtensions.gen.cs` | 91 | wrapper calls |
  | `Extensions/Platforms/*` | 30 | wrapper calls |

  There are **0 in `Manual/`** (including `Manual/Classes`) and **2 in Abstractions**
  (`CommandBufferExtensions.cs:262`, `DeviceExtensions.cs:592`).

## 6. Desktop, managed flavor (`out/desktop-flavor-smoke.txt`)

```
ENV os=macOS 27.0.1 rid=osx-arm64 framework=.NET 10.0.0 isBrowser=False mono=False nint=64-bit stopwatchHz=1000000000
STEP S00-assemblies PASS Brutal.Vulkan=2026.10.2-exp06-managed-fnptr+BRUTAL.a25cdf8; Brutal.Vulkan.Abstractions=2026.10.2-exp06-managed-fnptr+BRUTAL.a25cdf8
STEP S00-fnptr-type PASS VkCmdDraw.Invoke field type: IsFunctionPointer=True IsUnmanagedFunctionPointer=False
STEP S01-install PASS AppContext[BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR] = 0x10bb8a5f8 (managed &ManagedIcd.GetInstanceProcAddr; ICD implements 41 names)
STEP S02-global-version PASS vkEnumerateInstanceVersion -> 1.3.0 (first touch incl. Vulkan cctor: 1.19 ms)
STEP S02-global-extensions PASS vkEnumerateInstanceExtensionProperties count -> 4
STEP S02-validation PASS VulkanHelpers.Validation.IsSupported -> False (no layers)
STEP S02-gipa-identity PASS Vulkan.GetInstanceProcAddr.Invoke == supplied pointer
STEP S03-instance PASS Vulkan.CreateInstance: handle=0x1000 physicalDevices=1 tableLookups=105 (94 NULL so far) time=4.88 ms (46.52 us/lookup incl. create+enumerate)
STEP S04-properties PASS 'BRUTAL exp-06 managed-fnptr ICD' api=1.3.0 type=VirtualGpu vendor=0x10005
STEP S04-properties2 PASS GetProperties2 -> 'BRUTAL exp-06 managed-fnptr ICD' sType=PhysicalDeviceProperties2KHR
STEP S04-features PASS robustBufferAccess=True shaderInt64=True geometryShader=False
STEP S04-memory-properties PASS types=2 [0]=DeviceLocalBit [1]=HostVisibleBit, HostCoherentBit heaps=2
STEP S04-queue-family PASS FindQueueFamily(Graphics) -> 0
STEP S05-callback-app PASS ICD called the app's managed callback: severity=InfoBitEXT message="exp-06 ICD: debug messenger created" returned=0
STEP S05-callback-abstractions PASS ICD called VulkanHelpers.DebugUtilsMessengerCallbacks.Quiet (UCO attribute stripped by R5): returned=0
STEP S06-device PASS PhysicalDevice.CreateDevice (DeviceEx): handle=0x3000 vkGetDeviceProcAddr lookups=697 (673 NULL) time=20.15 ms (28.91 us/lookup)
STEP S06-queue PASS Device.GetQueue(0,0) -> 0x4000
TIMING create-instance first=4.88 ms warm(no name log)=0.02 ms; create-device first=20.15 ms warm(no name log)=0.08 ms (0.12 us per device-level lookup)
STEP S07-allocate-buffer PASS AllocateBuffer: buffer=0x10002 memory=0x10003 memoryType=1 (HOST_VISIBLE|HOST_COHERENT) allocSize=1 KB
STEP S07-map-write-unmap PASS BufferEx.Map() -> vkMapMemory(offset=0x0, size=0x400) ptr=0x79af005800; wrote 1024 bytes; ICD shadow memory matches=True
PROBE P01-objname PASS vkSetDebugUtilsObjectNameEXT name: sent=exp06-object received=exp06-object
PROBE P01-objname PASS vkSetDebugUtilsObjectNameEXT objectHandle: sent=0x10002 received=0x10002
PROBE P02-bindsets PASS commandBuffer: sent=0x5000 received=0x5000
PROBE P02-bindsets PASS bindPoint: sent=Compute received=Compute
PROBE P02-bindsets PASS layout: sent=0x7001 received=0x7001
PROBE P02-bindsets PASS firstSet/count: sent=(2, 3) received=(2, 3)
PROBE P02-bindsets PASS sets[0]/sets[2]: sent=(32769, 32771) received=(32769, 32771)
PROBE P02-bindsets PASS dynCount/dyn[0]/dyn[1]: sent=(2, 256, 2147483392) received=(2, 256, 2147483392)
PROBE P03-depthbias PASS floats: sent=(1.5, -2.25, 3.125E-07) received=(1.5, -2.25, 3.125E-07)
PROBE P04-blend PASS float[4]: sent=(0.25, 0.5, 0.75, 1.5) received=(0.25, 0.5, 0.75, 1.5)
PROBE P05-drawindexed PASS 5 ints: sent=(36, 2, 3, -4, 5) received=(36, 2, 3, -4, 5)
PROBE P06-drawindirect PASS offset (ByteSize64 by value): sent=0x100000002 received=0x100000002
PROBE P06-drawindirect PASS buffer/drawCount/stride: sent=(65538, 7, 20) received=(65538, 7, 20)
PROBE P07-timestamp2 PASS stage (64-bit enum by value): sent=0x100000002 received=0x100000002
PROBE P07-timestamp2 PASS queryPool/query: sent=(36865, 11) received=(36865, 11)
PROBE P08-deviceaddress PASS returned VkDeviceAddress (64-bit): sent=0xFEED000000010044 received=0xFEED000000010044
PROBE P09-pushconstants PASS layout/stages/offset/size: sent=(28674, VertexBit, FragmentBit, 16, 32) received=(28674, VertexBit, FragmentBit, 16, 32)
PROBE P09-pushconstants PASS payload first float / last word: sent=(1.5, 3405691582) received=(1.5, 3405691582)
PROBE P10-waitforfences PASS timeout (nint, as typed by BRUTAL): sent=0x100000002 received=0x100000002
PROBE P10-waitforfences PASS count/fence0/waitAll/result: sent=(1, 40961, True, Success) received=(1, 40961, True, Success)
NOTE P10 uint64_t timeout typed as nint by BRUTAL: sizeof(nint)=8; 0x100000002 is representable
PROBE P11-mapmemory PASS offset/size (2x ByteSize64 by value): sent=(4294967298, 12884901892) received=(4294967298, 12884901892)
PROBE P11-mapmemory PASS result/ptr!=null: sent=(Success, True) received=(Success, True)
PROBE P12-pipelinebarrier PASS src/dst/dep: sent=(TransferBit, FragmentShaderBit, ByRegionBit) received=(TransferBit, FragmentShaderBit, ByRegionBit)
PROBE P12-pipelinebarrier PASS counts mem/buf/img: sent=(0, 1, 2) received=(0, 1, 2)
PROBE P12-pipelinebarrier PASS bufBarrier[0] buffer/offset/size (64-bit in struct): sent=(65538, 4294967298, 12884901892) received=(65538, 4294967298, 12884901892)
PROBE P12-pipelinebarrier PASS imgBarrier[0] image/newLayout: sent=(49153, TransferDstOptimal) received=(49153, TransferDstOptimal)
PROBE P13-traceraysnv PASS 4 buffer handles: sent=0xB001,0xB002,0xB003,0xB004 received=0xB001,0xB002,0xB003,0xB004
PROBE P13-traceraysnv PASS 7 x ByteSize64 by value: sent=0x100000001,0x200000002,0x300000003,0x400000004,0x500000005,0x600000006,0x700000007 received=0x100000001,0x200000002,0x300000003,0x400000004,0x500000005,0x600000006,0x700000007
PROBE P13-traceraysnv PASS width/height/depth: sent=(1920, 1080, -3) received=(1920, 1080, -3)
STEP S09-vkresult-error PASS VkResultError: Vulkan call to vkAllocateMemory failed with error: VK_ERROR_OUT_OF_DEVICE_MEMORY
STEP S10-icd-exception PASS caught InvalidOperationException "thrown inside ManagedIcd.CmdDispatch(x=0xDEAD, y=2, z=3)"; top frames=[CmdDispatch <- Dispatch <- Run]
STEP S10-after-exception PASS runtime and ICD still usable after the exception (cb.Dispatch(1,1,1) ran)
BENCH baseline: direct static call AddDirect(int,int) [exp-01 T13a shape]: 0.8 ns/call (best of 3 x 1,000,000; runs=0.8/0.9/0.9)
BENCH baseline: delegate*<int,int,int> -> static [exp-01 T13f shape]: 1.0 ns/call (best of 3 x 1,000,000; runs=1.0/1.0/1.0)
BENCH baseline: direct call ManagedIcd.CmdEndRenderPass(cb) (no fnptr): 0.2 ns/call (best of 3 x 1,000,000; runs=0.2/0.9/0.9)
BENCH cb.EndRenderPass() wrapper -> vkCmdEndRenderPass (1 arg, void): 0.9 ns/call (best of 3 x 1,000,000; runs=0.9/0.9/0.9)
BENCH device.PInvoke.vkGetDeviceQueue.Invoke (4 args incl. out ptr): 2.3 ns/call (best of 3 x 1,000,000; runs=2.4/2.3/2.4)
BENCH cb.PInvoke.vkCmdBindDescriptorSets.Invoke raw (8 args): 6.2 ns/call (best of 3 x 1,000,000; runs=6.7/6.2/6.2)
BENCH cb.BindDescriptorSets(...) wrapper (8-arg shape + 2x fixed spans): 6.7 ns/call (best of 3 x 1,000,000; runs=6.7/6.7/6.7)
BENCH cb.SetDepthBias(f,f,f) wrapper (3 floats): 1.9 ns/call (best of 3 x 1,000,000; runs=2.0/2.0/1.9)
BENCH cb.WriteTimestamp2(ulong enum, handle, int) wrapper (64-bit enum): 1.9 ns/call (best of 3 x 1,000,000; runs=2.0/1.9/2.0)
BENCH cb.Dispatch(x,y,z) wrapper (4 args, callee has a throw path): 2.1 ns/call (best of 3 x 1,000,000; runs=2.2/2.1/2.2)
BENCH done: ICD counter advanced by 24,160,000 (expected 24,160,000 = 8 ICD-counting benches x (3 x 1,000,000 + 20,000 warm-up))
STEP S13-dispose PASS FreeAllocation + Device.Dispose + Instance.Dispose
SUMMARY vkGetInstanceProcAddr requests=216 (NULL 188); vkGetDeviceProcAddr requests=1394 (NULL 1346); logged names inst=110 dev=697
ICD call log: (omitted here; see the log file)
RESULT: OK pass=52 fail=0
exit=0
```

ICD call log of the same run, in order:

```
  vkEnumerateInstanceVersion
  vkEnumerateInstanceExtensionProperties(props=NULL, count=0)
  vkEnumerateInstanceLayerProperties
  vkEnumerateInstanceLayerProperties
  vkCreateInstance(apiVersion=1.3.0, extensions=[VK_KHR_portability_enumeration,VK_KHR_get_physical_device_properties2,VK_EXT_debug_utils])
  vkEnumeratePhysicalDevices(count)
  vkEnumeratePhysicalDevices(write)
  vkGetPhysicalDeviceProperties
  vkGetPhysicalDeviceProperties
  vkGetPhysicalDeviceProperties
  vkGetPhysicalDeviceProperties2(sType=PhysicalDeviceProperties2KHR)
  vkGetPhysicalDeviceFeatures
  vkGetPhysicalDeviceMemoryProperties
  vkGetPhysicalDeviceQueueFamilyProperties(count)
  vkGetPhysicalDeviceQueueFamilyProperties(write)
  vkCreateDebugUtilsMessengerEXT(callback=set) -> 0x10000
  vkDestroyDebugUtilsMessengerEXT(0x10000)
  vkCreateDebugUtilsMessengerEXT(callback=set) -> 0x10001
  vkDestroyDebugUtilsMessengerEXT(0x10001)
  vkCreateDevice(queueCreateInfos=1, family=0, priority=1)
  vkCreateInstance(apiVersion=1.3.0, extensions=[VK_KHR_portability_enumeration,VK_KHR_get_physical_device_properties2,VK_EXT_debug_utils])
  vkEnumeratePhysicalDevices(count)
  vkEnumeratePhysicalDevices(write)
  vkCreateDevice(queueCreateInfos=1, family=0, priority=1)
  vkDestroyDevice(0x3000)
  vkDestroyInstance(0x1000)
  vkCreateBuffer(size=1024, usage=TransferDstBit, StorageBufferBit) -> 0x10002
  vkGetBufferMemoryRequirements(0x10002)
  vkGetPhysicalDeviceMemoryProperties
  vkAllocateMemory(size=1024, type=1) -> 0x10003
  vkBindBufferMemory(buffer=0x10002, memory=0x10003, offset=0x0)
  vkMapMemory(memory=0x10003, offset=0x0, size=0x400, flags=0)
  vkUnmapMemory(0x10003)
  vkMapMemory(memory=0x10003, offset=0x100000002, size=0x300000004, flags=0)
  vkUnmapMemory(0x10003)
  vkAllocateMemory(size=64) -> ErrorOutOfDeviceMemory
  vkDestroyBuffer(0x10002)
  vkFreeMemory(0x10003)
  vkDestroyDevice(0x3000)
  vkDestroyInstance(0x1000)
```

## 7. Browser, managed flavor: Chrome for Testing 151, page load 1 (`out/browser-run-chrome-1.txt`)

```
[+   388ms] [driver] browser=Chrome/151.0.7922.34 query=(none) dir=/Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-06-managed-fnptr-flavor/apps/BrowserFlavorSmoke/bin/Release/net10.0/publish/wwwroot
[+   482ms] [http 404] http://127.0.0.1:61178/favicon.ico
Failed to load resource: the server responded with a status of 404 (Not Found)
EXP06 BrowserFlavorSmoke args=[] bench=True iters=1000000 callNull=False
ENV os=Browser rid=browser-wasm framework=.NET 10.0.1 isBrowser=True mono=True nint=32-bit stopwatchHz=1000000000
STEP S00-assemblies PASS Brutal.Vulkan=2026.10.2-exp06-managed-fnptr+BRUTAL.a25cdf8; Brutal.Vulkan.Abstractions=2026.10.2-exp06-managed-fnptr+BRUTAL.a25cdf8
STEP S00-fnptr-type PASS VkCmdDraw.Invoke field type: IsFunctionPointer=True IsUnmanagedFunctionPointer=False
STEP S01-install PASS AppContext[BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR] = 0x179fcc8 (managed &ManagedIcd.GetInstanceProcAddr; ICD implements 41 names)
STEP S02-global-version PASS vkEnumerateInstanceVersion -> 1.3.0 (first touch incl. Vulkan cctor: 2.70 ms)
STEP S02-global-extensions PASS vkEnumerateInstanceExtensionProperties count -> 4
STEP S02-validation PASS VulkanHelpers.Validation.IsSupported -> False (no layers)
STEP S02-gipa-identity PASS Vulkan.GetInstanceProcAddr.Invoke == supplied pointer
STEP S03-instance PASS Vulkan.CreateInstance: handle=0x1000 physicalDevices=1 tableLookups=105 (94 NULL so far) time=5.40 ms (51.43 us/lookup incl. create+enumerate)
STEP S04-properties PASS 'BRUTAL exp-06 managed-fnptr ICD' api=1.3.0 type=VirtualGpu vendor=0x10005
STEP S04-properties2 PASS GetProperties2 -> 'BRUTAL exp-06 managed-fnptr ICD' sType=PhysicalDeviceProperties2KHR
STEP S04-features PASS robustBufferAccess=True shaderInt64=True geometryShader=False
STEP S04-memory-properties PASS types=2 [0]=DeviceLocalBit [1]=HostVisibleBit, HostCoherentBit heaps=2
STEP S04-queue-family PASS FindQueueFamily(Graphics) -> 0
STEP S05-callback-app PASS ICD called the app's managed callback: severity=InfoBitEXT message="exp-06 ICD: debug messenger created" returned=0
STEP S05-callback-abstractions PASS ICD called VulkanHelpers.DebugUtilsMessengerCallbacks.Quiet (UCO attribute stripped by R5): returned=0
STEP S06-device PASS PhysicalDevice.CreateDevice (DeviceEx): handle=0x3000 vkGetDeviceProcAddr lookups=697 (673 NULL) time=13.60 ms (19.51 us/lookup)
STEP S06-queue PASS Device.GetQueue(0,0) -> 0x4000
TIMING create-instance first=5.40 ms warm(no name log)=0.50 ms; create-device first=13.60 ms warm(no name log)=2.90 ms (4.16 us per device-level lookup)
STEP S07-allocate-buffer PASS AllocateBuffer: buffer=0x10002 memory=0x10003 memoryType=1 (HOST_VISIBLE|HOST_COHERENT) allocSize=1 KB
STEP S07-map-write-unmap PASS BufferEx.Map() -> vkMapMemory(offset=0x0, size=0x400) ptr=0x1a7f1e0; wrote 1024 bytes; ICD shadow memory matches=True
PROBE P01-objname PASS vkSetDebugUtilsObjectNameEXT name: sent=exp06-object received=exp06-object
PROBE P01-objname PASS vkSetDebugUtilsObjectNameEXT objectHandle: sent=0x10002 received=0x10002
PROBE P02-bindsets PASS commandBuffer: sent=0x5000 received=0x5000
PROBE P02-bindsets PASS bindPoint: sent=Compute received=Compute
PROBE P02-bindsets PASS layout: sent=0x7001 received=0x7001
PROBE P02-bindsets PASS firstSet/count: sent=(2, 3) received=(2, 3)
PROBE P02-bindsets PASS sets[0]/sets[2]: sent=(32769, 32771) received=(32769, 32771)
PROBE P02-bindsets PASS dynCount/dyn[0]/dyn[1]: sent=(2, 256, 2147483392) received=(2, 256, 2147483392)
PROBE P03-depthbias PASS floats: sent=(1.5, -2.25, 3.125E-07) received=(1.5, -2.25, 3.125E-07)
PROBE P04-blend PASS float[4]: sent=(0.25, 0.5, 0.75, 1.5) received=(0.25, 0.5, 0.75, 1.5)
PROBE P05-drawindexed PASS 5 ints: sent=(36, 2, 3, -4, 5) received=(36, 2, 3, -4, 5)
PROBE P06-drawindirect PASS offset (ByteSize64 by value): sent=0x100000002 received=0x100000002
PROBE P06-drawindirect PASS buffer/drawCount/stride: sent=(65538, 7, 20) received=(65538, 7, 20)
PROBE P07-timestamp2 PASS stage (64-bit enum by value): sent=0x100000002 received=0x100000002
PROBE P07-timestamp2 PASS queryPool/query: sent=(36865, 11) received=(36865, 11)
PROBE P08-deviceaddress PASS returned VkDeviceAddress (64-bit): sent=0xFEED000000010044 received=0xFEED000000010044
PROBE P09-pushconstants PASS layout/stages/offset/size: sent=(28674, VertexBit, FragmentBit, 16, 32) received=(28674, VertexBit, FragmentBit, 16, 32)
PROBE P09-pushconstants PASS payload first float / last word: sent=(1.5, 3405691582) received=(1.5, 3405691582)
PROBE P10-waitforfences PASS timeout (nint, as typed by BRUTAL): sent=0x2 received=0x2
PROBE P10-waitforfences PASS count/fence0/waitAll/result: sent=(1, 40961, True, Success) received=(1, 40961, True, Success)
NOTE P10 uint64_t timeout typed as nint by BRUTAL: sizeof(nint)=4; 0x100000002 is truncated to 0x2 by the C# type (not by the call)
PROBE P11-mapmemory PASS offset/size (2x ByteSize64 by value): sent=(4294967298, 12884901892) received=(4294967298, 12884901892)
PROBE P11-mapmemory PASS result/ptr!=null: sent=(Success, True) received=(Success, True)
PROBE P12-pipelinebarrier PASS src/dst/dep: sent=(TransferBit, FragmentShaderBit, ByRegionBit) received=(TransferBit, FragmentShaderBit, ByRegionBit)
PROBE P12-pipelinebarrier PASS counts mem/buf/img: sent=(0, 1, 2) received=(0, 1, 2)
PROBE P12-pipelinebarrier PASS bufBarrier[0] buffer/offset/size (64-bit in struct): sent=(65538, 4294967298, 12884901892) received=(65538, 4294967298, 12884901892)
PROBE P12-pipelinebarrier PASS imgBarrier[0] image/newLayout: sent=(49153, TransferDstOptimal) received=(49153, TransferDstOptimal)
PROBE P13-traceraysnv PASS 4 buffer handles: sent=0xB001,0xB002,0xB003,0xB004 received=0xB001,0xB002,0xB003,0xB004
PROBE P13-traceraysnv PASS 7 x ByteSize64 by value: sent=0x100000001,0x200000002,0x300000003,0x400000004,0x500000005,0x600000006,0x700000007 received=0x100000001,0x200000002,0x300000003,0x400000004,0x500000005,0x600000006,0x700000007
PROBE P13-traceraysnv PASS width/height/depth: sent=(1920, 1080, -3) received=(1920, 1080, -3)
STEP S09-vkresult-error PASS VkResultError: Vulkan call to vkAllocateMemory failed with error: VK_ERROR_OUT_OF_DEVICE_MEMORY
STEP S10-icd-exception PASS caught InvalidOperationException "thrown inside ManagedIcd.CmdDispatch(x=0xDEAD, y=2, z=3)"; top frames=[CmdDispatch <- Run]
STEP S10-after-exception PASS runtime and ICD still usable after the exception (cb.Dispatch(1,1,1) ran)
BENCH baseline: direct static call AddDirect(int,int) [exp-01 T13a shape]: 21.0 ns/call (best of 3 x 1,000,000; runs=21.3/21.5/21.0)
BENCH baseline: delegate*<int,int,int> -> static [exp-01 T13f shape]: 28.9 ns/call (best of 3 x 1,000,000; runs=29.2/29.0/28.9)
BENCH baseline: direct call ManagedIcd.CmdEndRenderPass(cb) (no fnptr): 2.6 ns/call (best of 3 x 1,000,000; runs=2.8/2.7/2.6)
BENCH cb.EndRenderPass() wrapper -> vkCmdEndRenderPass (1 arg, void): 31.8 ns/call (best of 3 x 1,000,000; runs=31.8/31.9/32.0)
BENCH device.PInvoke.vkGetDeviceQueue.Invoke (4 args incl. out ptr): 41.6 ns/call (best of 3 x 1,000,000; runs=42.2/41.6/42.7)
BENCH cb.PInvoke.vkCmdBindDescriptorSets.Invoke raw (8 args): 36.8 ns/call (best of 3 x 1,000,000; runs=36.8/38.0/38.1)
BENCH cb.BindDescriptorSets(...) wrapper (8-arg shape + 2x fixed spans): 38.9 ns/call (best of 3 x 1,000,000; runs=41.4/38.9/39.4)
BENCH cb.SetDepthBias(f,f,f) wrapper (3 floats): 34.6 ns/call (best of 3 x 1,000,000; runs=35.2/34.6/34.8)
BENCH cb.WriteTimestamp2(ulong enum, handle, int) wrapper (64-bit enum): 34.4 ns/call (best of 3 x 1,000,000; runs=35.1/36.1/34.4)
BENCH cb.Dispatch(x,y,z) wrapper (4 args, callee has a throw path): 40.4 ns/call (best of 3 x 1,000,000; runs=40.4/40.8/41.5)
BENCH done: ICD counter advanced by 24,160,000 (expected 24,160,000 = 8 ICD-counting benches x (3 x 1,000,000 + 20,000 warm-up))
STEP S13-dispose PASS FreeAllocation + Device.Dispose + Instance.Dispose
SUMMARY vkGetInstanceProcAddr requests=216 (NULL 188); vkGetDeviceProcAddr requests=1394 (NULL 1346); logged names inst=110 dev=697
ICD call log: (omitted here; see the log file)
RESULT: OK pass=52 fail=0
EXP06 instance-proc-names BEGIN
EXP06 device-proc-names BEGIN
EXP06 DONE rc=0
EXP06 runMain exitCode=0
[+  1918ms] [driver] finished: DONE
```

* Page loads 2 and 3 and the Edge 155 run give the same 52/52 PASS (`out/browser-run-chrome-2.txt`,
  `out/browser-run-chrome-3.txt`, `out/browser-run-edge.txt`).
* The 110 instance-level and 697 device-level proc-name requests are **byte-identical** between browser and desktop
  (`out/browser-*-proc-names.txt` vs `out/desktop-*-proc-names.txt`).
* The name sequences are identical to exp-04's.

## 8. Benchmarks

Each row is the best of 3 x 1,000,000 calls after a 20,000-call warm-up (exp-01's harness shape). The ICD
counter check (8 counting benches x 3,020,000 = 24,160,000) matched on every run, so no call was elided.

| benchmark | Chrome 151 (3 page loads, min-max) | Edge 155 | browser median (4) | desktop CoreCLR |
|---|---|---|---|---|
| baseline: direct static call AddDirect(int,int) [exp-01 T13a shape] | 21.0-21.8 | 20.5 | 21.3 | 0.8 |
| baseline: delegate*<int,int,int> -> static [exp-01 T13f shape] | 27.4-29.6 | 27.7 | 28.3 | 1.0 |
| baseline: direct call ManagedIcd.CmdEndRenderPass(cb) (no fnptr) | 2.6-2.7 | 2.7 | 2.7 | 0.2 |
| cb.EndRenderPass() wrapper -> vkCmdEndRenderPass (1 arg, void) | 30.4-31.8 | 31.3 | 31.0 | 0.9 |
| device.PInvoke.vkGetDeviceQueue.Invoke (4 args incl. out ptr) | 39.9-41.6 | 40.7 | 40.8 | 2.3 |
| cb.PInvoke.vkCmdBindDescriptorSets.Invoke raw (8 args) | 36.6-37.0 | 36.8 | 36.8 | 6.2 |
| cb.BindDescriptorSets(...) wrapper (8-arg shape + 2x fixed spans) | 38.9-40.3 | 40.6 | 40.0 | 6.7 |
| cb.SetDepthBias(f,f,f) wrapper (3 floats) | 34.0-34.6 | 34.1 | 34.2 | 1.9 |
| cb.WriteTimestamp2(ulong enum, handle, int) wrapper (64-bit enum) | 34.0-34.4 | 34.0 | 34.1 | 1.9 |
| cb.Dispatch(x,y,z) wrapper (4 args, callee has a throw path) | 39.7-40.6 | 41.7 | 40.5 | 2.1 |

Construction timings (`TIMING` lines). In the browser, `performance.now()` is coarsened to 0.1 ms because the page is
not cross-origin isolated, so values are multiples of 0.1 ms.

```
chrome#1 first touch (Vulkan cctor + 4 global lookups + vkEnumerateInstanceVersion): 2.70 ms
chrome#1 create-instance first=5.40 ms warm(no name log)=0.50 ms; create-device first=13.60 ms warm(no name log)=2.90 ms (4.16 us per device-level lookup)
chrome#2 first touch (Vulkan cctor + 4 global lookups + vkEnumerateInstanceVersion): 2.40 ms
chrome#2 create-instance first=5.30 ms warm(no name log)=0.30 ms; create-device first=13.60 ms warm(no name log)=2.90 ms (4.16 us per device-level lookup)
chrome#3 first touch (Vulkan cctor + 4 global lookups + vkEnumerateInstanceVersion): 2.60 ms
chrome#3 create-instance first=5.50 ms warm(no name log)=0.50 ms; create-device first=13.60 ms warm(no name log)=2.90 ms (4.16 us per device-level lookup)
edge#1 first touch (Vulkan cctor + 4 global lookups + vkEnumerateInstanceVersion): 2.60 ms
edge#1 create-instance first=5.40 ms warm(no name log)=0.50 ms; create-device first=13.60 ms warm(no name log)=3.00 ms (4.30 us per device-level lookup)
desktop first touch (Vulkan cctor + 4 global lookups + vkEnumerateInstanceVersion): 1.19 ms
desktop create-instance first=4.88 ms warm(no name log)=0.02 ms; create-device first=20.15 ms warm(no name log)=0.08 ms (0.12 us per device-level lookup)
```

* "first" includes the ICD's per-name string bookkeeping and the first execution of the huge `FunctionTable`
  constructors: 697 lookups for the device table, and for the instance 105 lookups plus `vkCreateInstance` and 2 x
  `vkEnumeratePhysicalDevices`.
* "warm" is a second `Instance`/`DeviceEx` with logging off.

**Comparison with exp-01 / exp-04** (same machine and browser):

| Path | ns/call |
|---|---|
| exp-01 warmed full suite: direct static call / `delegate*<int,int,int>` / `[JSImport]` int→int | 9.2 / 12.1 / 103.3 |
| exp-01 isolated runs (console.perf.log): direct / managed fnptr / unmanaged fnptr → JS `addFunction` / unmanaged fnptr → JS trampoline → `[JSExport]` C# / `[JSImport]` int→int | 22.8 / 30.6 / 45.9 / 191.3 / 154.3 |
| exp-06 in-run baselines (median of 4 page loads): direct / `delegate*<int,int,int>` | 21.3 / 28.3 |
| exp-06 Brutal.Vulkan wrapper → managed ICD (median) | 31.0 (1 arg) · 34.2 (3 floats) · 34.1 (64-bit enum) · 36.8 raw / 40.0 wrapper (8 args) · 40.8 (4 args + out pointer) |
| exp-04 device creation through JS trampolines (697 lookups, C#→JS→C# each) | ~19 ms, ~27 µs/lookup (includes ICD logging) |
| exp-06 device creation through managed fnptrs | 13.6 ms first (with logging), 2.9–3.0 ms warm (≈4.2 µs/lookup) |

The exp-06 baselines agree with exp-01's *isolated* runs (21.3 vs 22.8, 28.3 vs 30.6). exp-01's 9.2/12.1 came from a
long warmed suite. The meaningful comparison is relative:

* A BRUTAL wrapper call into the managed ICD costs about 1.1–1.5x a bare `delegate*<int,int,int>` call.
* It is about 4–5x cheaper than one `[JSImport]` call (154 ns isolated).
* It is about 5–6x cheaper than exp-04's unmanaged-fnptr → JS trampoline → `[JSExport]` route (191 ns).

## 9. Robustness

### 9a. NULL table entry
Desktop (`out/desktop-flavor-call-null.txt`):
```
STEP S10-icd-exception PASS caught InvalidOperationException "thrown inside ManagedIcd.CmdDispatch(x=0xDEAD, y=2, z=3)"; top frames=[CmdDispatch <- Dispatch <- Run]
STEP S10-after-exception PASS runtime and ICD still usable after the exception (cb.Dispatch(1,1,1) ran)
CALLNULL vkCmdDraw table entry is NULL: True; calling cb.Draw(3,1,0,0) inside try/catch ...
exit=139
```
Browser (`out/browser-run-callnull.txt`):
```
EXP06 BrowserFlavorSmoke args=[bench=0,callnull=1] bench=False iters=1000000 callNull=True
STEP S10-icd-exception PASS caught InvalidOperationException "thrown inside ManagedIcd.CmdDispatch(x=0xDEAD, y=2, z=3)"; top frames=[CmdDispatch <- Run]
STEP S10-after-exception PASS runtime and ICD still usable after the exception (cb.Dispatch(1,1,1) ran)
CALLNULL vkCmdDraw table entry is NULL: True; calling cb.Draw(3,1,0,0) inside try/catch ...
[MONO] * Assertion at /__w/1/s/src/runtime/src/mono/mono/metadata/object.c:2033, condition `<disabled>' not met
[+   549ms] [pageerror] ExitStatus: ExitStatus
[+  1550ms] [driver] finished: CRASH
```

* In the Mono interpreter a managed function pointer is the callee's `InterpMethod*` (`ftnptr_to_imethod`,
  `interp.c`). `MINT_CALLI` does not check it for NULL.
* Wasm does not trap on loads from address 0, so execution goes on with garbage until `g_assert (klass)` in
  `mono_class_vtable_checked` (`metadata/object.c:2033` on `release/10.0`). The runtime then aborts with
  `ExitStatus`.
* Neither host raises a catchable exception.

### 9b. Managed exception inside the ICD
`STEP S10-icd-exception PASS` on both hosts. `InvalidOperationException` thrown in `ManagedIcd.CmdDispatch`
propagates through the managed `calli` and the generated `Dispatch<T>` wrapper to the caller's `catch`. Top frames:

* desktop: `CmdDispatch <- Dispatch <- Run`
* browser: `CmdDispatch <- Run` (the wrapper was inlined)

`S10-after-exception` confirms the runtime and the ICD keep working afterwards.

### 9c. VkResult errors
`STEP S09-vkresult-error PASS` on both hosts. The ICD returns `VK_ERROR_OUT_OF_DEVICE_MEMORY`, and the generated
wrapper throws `VkResultError`.

### 9d. Trimming and published size (`out/browser-published-sizes.txt`)
```
# published browser-wasm sizes (bytes), clean Release publish (ILLink trim), 2026-10-16T16:55:57Z
581397 Brutal.Vulkan.myyab7qiwl.wasm
140716 Brutal.Vulkan.myyab7qiwl.wasm.br
200385 Brutal.Vulkan.myyab7qiwl.wasm.gz
24341 Brutal.Vulkan.Abstractions.wxq03jmbxb.wasm
9250 Brutal.Vulkan.Abstractions.wxq03jmbxb.wasm.br
10671 Brutal.Vulkan.Abstractions.wxq03jmbxb.wasm.gz
54549 BrowserFlavorSmoke.tco0h1l7uj.wasm
21211 BrowserFlavorSmoke.tco0h1l7uj.wasm.br
24680 BrowserFlavorSmoke.tco0h1l7uj.wasm.gz
3001957 dotnet.native.muve7a13r4.wasm
976573 dotnet.native.muve7a13r4.wasm.br
1207839 dotnet.native.muve7a13r4.wasm.gz
linked IL (obj/Release/net10.0/linked): 581632 Brutal.Vulkan.dll 24576 Brutal.Vulkan.Abstractions.dll 
untrimmed managed flavor (src/*/bin/managed): 1561088 Brutal.Vulkan.dll 125952 Brutal.Vulkan.Abstractions.dll 
publish/wwwroot total: 13400 KiB; _framework file count: 72
```

* exp-04's unmanaged flavor was 573 KB trimmed (140 KB brotli). The managed flavor is the same size.
* Abstractions shrinks to 24 KB because the app uses only `DeviceEx`, `BufferEx`, `VulkanHelpers` and
  `PhysicalDeviceExtensions`.
* All 41 ICD `ldftn` targets survive ILLink (`out/fnptr-inspect-browser-trimmed.txt`).

## 10. Unmanaged flavor from the same projects

`DesktopUnmanagedSmoke --uco` (exp-04 UCO FakeIcd and scenario, both libraries source-built in the unmanaged flavor),
from `out/desktop-unmanaged-uco.txt`:
```
exp-06 DesktopUnmanagedSmoke: Brutal.Vulkan=2026.10.2-exp06-unmanaged-fnptr+BRUTAL.a25cdf8; Abstractions=2026.10.2-exp06-unmanaged-fnptr+BRUTAL.a25cdf8; VkCmdDraw.Invoke IsUnmanagedFunctionPointer=True
OS: macOS 27.0.1 / osx-arm64; IsBrowser=False; nint=64-bit
AppContext[BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR] = 0x109ec28b0
Brutal.Vulkan: Brutal.Vulkan, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null; informational=2026.10.2-exp06-unmanaged-fnptr+BRUTAL.a25cdf8
Brutal.Vulkan.Abstractions: Brutal.Vulkan.Abstractions, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null; informational=2026.10.2-exp06-unmanaged-fnptr+BRUTAL.a25cdf8
vkEnumerateInstanceVersion -> 1.3.0
vkEnumerateInstanceExtensionProperties count -> 3
Abstractions VulkanHelpers.Validation.IsSupported -> False
Vulkan.GetInstanceProcAddr.Invoke == supplied pointer: True
Instance created: handle=0x1000; physical devices=1
  PhysicalDevice 0x2000: 'BRUTAL exp-04 managed fake ICD' api=1.3.0 type=VirtualGpu
FindQueueFamily(Graphics) -> 0
DeviceEx created: handle=0x3000
Queue: handle=0x4000
AllocateBuffer: buffer=0x10000 memory=0x10001 memoryType=0 size=1 KB
Disposed device + instance.
vkGetInstanceProcAddr requests: 110 (98 answered NULL); after instance ctor: 109
vkGetDeviceProcAddr requests: 697 (689 answered NULL)
ICD call log:
  vkEnumerateInstanceVersion
  vkEnumerateInstanceExtensionProperties(layer=NULL, props=NULL, count=0)
  vkEnumerateInstanceLayerProperties(props=NULL, count=0)
  vkEnumerateInstanceLayerProperties(props=NULL, count=0)
  vkCreateInstance(flags=EnumeratePortabilityBitKHR, apiVersion=1.3.0, extensions=[VK_KHR_portability_enumeration, VK_KHR_get_physical_device_properties2], layers=0)
  vkEnumeratePhysicalDevices(devices=NULL, count=0)
  vkEnumeratePhysicalDevices(devices=buf, count=1)
  vkGetPhysicalDeviceProperties
  vkGetPhysicalDeviceProperties
  vkGetPhysicalDeviceQueueFamilyProperties(props=NULL)
  vkGetPhysicalDeviceQueueFamilyProperties(props=buf)
  vkCreateDevice(queueCreateInfos=1, family=0, extensions=0)
  vkGetDeviceQueue(family=0, index=0)
  vkCreateBuffer(size=1024, usage=TransferDstBit, StorageBufferBit) -> 0x10000
  vkGetBufferMemoryRequirements(0x10000)
  vkGetPhysicalDeviceMemoryProperties
  vkAllocateMemory(size=1024, type=0) -> 0x10001
  vkBindBufferMemory(buffer=0x10000, memory=0x10001, offset=0)
  vkDestroyBuffer(0x10000)
  vkFreeMemory(0x10001)
  vkDestroyDevice(0x3000)
  vkDestroyInstance(0x1000)
RESULT: OK
exit=0
```
`DesktopUnmanagedSmoke --native` (MoltenVK, the `#else` native-loader branch of the patched `ImportResolver.cs`),
from `out/desktop-unmanaged-native-moltenvk.txt`:
```
exp-06 DesktopUnmanagedSmoke: Brutal.Vulkan=2026.10.2-exp06-unmanaged-fnptr+BRUTAL.a25cdf8; Abstractions=2026.10.2-exp06-unmanaged-fnptr+BRUTAL.a25cdf8; VkCmdDraw.Invoke IsUnmanagedFunctionPointer=True
OS: macOS 27.0.1 / osx-arm64
env VULKAN_SDK=<unset>
env VK_ICD_FILENAMES=<unset>
env VK_DRIVER_FILES=<unset>
env DYLD_LIBRARY_PATH=:/Users/asherwin/VulkanSDK/1.4.328.1/macOS/lib
env VK_ADD_LAYER_PATH=<unset>
AppContext[BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR]=<unset>
Brutal.Vulkan: Brutal.Vulkan, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null; informational=2026.10.2-exp06-unmanaged-fnptr+BRUTAL.a25cdf8; location=/Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-06-managed-fnptr-flavor/apps/DesktopUnmanagedSmoke/bin/Release/net10.0/Brutal.Vulkan.dll
Brutal.Vulkan.Abstractions: Brutal.Vulkan.Abstractions, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null; informational=2026.10.2-exp06-unmanaged-fnptr+BRUTAL.a25cdf8
vkEnumerateInstanceVersion -> 1.4.328
vkGetInstanceProcAddr (native) = 0x109ee9044
Instance extensions (18): VK_KHR_device_group_creation, VK_KHR_external_fence_capabilities, VK_KHR_external_memory_capabilities, VK_KHR_external_semaphore_capabilities, VK_KHR_get_physical_device_properties2, VK_KHR_get_surface_capabilities2, VK_KHR_surface, VK_KHR_surface_protected_capabilities, VK_EXT_debug_report, VK_EXT_debug_utils, VK_EXT_headless_surface, VK_EXT_layer_settings, VK_EXT_metal_surface, VK_EXT_surface_maintenance1, VK_EXT_swapchain_colorspace, VK_MVK_macos_surface, VK_KHR_portability_enumeration, VK_LUNARG_direct_driver_loading
Abstractions VulkanHelpers.Validation.IsSupported -> True
Instance created: handle=0x78ff234000; physical devices=1
Instance table: vkCreateDevice=set, vkCreateMetalSurfaceEXT=NULL, vkCreateWin32SurfaceKHR=NULL
  PhysicalDevice: 'Apple M4 Pro' api=1.3.323 driver=0.2.2208 vendor=0x106b type=IntegratedGpu
Graphics queue family=0; device extensions=128; VK_KHR_portability_subset=True
DeviceEx created: handle=0x78ff8b9818
Queue: handle=0x78fedb4658
Device table: vkCreateBuffer=set, vxDestroyBuffers=NULL, vxDestroyImages=NULL
AllocateBuffer: buffer=0x78ff097e40 memory=0x78fed157c0 memoryType=1 allocSize=1 KB
Disposed device + instance.
RESULT: OK
exit=0
```

## 11. Negative check: managed-flavor Brutal.Vulkan with the NuGet Abstractions

From `out/desktop-nuget-abstractions-mismatch.txt`:

```
Brutal.Vulkan=2026.10.2-exp06-managed-fnptr+BRUTAL.a25cdf8
Brutal.Vulkan.Abstractions=2026.10.2+5dddc87ebb3f97c7ad825df4086f08e8aa99de63 (/Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-06-managed-fnptr-flavor/apps/DesktopNuGetAbstractionsMismatch/bin/Release/net10.0/Brutal.Vulkan.Abstractions.dll)
OK      Vulkan.CreateInstance (Brutal.Vulkan only): physicalDevices=1
OK      PhysicalDevice.CreateDevice (NuGet Abstractions DeviceEx)
OK      DeviceEx.AllocateBuffer + FreeAllocation (NuGet Abstractions -> Brutal.Vulkan extension methods)
THROWS  CommandBufferExtensions.PushConstants<float> (NuGet Abstractions IL: ldfld VkCmdPushConstants::Invoke + calli unmanaged cdecl): System.MissingFieldException: Field not found: 'Brutal.VulkanApi.VkCmdPushConstants.Invoke'.
        ICD saw vkCmdPushConstants size=0 firstFloat=0
DONE
exit=0
```

* Everything that the NuGet Abstractions routes through Brutal.Vulkan's extension methods keeps working.
* The 2 Abstractions methods that `ldfld` a wrapper's `Invoke` field and `calli` it themselves fail at JIT time with
  `MissingFieldException`. The field's signature type changed from `method unmanaged cdecl …` to `method …`.
* The 3 UCO callbacks cannot be used as managed function pointers (CS8786).
* Hence Abstractions must be source-built with the same transform.
