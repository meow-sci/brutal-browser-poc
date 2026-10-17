---
type: Code Analysis
title: Portability audit of BRUTAL's managed core libraries for .NET 10 browser-wasm
description: Line-level audit of Brutal.Core, Interop, Memory, Collections, Strings, Logging, Numerics, Maths, Concurrency and Localization for single-threaded Mono browser-wasm (wasm32), with ratings, the exact failure points (Platform static ctor, console logger thread, thread-pool job system) and the minimal patches.
tags: [brutal, wasm, portability, threading, simd, logging, memory, audit]
sources:
  - id: platform
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Core/Platform.cs
    title: Brutal.Core/Platform.cs
  - id: bytesize
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Core/Types/ByteSize.cs
    title: Brutal.Core/Types/ByteSize.cs
  - id: enumfmt
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Core/Enums/EnumFormatting.cs
    title: Brutal.Core/Enums/EnumFormatting.cs (generic UnsafeAccessor into CoreLib)
  - id: enuminfo
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Core/Enums/Enum.Info.cs
    title: Brutal.Core/Enums/Enum.Info.cs (reflection-based enum info)
  - id: ptr
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Core/Pointers/PtrOfT.cs
    title: Brutal.Core Ptr<T>
  - id: conventions
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/nupkg-extract/ahwoo.brutal.core/docs/brutal-conventions.md
    title: brutal-conventions.md shipped in Ahwoo.BRUTAL.Core
  - id: mem
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Memory/Mem.cs
    title: Brutal.Memory/Mem.cs
  - id: ptrowner
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Memory/PtrOwner.cs
    title: Brutal.Memory/PtrOwner.cs
  - id: nativelist
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Collections/NativeList/NativeList.cs
    title: Brutal.Collections NativeList
  - id: namespace
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Collections/Name/Namespace.cs
    title: Brutal.Collections Namespace (Lock)
  - id: sbpool
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Strings/StringBuilder/StringBuilderPool.cs
    title: Brutal.Strings StringBuilderPool (ObjectPool use)
  - id: logcategory
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Logging/LogCategory.cs
    title: Brutal.Logging LogCategory
  - id: logstring
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Logging/LogString.cs
    title: Brutal.Logging LogString<TLevel> interpolated string handler
  - id: logsystem
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Logging/LogSystem.cs
    title: Brutal.Logging LogSystem
  - id: melconsole
    resource: /Users/asherwin/.nuget/packages/microsoft.extensions.logging.console/10.0.0/lib/net10.0/Microsoft.Extensions.Logging.Console.dll
    title: Microsoft.Extensions.Logging.Console 10.0.0 (decompiled by the audit sub-agent)
  - id: browser-corelib
    resource: /Users/asherwin/.nuget/packages/microsoft.netcore.app.runtime.mono.browser-wasm/10.0.1/runtimes/browser-wasm/native/System.Private.CoreLib.dll
    title: Browser-wasm System.Private.CoreLib 10.0.1 (Thread.Start, ThreadPool, Enum internals inspected)
  - id: objectpool
    resource: /Users/asherwin/.nuget/packages/microsoft.extensions.objectpool/11.0.0-preview.5.26302.115/
    title: Microsoft.Extensions.ObjectPool 11.0.0-preview.5 package contents
  - id: f4x4ops
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Numerics/Matrix/float4x4/float4x4.operators.gen.cs
    title: float4x4 operators (Vector128)
  - id: d4x4ops
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Numerics/Matrix/double4x4/double4x4.operators.gen.cs
    title: double4x4 operators (unconditional Vector256)
  - id: f4x4
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Numerics/Matrix/float4x4/float4x4.gen.cs
    title: float4x4 core (TransposeKernel guards)
  - id: shuffle2
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Numerics/VectorShuffle2.cs
    title: Vector128/256 Shuffle2 extension polyfill
  - id: numerics-usings
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Numerics/GlobalUsings.cs
    title: Brutal.Numerics GlobalUsings (System.Xml.Serialization)
  - id: maths
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Maths/RandomExtensions.cs
    title: Brutal.Maths RandomExtensions
  - id: jobrunner
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Concurrency/Jobs/JobRunner.cs
    title: Brutal.Concurrency JobRunner (ThreadPool + busy-spin Wait)
  - id: jobscheduler
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Concurrency/Jobs/JobScheduler.cs
    title: Brutal.Concurrency JobScheduler
  - id: resxreader
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Localization/Resources/ResXReader.cs
    title: Brutal.Localization ResXReader
  - id: interop-marshaller
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Interop/InteropStringMarshaller.cs
    title: Brutal.Interop InteropStringMarshaller
  - id: interop-gen
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/nupkg-extract/ahwoo.brutal.interop/analyzers/dotnet/cs/Brutal.Interop.SourceGenerators.dll
    title: Brutal.Interop.SourceGenerators.dll (InteropHandleGenerator; source not in tree)
  - id: ksa-program
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA/Program.cs
    title: KSA decompiled Program.cs (AddConsole host logging; reference only)
  - id: ksa-jobs
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA/JobSystems.cs
    title: KSA decompiled JobSystems.cs (job-runner count, WaitAll; reference only)
  - id: vallist
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ImGui/Core/VaList.cs
    title: Brutal.ImGui VaList (Platform consumer, 8-byte vararg slots)
  - id: subagent-report
    resource: /Users/asherwin/.claude/projects/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/tool-results/toolu_01Mh5ZCpy1B57UGyMQb6ZVqe.txt
    title: Full sub-agent audit report (53 KB, line-level evidence incl. PDB-derived build facts)
  - id: srcbuild
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/build/
    title: Scratch source builds of Brutal.Core/Concurrency/Vulkan/Vulkan.Abstractions with SDK 10.0.100
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:16:59Z }
status: draft
---

