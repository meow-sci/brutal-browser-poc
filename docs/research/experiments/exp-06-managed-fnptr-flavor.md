---
type: Experiment Report
title: "exp-06: managed function-pointer flavor of Brutal.Vulkan + Abstractions (C# ICD called through delegate*<…> on desktop and browser-wasm)"
description: Builds Brutal.Vulkan and Brutal.Vulkan.Abstractions from the read-only BRUTAL source with every delegate* unmanaged[Cdecl] rewritten to a managed delegate*, and shows that a plain-static-method C# Vulkan ICD then runs under BRUTAL's own bindings on CoreCLR and on .NET 10 browser-wasm (no wasm-tools, no UnmanagedCallersOnly, no JS trampolines), with all signature shapes intact, catchable ICD exceptions and ~31–41 ns per Vulkan call in the interpreter.
tags: [experiment, brutal, brutal-vulkan, function-pointers, managed-fnptr, icd, browser-wasm, mono, interpreter, source-build, transform, generator, trimming, performance]
sources:
  - id: project
    resource: ../../../projects/exp-06-managed-fnptr-flavor/README.md
    title: exp-06 project README (layout, transform rules, build/run commands)
  - id: results
    resource: ../../../projects/exp-06-managed-fnptr-flavor/RESULTS.md
    title: exp-06 RESULTS (summary table, build problems, IL evidence, verbatim desktop/browser logs, benchmarks, robustness)
  - id: transform
    resource: ../../../projects/exp-06-managed-fnptr-flavor/tools/transform.py
    title: tools/transform.py (rules R1–R5, incremental writes, leftover check)
  - id: targets
    resource: ../../../projects/exp-06-managed-fnptr-flavor/src/Directory.Build.targets
    title: BrutalManagedFnptrTransform MSBuild target (Exec python3 -I, dynamic Compile items, BRUTAL_MANAGED_FNPTR define)
  - id: props
    resource: ../../../projects/exp-06-managed-fnptr-flavor/src/Directory.Build.props
    title: BrutalManagedFnptr switch and per-flavor obj/bin split
  - id: csproj-abstractions
    resource: ../../../projects/exp-06-managed-fnptr-flavor/src/Brutal.Vulkan.Abstractions/Brutal.Vulkan.Abstractions.csproj
    title: Reconstructed Brutal.Vulkan.Abstractions.csproj
  - id: patched-resolver
    resource: ../../../projects/exp-06-managed-fnptr-flavor/src/Brutal.Vulkan/Patched/ImportResolver.cs
    title: Patched ImportResolver.cs, one source for both flavors (#if BRUTAL_MANAGED_FNPTR)
  - id: patch
    resource: ../../../projects/exp-06-managed-fnptr-flavor/patches/brutal-vulkan-pluggable-loader-both-flavors.patch
    title: Unified diff against BRUTAL a25cdf8 (git apply --check OK)
  - id: icd
    resource: ../../../projects/exp-06-managed-fnptr-flavor/shared/ManagedIcd.cs
    title: Managed ICD (41 static entry points, &Method proc-addr table, argument probes)
  - id: scenario
    resource: ../../../projects/exp-06-managed-fnptr-flavor/shared/FlavorScenario.cs
    title: Scenario run by the desktop and browser apps (STEP/PROBE/BENCH/TIMING)
  - id: out-transform
    resource: ../../../projects/exp-06-managed-fnptr-flavor/out/transform-evidence.txt
    title: grep evidence over the generated copies (0 delegate* unmanaged, 0 UnmanagedCallersOnly)
  - id: out-il
    resource: ../../../projects/exp-06-managed-fnptr-flavor/out/fnptr-inspect-desktop.txt
    title: FnptrInspect over NuGet, unmanaged-flavor and managed-flavor assemblies (field and calli calling conventions)
  - id: out-il-trimmed
    resource: ../../../projects/exp-06-managed-fnptr-flavor/out/fnptr-inspect-browser-trimmed.txt
    title: FnptrInspect over the ILLink output of the browser publish (ldftn targets kept)
  - id: out-desktop
    resource: ../../../projects/exp-06-managed-fnptr-flavor/out/desktop-flavor-smoke.txt
    title: DesktopFlavorSmoke console output (CoreCLR, 52/52 PASS, benchmarks)
  - id: out-desktop-null
    resource: ../../../projects/exp-06-managed-fnptr-flavor/out/desktop-flavor-call-null.txt
    title: DesktopFlavorSmoke --call-null (exit 139)
  - id: out-browser
    resource: ../../../projects/exp-06-managed-fnptr-flavor/out/browser-run-chrome-1.txt
    title: Headless Chrome for Testing 151 run of the published BrowserFlavorSmoke (52/52 PASS)
  - id: out-browser-edge
    resource: ../../../projects/exp-06-managed-fnptr-flavor/out/browser-run-edge.txt
    title: Headless Edge 155 run (52/52 PASS)
  - id: out-browser-null
    resource: ../../../projects/exp-06-managed-fnptr-flavor/out/browser-run-callnull.txt
    title: Browser run calling a NULL table entry (Mono assertion object.c:2033)
  - id: out-bench
    resource: ../../../projects/exp-06-managed-fnptr-flavor/out/bench-summary.txt
    title: Benchmark and construction-timing summary (3 Chrome page loads, 1 Edge, desktop)
  - id: out-sizes
    resource: ../../../projects/exp-06-managed-fnptr-flavor/out/browser-published-sizes.txt
    title: Published browser-wasm sizes (webcil, gzip, brotli) and linked IL sizes
  - id: out-unmanaged-uco
    resource: ../../../projects/exp-06-managed-fnptr-flavor/out/desktop-unmanaged-uco.txt
    title: Unmanaged flavor + exp-04 UCO FakeIcd (RESULT OK)
  - id: out-unmanaged-native
    resource: ../../../projects/exp-06-managed-fnptr-flavor/out/desktop-unmanaged-native-moltenvk.txt
    title: Unmanaged flavor on MoltenVK (Apple M4 Pro, RESULT OK)
  - id: out-mismatch
    resource: ../../../projects/exp-06-managed-fnptr-flavor/out/desktop-nuget-abstractions-mismatch.txt
    title: Managed-flavor Brutal.Vulkan with the NuGet Abstractions (MissingFieldException)
  - id: out-uco-off
    resource: ../../../projects/exp-06-managed-fnptr-flavor/out/build-strip-uco-off.log
    title: Build with rule R5 disabled (CS8786 at the user's &Quiet)
  - id: out-build
    resource: ../../../projects/exp-06-managed-fnptr-flavor/out/build-timings.txt
    title: Clean and incremental build timings of both flavors
  - id: exp01
    resource: /experiments/exp-01-dotnet-wasm-smoke.md
    title: "exp-01 report (UCO pointers are 0, interp→native calli signature limits, 64-bit enum truncation, per-call costs)"
  - id: exp01-perf
    resource: ../../../projects/exp-01-dotnet-wasm-smoke/test/console.perf.log
    title: exp-01 isolated perf runs (direct 22.8, managed fnptr 30.6, JS trampoline 191.3, JSImport 154.3 ns)
  - id: exp04
    resource: /experiments/exp-04-brutal-vulkan-source-build.md
    title: exp-04 report (reconstructed csproj, pluggable-loader patch, JS-trampoline ICD in the browser)
  - id: fnptr-note
    resource: /dotnet-wasm/function-pointers-and-native-callbacks.md
    title: Function pointers, UnmanagedCallersOnly and native resolution on .NET 10 browser-wasm (option B = managed function pointers)
  - id: brutal-functions
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Functions.gen.cs
    title: BRUTAL Functions.gen.cs (814 function-pointer wrapper structs, 2441 delegate* unmanaged[Cdecl])
  - id: brutal-vkhelpers
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/VulkanHelpers.cs
    title: Abstractions VulkanHelpers.cs (VkDebugUtilsMessengerCallbackEXT alias, 3 [UnmanagedCallersOnly] callbacks)
  - id: brutal-abs-calli
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/Extensions/CommandBufferExtensions.cs
    title: Abstractions CommandBufferExtensions.PushConstants (direct PInvoke.vkCmdPushConstants.Invoke; DeviceExtensions.BindDescriptorSet likewise)
  - id: brutal-abs-usings
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/GlobalUsings.cs
    title: Abstractions GlobalUsings.cs (Collections, Logging, Memory, Numerics, Pointers, Strings, CommunityToolkit.HighPerformance)
  - id: brutal-device-ext
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Extensions/VkDeviceExtensions.gen.cs
    title: Generated device wrappers (e.g. WaitForFences(…, nint timeout))
  - id: brutal-props2
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan/Generated/Structs/VkPhysicalDeviceProperties2.gen.cs
    title: Example generated struct with [StructLayout(Explicit, Size = 840)] and 64-bit field offsets
  - id: brutal-vma
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Vma/Manual/VmaDeviceExtensions.cs
    title: Brutal.Vulkan.Vma hands Vulkan.GetInstanceProcAddr / vkGetDeviceProcAddr to native VMA
  - id: mono-interp
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/mono/mini/interp/interp.c
    title: Mono interpreter release/10.0 — ftnptr_to_imethod and MINT_CALLI (managed fnptr = InterpMethod*, no NULL check)
  - id: mono-object
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/mono/metadata/object.c
    title: Mono object.c release/10.0 — line 2033 is g_assert (klass) in mono_class_vtable_checked
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:59:12Z }
status: draft
---

# Purpose

exp-01 and exp-04 established two facts about stock .NET 10 browser-wasm, which runs the Mono interpreter with no
`wasm-tools` workload:[^exp01][^exp04]

* BRUTAL's `delegate* unmanaged[Cdecl]` function tables cannot point at C# methods. `&UnmanagedCallersOnlyMethod`
  is 0.
* JS trampolines work, but only for the interp→native `calli` signatures that the prebuilt runtime has trampolines
  for. 64-bit enums passed by value are truncated, and every call makes a C#→JS→C# round trip.

[/dotnet-wasm/function-pointers-and-native-callbacks.md](/dotnet-wasm/function-pointers-and-native-callbacks.md)
proposed "option B": a source build in which the tables hold **managed** `delegate*<…>`, so that calls stay inside
the interpreter.[^fnptr-note] This experiment builds that flavor and checks it on desktop and in the browser. It
asks four questions:

1. Can the flavor be produced mechanically from the read-only BRUTAL tree, and what does it take?
2. Does a C# ICD made of plain static methods then work under BRUTAL's own bindings: instance, device, Abstractions
   allocation and mapping, every argument shape, and callbacks?
3. What does a call cost, and how robust is the flavor (NULL entries, exceptions, trimming)?
4. Can the same projects still produce the upstream (unmanaged) flavor for desktop?

Related: [/experiments/exp-01-dotnet-wasm-smoke.md](/experiments/exp-01-dotnet-wasm-smoke.md),
[/experiments/exp-04-brutal-vulkan-source-build.md](/experiments/exp-04-brutal-vulkan-source-build.md).

# Setup

* **Machine and toolchain.** macOS 27.0.1 arm64 (Apple M4 Pro), .NET SDK 10.0.100, no `wasm-tools` workload,
  `python3 -I`, standard library only.
* **Browsers.** Chrome for Testing 151 and Edge 155, both `--headless=new`, driven by puppeteer-core from exp-01's
  `node_modules`.
* **Sources.** BRUTAL `a25cdf8` (read-only). NuGet Core/Collections/Memory/Numerics/Strings 2026.10.1, Logging
  2026.10.2 and CommunityToolkit.HighPerformance 8.4.2.
* **Projects.** `projects/exp-06-managed-fnptr-flavor/`.[^project]
  * `src/Brutal.Vulkan` reuses exp-04's reconstructed csproj and its pluggable-loader patch.
  * `src/Brutal.Vulkan.Abstractions` is a new reconstructed csproj.[^csproj-abstractions] Its dependencies come from
    `GlobalUsings.cs` and the 2026.10.2 nuspec: Core, Collections, Logging, Memory, Numerics, Strings,
    CommunityToolkit.HighPerformance, `ImplicitUsings`, and a `DisableRuntimeMarshalling` assembly attribute.[^brutal-abs-usings]
* **Flavor switch.** The MSBuild property `BrutalManagedFnptr`, default `false`.[^props]
  * `obj/` and `bin/` are split per flavor, so both flavors build side by side from the same projects.
  * Apps choose a flavor through `ProjectReference … AdditionalProperties="BrutalManagedFnptr=true"`.
  * With `true`, the target `BrutalManagedFnptrTransform` runs `python3 -I tools/transform.py` before `BeforeCompile`.
    It compiles the copies in `obj/managed/generated/src/`, together with the patched files, and defines
    `BRUTAL_MANAGED_FNPTR`.[^targets]
* **Apps.**
  * `DesktopFlavorSmoke` and `BrowserFlavorSmoke` run the managed flavor with `shared/ManagedIcd.cs`.[^icd][^scenario]
  * `DesktopUnmanagedSmoke` runs the unmanaged flavor with exp-04's UCO ICD, or with MoltenVK.
  * `DesktopNuGetAbstractionsMismatch` is a negative check.

# Transform rules

`tools/transform.py` copies `**/*.cs` and applies these rules in order. It writes only files whose content changed,
so builds stay incremental, and it fails if anything matching `delegate\*\s*unmanaged` or `UnmanagedCallersOnly`
remains.[^transform]

| Rule | Pattern | Replacement | Brutal.Vulkan | Abstractions |
|---|---|---|---|---|
| R1 | `delegate\*\s*unmanaged\s*\[\s*Cdecl\s*\]\s*<` | `delegate*<` | **2441**, all in `Generated/Functions.gen.cs` | **1**, the `VkDebugUtilsMessengerCallbackEXT` alias in `VulkanHelpers.cs` |
| R2 | `…unmanaged\s*\[\s*Stdcall\s*\]\s*<` | `delegate*<` | 0 | 0 |
| R3 | `…unmanaged\s*\[[^\]]*\]\s*<` (any other list) | `delegate*<` | 0 | 0 |
| R4 | `delegate\*\s*unmanaged\s*<` | `delegate*<` | 0 | 0 |
| R5 | `[UnmanagedCallersOnly(…)]` (line or inline, nested parens) | removed | 0 | **3** (`DebugUtilsMessengerCallbacks.Quiet/Default/Verbose`) |

Notes on the counts:[^out-transform][^brutal-functions][^brutal-vkhelpers]

* The upstream tree has 2442 `delegate* unmanaged` occurrences. The 2442nd is in `ImportResolver.cs`, which is
  replaced by the patched copy.
* In the generated copies (1953 + 47 files), `grep` finds **0** `delegate* unmanaged` and **0**
  `UnmanagedCallersOnly`.
* `Functions.gen.cs` differs from upstream in exactly 2441 lines.

**What compiled and what didn't:**

* Every BRUTAL file compiled under the transform on the first attempt.
* Neither tree contains explicit casts to `delegate* unmanaged`, `Marshal.GetFunctionPointerForDelegate` or
  `[UnmanagedFunctionPointer]`.
* The only fix needed was in my own patched `ImportResolver.cs`, for `CS0414` (a flag left unused in the managed
  branch). It was solved with `#if !BRUTAL_MANAGED_FNPTR`.
* R5 is needed for *consumers*, not for the library. With R5 off, Abstractions still compiles. But user code that
  passes `&VulkanHelpers.DebugUtilsMessengerCallbacks.Quiet` to the now-managed `PfnVkDebugUtilsMessengerCallbackEXT`
  fails with `CS8786: Calling convention … is not compatible with 'Default'`.[^out-uco-off]

**The patch for both flavors.** It is the exp-04 loader patch, re-cut as one source for both flavors: +157/−4
lines against upstream, and `git apply --check` passes.[^patch][^patched-resolver]

* `#if BRUTAL_MANAGED_FNPTR` selects `using unsafe` aliases for the 6 function-pointer types it needs (managed or
  `unmanaged[Cdecl]`).
* In the managed flavor it drops the native-library branch. With no AppContext key it throws
  `PlatformNotSupportedException`.
* The AppContext value is cast to `delegate*<VkInstance, byte*, PfnVkVoidFunction>`.

**Build cost.**[^out-build]

* Clean build: managed flavor 3.2 s, unmanaged 2.7 s.
* No-op incremental build: managed 1.0 s, unmanaged 0.7 s. `CoreCompile` is skipped in both projects.

**IL evidence** from `tools/FnptrInspect`:[^out-il]

| Assembly | function-pointer fields | `calli` call sites | `[UnmanagedCallersOnly]` |
|---|---|---|---|
| NuGet Brutal.Vulkan 2026.10.2 | `unmanaged cdecl` = 814 | `unmanaged cdecl` = 1606 | 0 |
| NuGet Brutal.Vulkan.Abstractions 2026.10.2 | – | `unmanaged cdecl` = **2** | 3 |
| exp-06 managed flavor: Brutal.Vulkan / Abstractions | **managed = 814** / – | **managed = 1614** / **managed = 2** | 0 / **0** |
| exp-06 unmanaged flavor: Brutal.Vulkan / Abstractions | `unmanaged cdecl` = 814 / – | `unmanaged cdecl` = 1614 / 2 | 0 / 3 |

In the browser publish, ILLink trims Brutal.Vulkan to 812 managed fields, 846 managed `calli` sites and **0**
P/Invokes.[^out-il-trimmed]

**Counts (task 2).**

* There are **814** distinct function-pointer wrapper structs, using 632 distinct C# signatures.
* Parameter counts reach 10 (`vkCmdPipelineBarrier`), 11 (`vkCmdWaitEvents`) and 15 (`vkCmdTraceRaysNV`).
* 40 signatures take a 64-bit value by value, and 5 take floats.
* `.Invoke(` call sites: **1606** in `Generated/`:
  * 802 are proc-addr lookups in the `VkInstance` and `VkDevice` `FunctionTable` constructors (105 + 697);
  * 804 are wrapper calls (`VkDeviceExtensions` 683, `VkInstanceExtensions` 91, platform files 30).
* There are **0** `.Invoke(` sites in `Manual/` (including `Manual/Classes`) and **2** in Abstractions:
  `CommandBufferExtensions.PushConstants` and `DeviceExtensions.BindDescriptorSet`.[^results][^brutal-abs-calli]

# Results

The ICD implements 41 entry points as plain `static` methods. `GetInstanceProcAddr` and `GetDeviceProcAddr` map
names to `(nint)(delegate*<…>)&Method` and wrap them as `new PfnVkVoidFunction((delegate*<void>)p)`. They log every
requested name and return NULL for unknown names.[^icd]

| Step / probe | Desktop CoreCLR | Browser-wasm (Mono interp), Chrome 151 x3 + Edge 155 |
|---|---|---|
| Assemblies are the exp-06 managed builds; `VkCmdDraw.Invoke` is a managed fnptr | PASS | PASS |
| Globals: AppContext GIPA, `vkEnumerateInstanceVersion` 1.3.0, 4 instance extensions, `Validation.IsSupported` | PASS | PASS |
| `Vulkan.CreateInstance` → 105 table lookups, 1 physical device | PASS | PASS |
| `GetProperties`/`GetProperties2`/`GetFeatures`/`GetMemoryProperties` (2 types)/`FindQueueFamily` | PASS | PASS |
| ICD → app callback through `PfnVkDebugUtilsMessengerCallbackEXT`: an app static method, and Abstractions' `Quiet` (R5) | PASS | PASS |
| `CreateDevice` (DeviceEx) → `vkGetDeviceProcAddr` + 697 lookups, `GetQueue` | PASS | PASS |
| `AllocateBuffer` picks type 1 (HOST_VISIBLE\|COHERENT); `BufferEx.Map()` → write 1 KiB → `Unmap`; ICD shadow memory matches | PASS | PASS |
| P01 `vkSetDebugUtilsObjectNameEXT` (struct, string, handle) | PASS | PASS |
| P02 `vkCmdBindDescriptorSets`: 8 args, 2 pointers | PASS | PASS |
| P03 `vkCmdSetDepthBias`: 3 floats (1.5, −2.25, 3.125e−7) / P04 `vkCmdSetBlendConstants` `float*` | PASS | PASS |
| P05 `vkCmdDrawIndexed`: 5 ints incl. −4 | PASS | PASS |
| P06 `vkCmdDrawIndexedIndirect`: `ByteSize64` offset 0x100000002 by value | PASS | PASS |
| P07 `vkCmdWriteTimestamp2`: `VkPipelineStageFlags2` (`enum : ulong`) = 0x100000002 by value | PASS | **PASS**. exp-01 saw 0x2 on the unmanaged path. |
| P08 `vkGetBufferDeviceAddress`: 64-bit struct return 0xFEED…0044 | PASS | PASS |
| P09 `vkCmdPushConstants`: payload pointer, sizes | PASS | PASS |
| P10 `vkWaitForFences`: `Bool32`; `timeout` typed **`nint`** by BRUTAL | 0x100000002 | 0x2. The C# type truncates on wasm32 before any call, and the ICD receives exactly what was passed. |
| P11 `vkMapMemory` raw table call: 2 x `ByteSize64` (0x100000002, 0x300000004) | PASS | PASS |
| P12 `vkCmdPipelineBarrier`: 10 args, 64-bit fields inside the barrier structs | PASS | **PASS**. exp-01 found that more than 6 args aborts on the unmanaged path. |
| P13 `vkCmdTraceRaysNV`: 15 args, 7 x `ByteSize64` by value | PASS | **PASS** |
| `VkResult` error → `VkResultError`; ICD-thrown exception caught by the caller | PASS | PASS |
| **Total** | **52/52** | **52/52** on every page load |

Sources: desktop[^out-desktop], Chrome[^out-browser], Edge[^out-browser-edge].

* The ordered proc-name requests (110 instance-level, 697 device-level) are byte-identical between desktop and
  browser.
* The name sequence is identical to exp-04's.
* The unmanaged flavor built from the same projects still passes exp-04's UCO-ICD scenario (`RESULT: OK`), and
  still works natively on MoltenVK (Apple M4 Pro, `RESULT: OK`). The `#if` structure therefore serves both
  flavors.[^out-unmanaged-uco][^out-unmanaged-native]

**Why Abstractions must be source-built too.** Managed-flavor Brutal.Vulkan with the **NuGet** Abstractions
behaves as follows:[^out-mismatch]

* Instance, `DeviceEx`, `AllocateBuffer` and `FreeAllocation` work, because they go through Brutal.Vulkan's
  extension methods.
* `CommandBufferExtensions.PushConstants<T>` throws `System.MissingFieldException: Field not found:
  'Brutal.VulkanApi.VkCmdPushConstants.Invoke'`. The field's signature type changed, so the 2 Abstractions methods
  that `ldfld` + `calli` themselves cannot bind.
* The 3 UCO callbacks cannot be used as managed pointers.

# Benchmarks

Each figure is the best of 3 x 1,000,000 calls after a 20,000-call warm-up. The browser column is the median of 3
Chrome page loads and 1 Edge page load. An ICD call counter confirmed that no call was elided.[^out-bench]

| Call | Browser (ns/call) | Desktop JIT (ns/call) |
|---|---|---|
| In-run baseline: direct static call (exp-01 T13a shape) | 21.3 | 0.8 |
| In-run baseline: `delegate*<int,int,int>` (exp-01 T13f shape) | 28.3 | 1.0 |
| `cb.EndRenderPass()` → `vkCmdEndRenderPass` (1 arg) | 31.0 | 0.9 |
| `cb.SetDepthBias(f, f, f)` (float shape) | 34.2 | 1.9 |
| `cb.WriteTimestamp2(ulong enum, …)` (64-bit enum shape) | 34.1 | 1.9 |
| `vkCmdBindDescriptorSets`, raw table call (8 args) | 36.8 | 6.2 |
| `cb.BindDescriptorSets(…)` wrapper (8 args + 2 `fixed` spans) | 40.0 | 6.7 |
| `device.PInvoke.vkGetDeviceQueue.Invoke` (4 args + out pointer) | 40.8 | 2.3 |
| `cb.Dispatch(x, y, z)` | 40.5 | 2.1 |

* **Comparison with exp-01.** These in-run baselines match exp-01's *isolated* runs (22.8 / 30.6 ns). exp-01's
  quoted 9.2 / 12.1 ns came from a long warmed suite, so compare ratios, not absolute values.[^exp01-perf][^exp01]
* **Ratios in the browser.**
  * A BRUTAL wrapper call into the managed ICD costs **1.1–1.5x** a bare managed fnptr call.
  * It is about **4–5x cheaper than one `[JSImport]`** call (154 ns isolated).
  * It is about **5–6x cheaper than exp-04's route**, an unmanaged fnptr → JS trampoline → `[JSExport]` (191 ns).
* **Construction in the browser.** The first touch of `Vulkan` (static constructor plus 4 global lookups) takes
  2.4–2.8 ms.
  * `Vulkan.CreateInstance`: 5.3–5.5 ms the first time. A second instance with name logging off takes 0.3–0.6 ms.
  * `CreateDevice` (697 lookups): 13.6 ms the first time and **2.9–3.0 ms warm**, about 4.2 µs per lookup, mostly
    the ICD's UTF-8 → string → `Dictionary` lookup in the interpreter.
  * exp-04's trampoline route took about 19 ms (27 µs per lookup, with logging).[^exp04]
  * `performance.now()` is coarsened to 0.1 ms here, because the page is not cross-origin isolated.
* **Construction on desktop.** First touch 1.2 ms, instance 4.9 ms first and 0.02 ms warm, device 20 ms first (JIT
  of the 697-entry constructor) and 0.08 ms warm.

# Robustness

* **(a) NULL table entry: fatal on both hosts, not catchable.**
  * Desktop: `cb.Draw` through the NULL `vkCmdDraw` entry kills the process with SIGSEGV, exit 139.[^out-desktop-null]
  * Browser: `[MONO] * Assertion at …/mono/metadata/object.c:2033, condition '<disabled>' not met`, then
    `ExitStatus`, and the runtime is gone.[^out-browser-null]
  * In the interpreter a managed function pointer *is* the callee's `InterpMethod*`
    (`ftnptr_to_imethod`). `MINT_CALLI` does not check it for NULL.[^mono-interp]
  * Wasm does not trap on loads from address 0, so execution reaches `g_assert (klass)` in
    `mono_class_vtable_checked`, which is line 2033 on `release/10.0`.[^mono-object]
  * The same holds for any bogus pointer value: managed fnptrs are not validated.
* **(b) Exceptions inside the ICD propagate cleanly.** An `InvalidOperationException` thrown in
  `ManagedIcd.CmdDispatch` reaches the caller's `catch` through the managed `calli` and the generated wrapper, with
  frames `CmdDispatch <- Dispatch <- Run` on desktop and `CmdDispatch <- Run` in the browser (wrapper inlined). The
  runtime and the ICD keep working afterwards.
  * This is a real advantage over unmanaged transitions. An exception cannot cross a UCO boundary, and exp-04's JS
  trampolines cannot carry one either.
* **(c) Trimming.**
  * A Release publish with `SuppressTrimAnalysisWarnings=false` gives **0 warnings**.
  * All **41** ICD `ldftn` targets are kept. They are referenced, and the *published, trimmed* app is the one that
    passed 52/52.
  * Published sizes:[^out-sizes]
    * `Brutal.Vulkan`: 581,397 B webcil, 140,716 B brotli. This is the same as exp-04's unmanaged flavor
      (573 KB / 140 KB brotli).
    * `Brutal.Vulkan.Abstractions`: 24,341 B, 9,250 B brotli.
    * Untrimmed: 1.56 MB and 126 KB.
  * `VkResult` errors still become `VkResultError` through the generated wrappers.

# Findings

1. **The managed flavor is a mechanical, near-zero-maintenance transform.**
   * One regex does almost all the work: 2441 hits in one generated file, plus 1 alias and 3 attributes in
     Abstractions.
   * The trees need no source edits beyond the exp-04 loader patch, which now carries a few `#if` blocks.
   * Build overhead is about 0.5 s.
   * The output is metadata-clean: 814 managed fields, 1614 managed `calli` sites, 0 unmanaged.
2. **Every exp-01 browser limitation disappears.**
   * No `[UnmanagedCallersOnly]` is needed, so the "pointer is 0" problem does not arise.
   * There is no interp→native transition, so there is no "CANNOT HANDLE INTERP ICALL SIG". The 10- and 15-argument
     shapes work.
   * 64-bit enums and `ByteSize64` structs pass by value intact, as do 64-bit returns and floats.
   * No JS is involved: `main.js` only calls `runMain`.
   * The other direction works too: the ICD calling app callbacks.
3. **The remaining 64-bit hazard is in BRUTAL's types, not in the call path.**
   * Some `uint64_t` values are typed `nint`: `vkWaitForFences`'s `timeout`, `VkDebugUtilsObjectNameInfoEXT.ObjectHandle`
     and all 56 handle types. They are 32 bits on wasm32.[^brutal-device-ext]
   * Generated structs use `StructLayout.Explicit` with 64-bit offsets.[^brutal-props2]
   * Both sides of a C#↔C# call agree, so this is harmless for a managed ICD as long as it interprets such values
     consistently. For example, it should treat a negative `nint` timeout as `UINT64_MAX`.
4. **Per-call cost is about 31–41 ns in the interpreter.** That is 1.1–1.5x a bare managed fnptr and 4–6x cheaper
   than any JS-bridged route. Device creation takes about 3 ms warm.
5. **Robustness.**
   * ICD exceptions are catchable managed exceptions.
   * NULL (or garbage) table entries remain fatal on both hosts.
   * Trimming is safe.
6. **Abstractions must be source-built with the same transform.**
   * The NuGet build fails in its 2 direct-`calli` methods with `MissingFieldException`, and its 3 UCO callbacks are
     unusable.
   * Everything routed through Brutal.Vulkan wrappers keeps working, which makes the break easy to miss.
7. **One project, two flavors.** The unmanaged flavor (UCO ICD, MoltenVK) builds from the same projects and is
   unchanged from exp-04.

# Implications for the ICD design

* **Make the managed flavor the primary browser path.** It needs no workload, no relink, no signature anchors and no
  JS. It is the cheapest call, and errors are debuggable managed exceptions.
  * The unmanaged flavor stays the desktop/native path.
  * The managed-flavor `Brutal.Vulkan` cannot load a native driver; its native branch is compiled out on purpose. A
    product therefore ships two builds of the same assembly identity (`Brutal.Vulkan, Version=1.0.0.0`), selected per
    platform: browser = managed, desktop = upstream.
  * Never mix a managed-flavor Brutal.Vulkan with NuGet-built dependents.
* **The ICD should never hand out NULL for a name it advertises, or for any name an app might call.** NULL is a
  process kill or wasm abort.
  * Generate **typed throwing stubs** for all 814 commands from `Functions.gen.cs` signatures, for example
    `throw new NotImplementedException("vkCmdDraw")`.
  * Because exceptions propagate (finding 5), each stub turns a silent abort into a named, catchable error.
  * Alternatively, a generator option could emit a null check in each wrapper. It costs a compare per call; not
    measured.
* **Proc-addr lookups.** Replace `PtrToStringUTF8` + `Dictionary<string, …>` with a UTF-8 span-keyed table, for
  example a precomputed perfect hash. This would cut the ~4 µs per lookup (≈3 ms per device).
* **Treat BRUTAL's `nint`-typed 64-bit values as signed 32-bit on wasm32.** In particular, `-1` means "infinite
  timeout".
* **Dependents that hand Vulkan pointers to native code cannot use this flavor.** `Brutal.Vulkan.Vma` passes
  `Vulkan.GetInstanceProcAddr` / `vkGetDeviceProcAddr` into native VMA, which cannot call managed pointers.[^brutal-vma]
  Browser apps must use the Abstractions allocator (`DeviceEx`), as this experiment does, or a managed allocator.
* **What an upstream "generator flag" would look like.** BRUTAL's binding generator is not in the public tree; only
  `Generated/` is committed.
  * A generator option `FunctionPointerKind = Unmanaged | Managed` would emit either `delegate* unmanaged[Cdecl]<…>`
    or `delegate*<…>` in the wrapper structs.
  * Hand-written code would only need a type alias. Abstractions' callback alias and the 3 debug callbacks would get
    `#if BRUTAL_MANAGED_FNPTR` around `[UnmanagedCallersOnly]`.
  * The MSBuild property `BrutalManagedFnptr` would map to the `BRUTAL_MANAGED_FNPTR` define.
  * BRUTAL would ship the result as separate packages (for example a `browser`-only `Ahwoo.BRUTAL.Vulkan.Managed`),
    or as RID-specific `runtimes/browser-wasm/lib` assets with the same identity. The managed variant drops the 4
    `LibraryImport`s and the `NativeLibrary` path.
  * Until upstream agrees, `tools/transform.py` plus the two-file patch is a 5-rule post-process that is easy to
    review.

# Open questions

* **AOT.** Does the flavor behave the same under `wasm-tools` AOT (`RunAOTCompilation`, `mono_llvm_only`)? There,
  managed function pointers are `MonoFtnDesc*` and `ftnptr_to_imethod` asserts on NULL differently. Is per-call
  cost then near native?
* **Other dependents.** Do Brutal.ImGui.Backends' Vulkan renderer and other dependents bind unchanged to a
  managed-flavor Brutal.Vulkan?
  * The ImGui Vulkan backend has 0 direct `.Invoke(` sites, but it was not run.
  * Its ImGui-side multi-viewport callbacks are separate `[UnmanagedCallersOnly]` code meant for native ImGui.
* **Interpreter tiering.** Would the 697-entry `VkDevice.FunctionTable` constructor (13.6 ms on first use) benefit
  from lazy per-command lookup or a generated name→index table shared by ICD and binding?
* **Upstream acceptance.** Would BRUTAL accept a generator flag (CONTRIBUTING requires an issue first), or should the
  flavor live as a downstream package built by this transform?
* **Hot-path cost under load.** What do real draw-heavy frames cost, with the ICD doing WebGPU command encoding and
  `[JSImport]` batching? The 31–41 ns call overhead is small next to exp-03's JS interop costs, but this has not been
  measured end to end.

[^project]: exp-06 project README (layout, transform rules, build/run commands)
[^results]: exp-06 RESULTS (summary table, build problems, IL evidence, verbatim desktop/browser logs, benchmarks, robustness)
[^transform]: tools/transform.py (rules R1–R5, incremental writes, leftover check)
[^targets]: BrutalManagedFnptrTransform MSBuild target
[^props]: BrutalManagedFnptr switch and per-flavor obj/bin split
[^csproj-abstractions]: Reconstructed Brutal.Vulkan.Abstractions.csproj
[^patched-resolver]: Patched ImportResolver.cs, one source for both flavors
[^patch]: Unified diff against BRUTAL a25cdf8 (git apply --check OK)
[^icd]: Managed ICD (41 static entry points, &Method proc-addr table, argument probes)
[^scenario]: Scenario run by the desktop and browser apps
[^out-transform]: grep evidence over the generated copies
[^out-il]: FnptrInspect over NuGet, unmanaged-flavor and managed-flavor assemblies
[^out-il-trimmed]: FnptrInspect over the ILLink output of the browser publish
[^out-desktop]: DesktopFlavorSmoke console output (CoreCLR, 52/52 PASS, benchmarks)
[^out-desktop-null]: DesktopFlavorSmoke --call-null (exit 139)
[^out-browser]: Headless Chrome for Testing 151 run of the published BrowserFlavorSmoke (52/52 PASS)
[^out-browser-edge]: Headless Edge 155 run (52/52 PASS)
[^out-browser-null]: Browser run calling a NULL table entry (Mono assertion object.c:2033)
[^out-bench]: Benchmark and construction-timing summary
[^out-sizes]: Published browser-wasm sizes and linked IL sizes
[^out-unmanaged-uco]: Unmanaged flavor + exp-04 UCO FakeIcd (RESULT OK)
[^out-unmanaged-native]: Unmanaged flavor on MoltenVK (RESULT OK)
[^out-mismatch]: Managed-flavor Brutal.Vulkan with the NuGet Abstractions (MissingFieldException)
[^out-uco-off]: Build with rule R5 disabled (CS8786)
[^out-build]: Clean and incremental build timings of both flavors
[^exp01]: exp-01 report
[^exp01-perf]: exp-01 isolated perf runs
[^exp04]: exp-04 report
[^fnptr-note]: Function pointers, UnmanagedCallersOnly and native resolution on .NET 10 browser-wasm
[^brutal-functions]: BRUTAL Functions.gen.cs
[^brutal-vkhelpers]: Abstractions VulkanHelpers.cs
[^brutal-abs-calli]: Abstractions CommandBufferExtensions.PushConstants / DeviceExtensions.BindDescriptorSet
[^brutal-abs-usings]: Abstractions GlobalUsings.cs
[^brutal-device-ext]: Generated device wrappers (WaitForFences nint timeout)
[^brutal-props2]: Generated struct with explicit 64-bit layout
[^brutal-vma]: Brutal.Vulkan.Vma hands proc-addr pointers to native VMA
[^mono-interp]: Mono interpreter release/10.0 — ftnptr_to_imethod and MINT_CALLI
[^mono-object]: Mono object.c release/10.0 — g_assert (klass) in mono_class_vtable_checked
