---
type: Research Note
title: Game main loop, threading model and porting pitfalls for C# on .NET 10 browser-wasm
description: How to drive a frame loop from requestAnimationFrame into C# without blocking the browser thread, what blocking/threading APIs do on the single-threaded Mono-wasm runtime, how async WebGPU work must be structured, and a checklist of platform behaviours (OS detection, pointer size, processor count, memory, files, HTTP, timers, clocks) that bite when porting a desktop framework like BRUTAL.
tags: [dotnet-wasm, game-loop, requestanimationframe, threading, async, pitfalls, porting, brutal]
sources:
  - id: template-nupkg
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.templates.net10/content/templates/browser/Program.cs
    title: wasmbrowser template Program.cs (await Task.Delay loop)
  - id: dotnet-d-ts
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.netcore.app.runtime.mono.browser-wasm/runtimes/browser-wasm/native/dotnet.d.ts
    title: dotnet.d.ts (runMain keeps runtime alive; run/runMainAndExit exit)
  - id: wasm-features-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/features.md
    title: Configuring and hosting .NET WebAssembly applications (release/10.0)
  - id: wasm-threads-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/threads.md
    title: Threaded runtime notes (background jobs on main thread, JSSynchronizationContext)
  - id: monitor-c
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/mono/metadata/monitor.c
    title: Mono monitor.c ("Cannot wait on monitors on this runtime.")
  - id: mono-threads-c
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/mono/utils/mono-threads.c
    title: Mono mono-threads.c (mono_thread_info_sleep → clock_nanosleep)
  - id: thread-ref
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Threading.Thread/ref/System.Threading.Thread.cs
    title: System.Threading.Thread ref ([UnsupportedOSPlatform("browser")] on Start unless FEATURE_WASM_MANAGED_THREADS)
  - id: task-replicator
    resource: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Threading.Tasks.Parallel/src/System/Threading/Tasks/TaskReplicator.cs
    title: TaskReplicator.cs ("Replicated tasks cannot yield in this single-threaded browser environment")
  - id: runtimeinfo-browser
    resource: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/RuntimeInformation.Browser.cs
    title: RuntimeInformation.Browser.cs (OSDescription "Browser", OSArchitecture Wasm)
  - id: emscripten-pthread-stub
    resource: https://github.com/emscripten-core/emscripten/blob/3.1.56/system/lib/pthread/library_pthread_stub.c
    title: Emscripten 3.1.56 library_pthread_stub.c (emscripten_num_logical_cores returns 1 single-threaded)
  - id: emscripten-sysconf
    resource: https://github.com/emscripten-core/emscripten/blob/3.1.56/system/lib/libc/musl/src/conf/sysconf.c
    title: Emscripten 3.1.56 sysconf.c (_SC_NPROCESSORS_* → emscripten_num_logical_cores)
  - id: mdn-perf-now
    resource: https://developer.mozilla.org/en-US/docs/Web/API/Performance/now
    title: MDN — Performance.now() (5 µs isolated / 100 µs non-isolated resolution)
  - id: emcc-link-rsp
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.netcore.app.runtime.mono.browser-wasm/runtimes/browser-wasm/native/src/emcc-link.rsp
    title: emcc-link.rsp (FORCE_FILESYSTEM, STACK_SIZE, memory growth)
  - id: illink-subst
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.sdk/Sdk/ILLink.Substitutions.WasmIntrinsics.xml
    title: ILLink substitutions (Vector128/Vector/PackedSimd hardware acceleration)
  - id: sig-classification
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/sig-classification.txt
    title: Prebuilt runtime thunk inventory (includes TimerQueue.TimerHandler / ThreadPool.BackgroundJobHandler)
  - id: uno-wgpu-wasm-targets
    resource: https://github.com/unoplatform/uno/blob/master/src/Uno.UI.Composition.WebGpu.Init/wgpu-wasm.targets
    title: Uno wgpu-wasm.targets (notes on in-wasm event pumps hanging from managed stacks)
  - id: probe-build
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/proj/WasmProbe
    title: Throwaway project (generated runtimeconfig.json feature switches)
  - id: brutal-binding-doc
    resource: /brutal/vulkan-binding-architecture.md
    title: Brutal.Vulkan binding architecture (loader throws on browser; nint timeouts; WHOLE_SIZE)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:08:02Z }