# Ratings

None of these ten libraries has a `DllImport`, `LibraryImport`, `NativeLibrary`, `delegate* unmanaged` or `[UnmanagedCallersOnly]`. All are pure managed.[^subagent-report]

| Library | Rating | One-line reason | Patch needed for browser |
|---|---|---|---|
| Brutal.Core | **YELLOW** | `Platform` static ctor throws on browser; `ByteSize` is `nuint` (32-bit on wasm); enum formatting uses a generic `[UnsafeAccessor]` into CoreLib internals[^platform][^bytesize][^enumfmt] | Add `Platform.Family.Browser` (small source patch), unless no code touches `Platform` |
| Brutal.Interop | **GREEN** | custom UTF-8 string and handle marshallers over `NativeMemory` and `Marshal.PtrToStringUTF8`[^interop-marshaller] | none |
| Brutal.Memory | **GREEN** | `Mem` is a thin wrapper over `System.Runtime.InteropServices.NativeMemory` (emscripten `malloc` on wasm)[^mem] | none |
| Brutal.Collections | **GREEN** | `NativeList`/`NativeStrideList` over `Mem`; only an uncontended `Lock` in `Namespace`[^nativelist][^namespace] | none |
| Brutal.Strings | **GREEN** | pure managed; the preview `Microsoft.Extensions.ObjectPool` resolves to its `netstandard2.0` asset[^sbpool][^objectpool] | optionally swap to ObjectPool 10.0.x in a source build |
| Brutal.Logging | **YELLOW** | the library is fine; the **host's `AddConsole()` provider starts a `Thread` and is `[UnsupportedOSPlatform("browser")]`**[^melconsole][^browser-corelib] | host must register a browser-safe synchronous provider |
| Brutal.Numerics | **GREEN** (perf caveat) | only portable `Vector128/256/512` APIs, no X86/Arm/PackedSimd. `double4x4 *` uses `Vector256` unconditionally, which is software-emulated on wasm[^d4x4ops] | optional scalar fallback for performance |
| Brutal.Maths | **GREEN** | one file of `Random` extensions[^maths] | none |
| Brutal.Concurrency | **YELLOW, effectively RED single-threaded** | jobs go to `ThreadPool.UnsafeQueueUserWorkItem`, and `Wait()` is a busy-spin. On single-threaded wasm the work item runs only after returning to the JS event loop, so `Wait()` **hangs forever**. No inline mode exists[^jobrunner][^browser-corelib] | add an inline-execution mode (about 10 lines) |
| Brutal.Localization | **YELLOW (minor)** | `ResXReader(string path)` uses file I/O (fine on MEMFS); culture uses are invariant-safe[^resxreader] | none (load files into the VFS or use the `TextReader` ctor) |

# Brutal.Core