status: draft
---

# The one rule

The browser main thread is the only thread in the default .NET 10 runtime. Every C# frame, interop call, timer callback and `await` continuation runs on that thread. **Nothing may block.** The browser only paints, delivers input and resolves WebGPU promises after control returns to the JS event loop.[^wasm-threads-md][^wasm-features-md]

# Frame loop patterns

## A. JS-driven tick (recommended)

```js
// main.js
const api = await dotnet.create();
api.setModuleImports('game', { /* WebGPU shim functions … */ });
const exports = await api.getAssemblyExports(api.getConfig().mainAssemblyName);
await api.runMain();                       // C# Main initialises and returns; runtime stays alive
function frame(t) { exports.Game.Host.Tick(t); requestAnimationFrame(frame); }
requestAnimationFrame(frame);
```

```csharp
public static partial class Host {
    [JSExport] public static void Tick(double timestampMs) { /* update + record + submit; never block */ }
}
```

- `runMain()` "will keep the .NET runtime alive and the APIs will be available for further calls". `run()`/`runMainAndExit()` exit the runtime afterwards.[^dotnet-d-ts]
- Each frame costs one synchronous `[JSExport]` entry. The `requestAnimationFrame` timestamp drives timing, and the browser throttles it in background tabs.

## B. C#-driven async loop

```csharp
[JSImport("nextFrame", "game")] static partial Task<double> NextFrame();   // JS: () => new Promise(r => requestAnimationFrame(r))
while (running) { double t = await NextFrame(); Tick(t); }
```

This works because each `await` yields to the event loop. It costs a `Promise` + `Task` + `GCHandle` per frame ("significant performance overhead" types, see [/dotnet-wasm/js-interop-jsimport-jsexport.md](/dotnet-wasm/js-interop-jsimport-jsexport.md)). The template's `while (true) { …; await Task.Delay(1000); }` is the same idea, but timer-driven rather than vsync-driven.[^template-nupkg] It is fine for prototypes; A is simpler to reason about.

## C. `emscripten_set_main_loop` (not recommended)

It needs C code via `NativeFileReference` (wasm-tools + relink), and a C callback that calls a UCO C# method. With `simulate_infinite_loop=1` Emscripten unwinds the native stack by throwing, which must not cross the Mono runtime's frames. UNVERIFIED: no one was found doing this with .NET; it adds nothing over A.

## Async WebGPU work inside a frame

`requestAdapter`, `requestDevice`, `mapAsync`, `onSubmittedWorkDone` and `popErrorScope` are Promises. Await them from C# (`[JSImport] Task …`) **outside** the synchronous tick, or start them in one tick and poll a completion flag in later ticks. Uno Platform's WebGPU port reports that an in-wasm event pump "hangs when driven from a managed call stack on the browser" and that a "DevicePoll busy-spin can't yield for the map to complete". Uno creates the device in JS and does map/readback through JS promises.[^uno-wgpu-wasm-targets]

**Vulkan semantics consequence.** `vkWaitForFences`, `vkQueueWaitIdle`, `vkDeviceWaitIdle` and `vkAcquireNextImageKHR` with an infinite timeout cannot block in the browser. The Vulkan-on-WebGPU layer must return immediately: report signalled fences optimistically, or `VK_TIMEOUT`/`VK_NOT_READY`, and let the next tick observe completion. BRUTAL passes `-1` (`nint`) as the timeout in places.[^brutal-binding-doc] See [/webgpu/](/webgpu/) for the WebGPU-side design.

# What blocking and threading APIs do (single-threaded runtime)