* **`Platform`** (`Platform.cs:9–34`): `enum Platform.Family { Windows, Linux, MacOS }`, `CurrentFamily`, `IsWindows/IsLinux/IsMacOS/IsUnix`. The explicit static ctor chains `OperatingSystem.IsWindows/IsLinux/IsMacOS` and otherwise does `throw new PlatformNotSupportedException("Unsupported platform")`.[^platform] On browser, the first touch throws `TypeInitializationException`, and later touches rethrow it. **Consumers in the whole tree:** `Brutal.ImGui/Core/VaList.cs:173` (a static field initializer, `Platform.IsLinux ? … : 0`) and `Brutal.OpenAl/Manual/NativeLoader.cs:31` (the DllImport resolver). **No consumer exists in the ten managed libraries, Brutal.Vulkan, Abstractions or Gltf**, so these run on browser without touching `Platform`.[^subagent-report][^vallist] The KSA game itself uses `Platform` in `Program.cs:768` and its GLFW ImGui backend.[^ksa-program]
  * Patch (source build of Core): add `Family.Browser`, `IsBrowser`, and `else if (OperatingSystem.IsBrowser()) CurrentFamily = Family.Browser;`. **Do not** map browser to Linux, because `VaList` would then assume the SysV `va_list` header. `VaList` also packs every vararg into 8-byte slots (`VaList.cs:87, 94, 193`), which does not match wasm32. That matters only for ImGui (UNVERIFIED against the Emscripten ABI).
* **`ByteSize`** = `readonly record struct ByteSize(nuint Value)` (`ByteSize.cs:243`), so it is **32-bit on wasm32** by design ("like size_t"). `ByteSize64` is `ulong` (`:155`). The implicit `ByteSize64 → ByteSize` conversion truncates silently above 4 GB (`:162`, `:227`). The Vulkan consequence is that `VK.WHOLE_SIZE` becomes `0xFFFFFFFF` (see [/brutal/vulkan-binding-architecture.md](/brutal/vulkan-binding-architecture.md)).[^bytesize]
* **`Ptr`, `Ptr<T>`, `PtrPtr<T>`** wrap raw pointers (pointer-size neutral). `Ptr<T>` converts implicitly from `Span`/`ReadOnlySpan` via `Unsafe.AsPointer` **without pinning**, which is safe only for `[Fixed]` (unmanaged or stack) spans. The same caveat applies on CoreCLR.[^ptr][^conventions] `Bool8`/`Bool32`, `ElementCount` (int) and `INullable` are trivial.
* **Enum utilities:** `EnumFormatting.cs:98–106` uses a **generic `[UnsafeAccessor]`** targeting the internal `Enum.TryFormatUnconstrained<TEnum>`. The browser CoreLib 10.0.1 has a matching internal method; runtime and trimming behaviour on Mono is UNVERIFIED and should be smoke-tested.[^enumfmt][^browser-corelib] `Enum.Info.cs` uses `Enum.GetValues(Type)` and `GetCustomAttribute<FlagsAttribute>` reflection.[^enuminfo]
* Shipped analyzers (`Brutal.Core.Analyzers`, IDs BRUTALCORE0003/7/8/9/10/12; 0009 and 0010 `[Fixed]` misuse are **errors**) apply to NuGet consumers only; dependencies use `exclude="Build,Analyzers"`. There is **no analyzer source** in the tree.[^subagent-report]

# Brutal.Interop

`InteropStringMarshaller` and `InteropNullableStringMarshaller` (UTF-8: a 256-byte caller buffer, else `NativeMemory.Alloc`; out via `Marshal.PtrToStringUTF8`), `InteropHandleMarshaller<T,THandle>` and `IInteropHandle`.[^interop-marshaller] The NuGet package also ships `Brutal.Interop.SourceGenerators.dll` (`InteropHandleGenerator`, triggered by `[InteropHandle]` on structs). It generates a `partial struct` with a `record struct Handle(nint Value)`, a `Pointer` property and `[NativeMarshalling]`. **Its source is not in the tree.**[^interop-gen] None of the ten managed libs uses `[InteropHandle]`; its users are native bindings (Jolt, NotCurses, Fmod, ShaderC, OpenAl, Sdl*, Slang). Building those from source requires referencing the analyzer DLL or re-implementing the roughly 20-line generator.[^subagent-report]