| API | Behaviour | Evidence |
|---|---|---|
| `new Thread(...).Start()` | `[UnsupportedOSPlatform("browser")]` in the default ref assemblies; throws `PlatformNotSupportedException` | [^thread-ref] |
| `Task.Wait()`, `.Result`, `Monitor.Wait(obj)` on something not yet complete | infinite monitor waits throw `PlatformNotSupportedException: Cannot wait on monitors on this runtime.` | [^monitor-c] |
| `Thread.Sleep(ms)` | Mono calls `clock_nanosleep` → Emscripten. UNVERIFIED: expected to **busy-wait the main thread** (freezes rendering). Never use in a loop | [^mono-threads-c] |
| `Task.Run`, `ThreadPool.QueueUserWorkItem` | allowed, but the "thread pool" is pumped as background jobs **on the main thread** after the current JS turn: concurrency, no parallelism | [^wasm-threads-md][^sig-classification] |
| `Parallel.For/ForEach` | run on the calling thread. If a replicated task would need to yield, throws `Exception("Replicated tasks cannot yield in this single-threaded browser environment")` | [^task-replicator] |
| `Timer`, `Task.Delay` | implemented with JS timers; the runtime ships the `TimerQueue.TimerHandler` native→interp thunk | [^sig-classification] |
| `lock`, `Interlocked`, `Volatile` | work (uncontended). BRUTAL's `Queue` uses `System.Threading.Lock`, harmless single-threaded | [^brutal-binding-doc] |
| `SynchronizationContext.Current` | UNVERIFIED for single-threaded; the multithreaded runtime installs a `JSSynchronizationContext` | [^wasm-threads-md] |

## Multithreading (`WasmEnableThreads=true`) — experimental

- Requires the `Cross-Origin-Embedder-Policy: require-corp` + `Cross-Origin-Opener-Policy: same-origin` headers; `[JSImport]/[JSExport]` stay main-thread only; "Blocking on the main thread with operations like `Task.Wait` or `Monitor.Enter` are not supported by browsers and are very dangerous. The work on the proper design for this is still in progress."[^wasm-features-md]
- WebGPU and the DOM are main-thread (or `JSWebWorker`) bound, so worker threads could only do CPU work (physics, asset decoding). **Out of scope for the PoC.**

# Porting checklist (desktop C# framework → browser-wasm)

| Item | Browser-wasm behaviour (.NET 10) | Evidence |
|---|---|---|
| `OperatingSystem.IsBrowser()` | `true`; `IsWindows/IsLinux/IsMacOS` all `false`. **Brutal.Vulkan's static loader throws `TypeInitializationException`** because no branch matches | [^brutal-binding-doc] |
| `RuntimeInformation.OSDescription` | `"Browser"` | [^runtimeinfo-browser] |
| `RuntimeInformation.OSArchitecture` | `Architecture.Wasm` (UNVERIFIED for `ProcessArchitecture`, expected `Wasm`) | [^runtimeinfo-browser] |
| `IntPtr.Size` / `nint` | 4 (wasm32). `nint`-typed 64-bit quantities truncate (BRUTAL `VK.WHOLE_SIZE`, timeouts) | [^brutal-binding-doc] |
| `Environment.ProcessorCount` | expected **1**: Mono → `sysconf(_SC_NPROCESSORS_*)` → `emscripten_num_logical_cores()`, which returns 1 in single-threaded Emscripten builds. UNVERIFIED by running | [^emscripten-sysconf][^emscripten-pthread-stub] |
| `NativeMemory.Alloc` / `Marshal.AllocHGlobal` | `malloc` in wasm linear memory (`_malloc`/`_free` are exported); grows the heap up to `EmccMaximumHeapSize` (2 GB default) | [^emcc-link-rsp] |
| `stackalloc` | the C stack is 5 MB (`STACK_SIZE=5MB`, `EmccStackSize`). UNVERIFIED whether interpreter `localloc` uses that stack or interpreter frame memory; keep large buffers off the stack | [^emcc-link-rsp] |
| SIMD | `Vector128.IsHardwareAccelerated`, `Vector.IsHardwareAccelerated`, `PackedSimd.IsSupported` are trimmed to `true` (SIMD on). No 256-bit SIMD; UNVERIFIED value of `Vector256.IsHardwareAccelerated` (expected `false`) | [^illink-subst] |
| `unsafe`, pointers, `fixed`, function pointers | supported. **Unmanaged function pointers to C# need build-time thunks**, see [/dotnet-wasm/function-pointers-and-native-callbacks.md](/dotnet-wasm/function-pointers-and-native-callbacks.md) | — |
| `[ModuleInitializer]`, static ctors | run normally. An exception in a static ctor surfaces as `TypeInitializationException` on every later access (BRUTAL loader) | [^brutal-binding-doc] |
| `Console.WriteLine` / `Console.Error` | appear in the browser DevTools console. The template's `Console.WriteLine("Hello, Browser!")` relies on this; the Emscripten module exposes `out`/`err` printers (UNVERIFIED: exact routing of stderr) | [^template-nupkg][^dotnet-d-ts] |
| `System.IO.File` | Emscripten in-memory FS (`FORCE_FILESYSTEM=1`), empty except preloaded assets (`WasmFilesToIncludeInFileSystem`/`vfs` assets), **not persistent**. Load game assets with `HttpClient` or `fetch` instead | [^emcc-link-rsp] |
| `HttpClient` | browser `fetch`. CORS applies; no raw sockets. .NET 10 streams responses by default (`BrowserHttpReadStream`; synchronous `Read` unsupported; opt out with `WasmEnableStreamingResponse=false`) | [^wasm-features-md] |
| WebSockets | browser `WebSocket` API | [^wasm-features-md] |
| `Environment.Exit` | exits the runtime; JS API calls are then rejected | [^dotnet-d-ts] |
| `Stopwatch`, `DateTime` | expected to be backed by `performance.now()` through Emscripten's clock (UNVERIFIED chain). Resolution is 5 µs when cross-origin isolated, else 100 µs | [^mdn-perf-now] |
| Time zones / culture | bundled TZ database and sharded ICU by default; `InvariantGlobalization`/`InvariantTimezone` drop them | [^wasm-features-md] |
| Reflection / `Reflection.Emit` | works (interpreted); `RuntimeFeature.IsDynamicCodeSupported=true`. Full trimming is on for publish and **trim warnings are suppressed by default**, so reflection over BRUTAL types can break silently in Release | [^probe-build] |
| Exception messages | `UseSystemResourceKeys=true` by default, so messages are resource keys, not prose | [^probe-build] |
| Native libraries (GLFW, OpenAL, FreeType, ImGui native, …) | no `dlopen`; each must be replaced by a managed/JS implementation or statically linked with wasm-tools (see [/dotnet-wasm/native-linking-and-emscripten.md](/dotnet-wasm/native-linking-and-emscripten.md) and [/brutal/](/brutal/)) | — |

## Open questions

- UNVERIFIED: `Thread.Sleep` behaviour (busy-wait vs other) on the main thread in Mono-wasm.
- UNVERIFIED: `SynchronizationContext.Current` in the single-threaded runtime.
- UNVERIFIED: `Environment.ProcessorCount` and `Vector256.IsHardwareAccelerated` values at run time.
- UNVERIFIED: interpreter `localloc`/`stackalloc` limits.
- UNVERIFIED: whether `emscripten_set_main_loop` can be used safely with the .NET runtime.
- UNVERIFIED: stderr routing of `Console.Error`.

[^template-nupkg]: wasmbrowser template Program.cs (10.0.13)
[^dotnet-d-ts]: dotnet.d.ts (runtime pack 10.0.13)
[^wasm-features-md]: dotnet/runtime src/mono/wasm/features.md (release/10.0)
[^wasm-threads-md]: dotnet/runtime src/mono/wasm/threads.md (release/10.0)
[^monitor-c]: Mono monitor.c (release/10.0)
[^mono-threads-c]: Mono mono-threads.c (release/10.0)
[^thread-ref]: System.Threading.Thread ref source (release/10.0)
[^task-replicator]: TaskReplicator.cs (dotnet/runtime main)
[^runtimeinfo-browser]: RuntimeInformation.Browser.cs (dotnet/runtime main)
[^emscripten-pthread-stub]: Emscripten 3.1.56 library_pthread_stub.c
[^emscripten-sysconf]: Emscripten 3.1.56 sysconf.c
[^mdn-perf-now]: MDN — Performance.now()
[^emcc-link-rsp]: runtime pack emcc-link.rsp (10.0.13)
[^illink-subst]: ILLink.Substitutions.WasmIntrinsics.xml (10.0.13)
[^sig-classification]: Prebuilt runtime thunk inventory (scratchpad r1)
[^uno-wgpu-wasm-targets]: Uno wgpu-wasm.targets
[^probe-build]: WasmProbe generated runtimeconfig.json / boot config
[^brutal-binding-doc]: /brutal/vulkan-binding-architecture.md