# Brutal.Memory and Brutal.Collections

* `Mem` (`Mem.cs:10–214`) forwards to `NativeMemory.Alloc/AllocZeroed/Realloc/Free/Copy/Fill/Clear` and adds typed helpers (`AllocAndCopy<T>`, `Alloc<T>(count)` returning `Span<T>`, `Copy<T>`). There is **no tracking, no locks and no aligned allocation**, despite the conventions doc mentioning "memory tracking".[^mem][^conventions] `PtrOwner<T>` allocates in its ctor and frees in `Dispose` or the finalizer. Its `Reset()` leaks (it calls `Release()`), a correctness bug unrelated to portability.[^ptrowner]
* Collections: `NativeList`, `NativeList<T>`, `NativeStrideList` (finalizers free native memory), `NativeStrideSpan`, `ScratchSpan<T>` (stackalloc or `ArrayPool`), `FreeListIndexPool`, and `Name<T>`/`Namespace<T>` string interning over CommunityToolkit `StringPool` with a `Lock`.[^nativelist][^namespace]
* wasm notes: `NativeMemory` maps to emscripten `malloc`, so mapped Vulkan memory in a C# implementation would come from the same heap. Its alignment (probably 8 or 16) is UNVERIFIED. The finalizer thread does not exist single-threaded, so when finalizers run is UNVERIFIED. Always `Dispose`.

# Brutal.Strings

* Types: `String8` (heap, NUL-terminated), `RefString8` (ref-struct view), `RefString8Array` (builds `char**`, pointer-size aware), `StringBuilder<TChar>`/`ValueStringBuilder<TChar>`, interpolation handlers, and `Utf<T>`/`Utf8`/`Utf16`/`Unicode`/`Utf8.Path`.[^subagent-report]
* **ObjectPool:** the only use is `StringBuilderPool<TChar> : DefaultObjectPool<StringBuilder<TChar>>` (`StringBuilderPool.cs:12`), consumed by Brutal.Logging's `LogString`. The referenced **`Microsoft.Extensions.ObjectPool 11.0.0-preview.5.26302.115`** contains `lib/net11.0`, `lib/net462` and `lib/netstandard2.0` only, so a net10.0 (including browser-wasm) app binds the **netstandard2.0** asset. That works, but it means a stable package depends on a prerelease package. A source build can use `Microsoft.Extensions.ObjectPool 10.0.x`.[^sbpool][^objectpool]
* Globalization: ordinal comparisons, no `ToUpper`/`ToLower`. Number formatting honours the provider or current culture, which is fine with `InvariantGlobalization=true`.[^subagent-report]

# Brutal.Logging

* `LogSystem.Initialize(Action<ILoggingBuilder>)` creates the `LoggerFactory` and an `ILogger` per registered `LogCategory`. `LogSystem.IsEnabled` defaults to **false**. **BRUTAL itself never adds a provider.**[^logsystem]
* `LogCategory.Trace/Debug/Info/Warning/Error/Critical(...)` take a `[InterpolatedStringHandlerArgument] LogString<TLevel>` (`LogCategory.cs:163–218`; `LogString.cs:61–113`). There is **no `[Conditional]`**. The handler constructor computes `category.IsEnabled(level)` and **skips evaluating the interpolation holes** when disabled. A disabled call therefore costs a few field loads and two short calls, with no allocation. With logging enabled but the level filtered, it adds two `ILogger.IsEnabled` interface calls.[^logcategory][^logstring] This is the cost of the `Log.Trace()` that Brutal.Glfw (154 call sites) and Brutal.Sdl (about 1,500) put before every native call. Brutal.Vulkan has no per-call logging.
* **Browser failure point (host-side):** `Microsoft.Extensions.Logging.Console` 10.0.0 marks `ConsoleLoggerExtensions`, `ConsoleLoggerProvider` and `ConsoleLoggerProcessor` `[UnsupportedOSPlatform("browser")]`. `ConsoleLoggerProcessor`'s ctor unconditionally starts a background `Thread`, and on single-threaded browser `Thread.Start()` throws `PlatformNotSupportedException`.[^melconsole][^browser-corelib] KSA's host calls `builder.AddConsole(...)` (`Program.cs:712–723`).[^ksa-program] **On browser: register a tiny synchronous `ILoggerProvider` that writes through `Console.WriteLine`** (which surfaces as `console.log`), or leave `LogSystem` uninitialised. Note that `Log<T>` throws `LogNotInitializedException` if logging is enabled but a category has no logger.[^logcategory]
* Packaging oddity: Core, Memory, Collections, Strings and Numerics all declare `Microsoft.Extensions.Logging(.Console) 10.0.0` dependencies, which their code never uses. That brings the console provider into every BRUTAL app's dependency graph. It is harmless unless it is registered.[^subagent-report]

# Brutal.Numerics and Brutal.Maths

* Types: vectors `{bool,byte,sbyte,short,ushort,int,uint,long,ulong,float,double}{2,3,4}`; matrices `float3x2`, `float4x4`, `double3x2` and `double4x4`, plus stub "unsupported" matrix types; `floatQuat`/`doubleQuat`, `floatPlane`/`doublePlane`, `floatRect`/`doubleRect`, `Color`, and packing helpers. No `Half`. 365 of 381 files are T4-generated `.gen.cs`; the `.tt` templates are **not** in the tree.[^subagent-report]
* SIMD: no `Sse*/Avx*/AdvSimd/X86/Arm/PackedSimd` usage anywhere, only portable `Vector128/256/512` (`ShuffleNative`, `MultiplyAddEstimate`, `ConditionalSelect`, `Dot`) and the `Vector128/256.Shuffle2` polyfill written as a **C# 14 static extension**.[^shuffle2] Nothing can throw `PlatformNotSupportedException`. Guarded kernels (`float4x4` transpose, decompose and invert; `double4` and `doubleQuat` ops) take scalar fallbacks when `IsHardwareAccelerated` is false.[^f4x4] Unguarded: `float4x4 *` (Vector128, fine; wasm SIMD128 when AOT with SIMD) and **`double4x4 *` (Vector256, software-emulated on wasm, a performance risk for double-precision-heavy games such as KSA)**.[^f4x4ops][^d4x4ops]
* `GlobalUsings.cs` imports `System.Xml.Serialization` only for `[XmlAttribute]`/`[XmlIgnore]` annotations on the generated types. No `XmlSerializer` is used, but the reference to `System.Private.Xml` may add download size if not trimmed.[^numerics-usings]
* Brutal.Maths is a single file of `Random` extensions.[^maths]

# Brutal.Concurrency

* `JobScheduler(uint jobRunnerCount, …)` requires 1 or more runners, supplied by the caller (no `Environment.ProcessorCount`). `Queue` assigns jobs to the least-loaded runner. `ExecuteJobs` → `runner.ExecuteJobs` → a CAS state transition, then **`ThreadPool.UnsafeQueueUserWorkItem(_workExecutor, false)`** (`JobRunner.cs:122–145`). `JobRunner.Wait()` is a **pure busy-spin** on `Volatile.Read(_state)` (`:212–218`). `DoWork` sets `Thread.CurrentThread.Priority` (`:289`). There is no `new Thread`, Task, Sleep or event.[^jobrunner][^jobscheduler]
* Single-threaded browser: the browser CoreLib's thread pool schedules the work item with `MainThreadScheduleBackgroundJob`, so it runs **only after control returns to the JS event loop**. Any synchronous `ExecuteJobs(); Wait();`, including KSA's `JobSystems.WaitAll` (`JobSystems.cs:61–67`), therefore **spins forever**.[^browser-corelib][^ksa-jobs] An exception inside a job causes `Environment.FailFast`.
* **Patch:** add a static inline mode, e.g. `JobRunner.ExecuteInline`. When set, `ExecuteJobs` calls `_workExecutor.Execute()` directly instead of queueing and skips the `Priority` setter. `DoWork`'s `finally` resets the state, so `Wait()` returns immediately. Brutal.Concurrency has no dependencies (framework only), so source-building it is trivial. A scratch build with SDK 10.0.100 succeeded.[^srcbuild]
* Multithreaded wasm (`WasmEnableThreads`) needs COOP/COEP headers, the multithreaded runtime pack, and a worker pool of at least N runners. A busy-spin on the browser UI thread is still undesirable. Run the game loop on a worker. UNVERIFIED for .NET 10.

# Brutal.Localization

`LString`, `LStrings`, `LocalizedStrings` and ResX reader/writer (vendored resx-net, `XDocument`). `ResXReader(string)` uses `StreamReader(path)`; a `TextReader` overload exists. The `[GeneratedRegex(..., "en-US")]` code has no runtime culture dependency. Uses `Lock` and `DefaultCategory.Log`.[^resxreader][^subagent-report]

# Build facts that matter for these libraries

The shipped PDBs record **language version 14.0**, `nullable enable`, `unsafe`, Release, implicit usings on, embedded `AssemblyVersion("1.0.0.0")` and **`[assembly: DisableRuntimeMarshalling]`**, with no custom defines. The tree contains the complete sources, matching the PDB document lists file-for-file. C# 14 **extension blocks**, including extension operators and properties, are used in Core, Strings, Numerics and Localization; examples are `EnumArithmetic.gen.cs:13`, `EnumExtensions.cs:13` and `VectorShuffle2.cs:22`.[^subagent-report] **Verified:** Brutal.Core, which has the extension operators, and Brutal.Concurrency compile from source unchanged with the locally installed .NET SDK 10.0.100, giving 0 warnings and 0 errors using the csproj template in [/brutal/building-from-source.md](/brutal/building-from-source.md).[^srcbuild]

# Smoke tests to run first on browser-wasm

1. Enum formatting through the `[UnsafeAccessor]` path (an enum hole in a `StringBuilder8` or a log line), under a trimmed Release build.
2. `Enum<T>` flags detection after trimming.
3. `float4x4`/`double4x4` multiply and invert results versus desktop (expect at most 1-ulp differences from `MultiplyAddEstimate`).
4. `JobScheduler` with the inline patch.
5. `LogSystem.Initialize` with a custom browser provider.

# Open questions

* UNVERIFIED: Mono-wasm runtime support for the generic `[UnsafeAccessor]` in `EnumFormatting.cs`, and whether ILLink preserves its target.
* UNVERIFIED: whether `LoggerFactory.Create` instantiates providers eagerly. This decides whether merely calling `AddConsole` throws, or only the first log call.
* UNVERIFIED: emscripten `malloc` alignment for `NativeMemory.Alloc`, and the timing of finalizers on single-threaded wasm.
* UNVERIFIED: `Vector128.IsHardwareAccelerated` under the Mono interpreter versus AOT with `WasmEnableSIMD`, and the cost of the `Vector256<double>` fallback.

[^platform]: Brutal.Core/Platform.cs
[^bytesize]: Brutal.Core/Types/ByteSize.cs
[^enumfmt]: Brutal.Core/Enums/EnumFormatting.cs
[^enuminfo]: Brutal.Core/Enums/Enum.Info.cs
[^ptr]: Brutal.Core Ptr<T>
[^conventions]: brutal-conventions.md
[^mem]: Brutal.Memory/Mem.cs
[^ptrowner]: Brutal.Memory/PtrOwner.cs
[^nativelist]: Brutal.Collections NativeList
[^namespace]: Brutal.Collections Namespace
[^sbpool]: Brutal.Strings StringBuilderPool
[^logcategory]: Brutal.Logging LogCategory
[^logstring]: Brutal.Logging LogString
[^logsystem]: Brutal.Logging LogSystem
[^melconsole]: Microsoft.Extensions.Logging.Console 10.0.0
[^browser-corelib]: Browser-wasm System.Private.CoreLib 10.0.1
[^objectpool]: Microsoft.Extensions.ObjectPool 11.0.0-preview.5 package
[^f4x4ops]: float4x4 operators
[^d4x4ops]: double4x4 operators
[^f4x4]: float4x4 core
[^shuffle2]: VectorShuffle2.cs
[^numerics-usings]: Brutal.Numerics GlobalUsings
[^maths]: Brutal.Maths RandomExtensions
[^jobrunner]: Brutal.Concurrency JobRunner
[^jobscheduler]: Brutal.Concurrency JobScheduler
[^resxreader]: Brutal.Localization ResXReader
[^interop-marshaller]: Brutal.Interop InteropStringMarshaller
[^interop-gen]: Brutal.Interop.SourceGenerators.dll
[^ksa-program]: KSA Program.cs
[^ksa-jobs]: KSA JobSystems.cs
[^vallist]: Brutal.ImGui VaList.cs
[^subagent-report]: Full sub-agent audit report
[^srcbuild]: Scratch source builds
