---
type: Reference
title: JS interop on .NET 10 browser-wasm — [JSImport]/[JSExport], type marshalling, zero-copy memory access and call cost
description: How C# calls JavaScript and vice versa with System.Runtime.InteropServices.JavaScript in .NET 10 (supported types, MemoryView semantics, passing wasm pointers to JS without copies, memory-growth hazards, async/Promise mapping, lifetimes, threading rules, published per-call cost), with guidance for a WebGPU translation layer.
tags: [dotnet-wasm, jsimport, jsexport, interop, marshalling, memoryview, webgpu, performance]
sources:
  - id: ms-dotnet-interop
    resource: https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/?view=aspnetcore-10.0
    title: JavaScript [JSImport]/[JSExport] interop in .NET WebAssembly (Microsoft Learn, .NET 10; updated 2026-10-08)
  - id: ms-blazor-jsimport
    resource: https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/import-export-interop?view=aspnetcore-10.0
    title: JavaScript JSImport/JSExport interop with ASP.NET Core Blazor (.NET 10)
  - id: ms-wasm-browser-app
    resource: https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/wasm-browser-app?view=aspnetcore-10.0
    title: JSImport/JSExport interop with a WebAssembly Browser App project (.NET 10)
  - id: js-test-helper
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Runtime.InteropServices.JavaScript/tests/System.Runtime.InteropServices.JavaScript.UnitTests/System/Runtime/InteropServices/JavaScript/JavaScriptTestHelper.cs
    title: Runtime unit-test helper with [JSImport] Span<byte>/ArraySegment<byte> MemoryView signatures (release/10.0)
  - id: marshaler-single-10
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Runtime.InteropServices.JavaScript/src/System/Runtime/InteropServices/JavaScript/Marshaling/JSMarshalerArgument.Single.cs
    title: JSMarshalerArgument.Single.cs on release/10.0 (scalar float only)
  - id: marshaler-single-main
    resource: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Runtime.InteropServices.JavaScript/src/System/Runtime/InteropServices/JavaScript/Marshaling/JSMarshalerArgument.Single.cs
    title: JSMarshalerArgument.Single.cs on main (adds float[], ArraySegment<float>, Span<float>)
  - id: marshaler-byte-10
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Runtime.InteropServices.JavaScript/src/System/Runtime/InteropServices/JavaScript/Marshaling/JSMarshalerArgument.Byte.cs
    title: JSMarshalerArgument.Byte.cs on release/10.0 (byte[], ArraySegment<byte>, Span<byte>)
  - id: dotnet-d-ts
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.netcore.app.runtime.mono.browser-wasm/runtimes/browser-wasm/native/dotnet.d.ts
    title: dotnet.d.ts (runtime pack 10.0.13) — IMemoryView, localHeapView*, setModuleImports, getAssemblyExports
  - id: dotnet-native-js
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.netcore.app.runtime.mono.browser-wasm/runtimes/browser-wasm/native/dotnet.native.js
    title: dotnet.native.js (Emscripten 3.1.56 glue; updateMemoryViews assigns Module.HEAP*)
  - id: emcc-link-rsp
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.netcore.app.runtime.mono.browser-wasm/runtimes/browser-wasm/native/src/emcc-link.rsp
    title: emcc-link.rsp (ALLOW_MEMORY_GROWTH=1, EXPORTED_RUNTIME_METHODS)
  - id: wasm-features-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/features.md
    title: Configuring and hosting .NET WebAssembly applications (release/10.0)
  - id: wasm-threads-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/threads.md
    title: Threaded runtime notes (JSSynchronizationContext, JSWebWorker)
  - id: browserwasmapp-targets
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r1/pkgs/microsoft.net.runtime.webassembly.sdk/Sdk/BrowserWasmApp.targets
    title: BrowserWasmApp.targets 10.0.13 (WasmEnableJsInteropByValue)
  - id: jsfunctionbinding-search
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Runtime.InteropServices.JavaScript/src/System/Runtime/InteropServices/JavaScript/JSFunctionBinding.cs
    title: JSFunctionBinding.cs (guards on RuntimeInformation.OSArchitecture == Wasm)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:02:51Z }
status: draft
---

# Essentials

- Namespace `System.Runtime.InteropServices.JavaScript`. A **source generator** emits the marshalling for `[JSImport]` (`static partial` methods without a body) and `[JSExport]` (static methods). The project needs `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`.[^ms-dotnet-interop][^ms-wasm-browser-app]
- **Import** (C# → JS): `[JSImport("fnName", "moduleName")]`. `moduleName` must match either `setModuleImports("moduleName", {...})` on the JS side or a module loaded with `await JSHost.ImportAsync("moduleName", "/path/module.js")`. A function on the global object needs no module, e.g. `[JSImport("globalThis.console.log")]`.[^ms-dotnet-interop][^ms-blazor-jsimport]
- **Export** (JS → C#): `[JSExport] static T M(...)`. JS gets it with `const exports = await getAssemblyExports("MyAssembly")` (or `"MyAssembly.dll"`) and calls `exports.Namespace.Class.M(...)` **synchronously**.[^ms-dotnet-interop][^dotnet-d-ts]
- Calls are **synchronous** in both directions. A JS function that returns a `Promise` maps to a C# `Task`/`Task<T>`. Do not mark the `[JSImport]` method `async`; return `Task`. A `[JSExport]` returning `Task` gives JS a `Promise`.[^ms-dotnet-interop]
- `Action`/`Func<…>` (up to 3 type args) marshal as callable JS functions. `JSObject` is a proxy to a JS object and `Exception` maps to `Error`.[^ms-dotnet-interop]
- The runtime refuses to bind outside wasm: `JSFunctionBinding` checks `RuntimeInformation.OSArchitecture == Architecture.Wasm`. Guard shared code with `OperatingSystem.IsBrowser()`.[^jsfunctionbinding-search][^ms-blazor-jsimport]

# Type mappings (.NET 10)

Abridged from the official table for .NET ≤ 10 (✅ = supported):[^ms-dotnet-interop]

| .NET | JS | Nullable | `Task`→`Promise` | `JSMarshalAs` optional | Array of |
|---|---|---|---|---|---|
| `Boolean` | Boolean | ✅ | ✅ | ✅ | — |
| `Byte` | Number | ✅ | ✅ | ✅ | ✅ (`byte[]`) |
| `Char`, `Int16` | Number | ✅ | ✅ | ✅ | — |
| `Int32` | Number | ✅ | ✅ | ✅ | ✅ (`int[]`) |
| `Int64` | Number **or** BigInt | ✅ | ✅ | **required** (`JSType.Number` / `JSType.BigInt`) | — |
| `Single` | Number | ✅ | ✅ | ✅ | **— (not in .NET 10)** |
| `Double` | Number | ✅ | ✅ | ✅ | ✅ (`double[]`) |
| `IntPtr` | Number | ✅ | ✅ | ✅ | — |
| `DateTime`, `DateTimeOffset` | Date | ✅ | ✅ | — | — |
| `Exception` | Error | — | ✅ | ✅ | — |
| `JSObject` | Object | — | ✅ | ✅ | ✅ |
| `String` | String | — | ✅ | ✅ | ✅ |
| `Object` | Any | — | ✅ | — | ✅ |
| `Span<Byte/Int32/Double>` | MemoryView | — | — | — | — |
| `ArraySegment<Byte/Int32/Double>` | MemoryView | — | — | — | — |
| `Task` | Promise | — | — | ✅ | — |
| `Action…`, `Func…` (≤3 args) | Function | — | — | — | — |

**`float[]`, `Span<float>`, `ArraySegment<float>` are not available in .NET 10.** `JSMarshalerArgument.Single.cs` on `release/10.0` has only scalar/nullable `float`. `main` (.NET 11) adds `float[]`, `ArraySegment<float>` and `Span<float>`, and the docs' .NET 11 table lists them.[^marshaler-single-10][^marshaler-single-main][^ms-dotnet-interop] For vertex/uniform data in .NET 10, reinterpret as bytes (`MemoryMarshal.AsBytes(span)` → `Span<byte>`) or pass a pointer + length (below).

Rules from the docs:[^ms-dotnet-interop]
- "`JSObject`, `Exception`, `Task` and `ArraySegment` create `GCHandle` and a proxy … These types carry significant performance overhead."
- "Marshaling an array creates a copy of the array in JS or .NET."
- `MemoryView` wraps `Span`/`ArraySegment` **without copying**. A `Span` MemoryView "is only valid for the duration of the interop call". An `ArraySegment` MemoryView survives the call and pins the array until JS calls `dispose()`.
- Nested generics in `JSMarshalAs` (e.g. `Promise<Array<Number>>`) are a compile-time error. Workaround: return `JSObject` and unwrap it in a second call.

Note on a doc inconsistency: the docs say "it isn't possible to import a JS method as a .NET method that has a parameter of `Span` or `ArraySegment`". The runtime's own tests declare exactly that: `[JSImport("echo1view", …)] … echo1_SpanOfByte([JSMarshalAs<JSType.MemoryView>] Span<byte> value, …)`. `JSMarshalerArgument` on `release/10.0` has `ToJS(Span<byte>)`/`ToJS(ArraySegment<byte>)`.[^js-test-helper][^marshaler-byte-10] **Spans can be passed to `[JSImport]`.** What is impossible is a `[JSExport]` that receives or returns a `Span`, because JS cannot construct a MemoryView.

`IMemoryView` (JS side) has `set(source, targetOffset?)`, `copyTo(target, sourceOffset?)`, `slice(start?, end?)`, `length`, `byteLength`, `dispose()` and `isDisposed`.[^dotnet-d-ts]

# Zero-copy: handing wasm memory to JS

For bulk data such as command streams, vertex uploads and texture data, pass **a pointer and a length as `IntPtr`/`int`** and let JS view the heap directly:

```csharp
[JSImport("queueWriteBuffer", "webgpu-shim")]
internal static partial void QueueWriteBuffer(int bufferId, double offset, nint ptr, int byteLength);

unsafe void Upload(ReadOnlySpan<byte> data) {
    fixed (byte* p = data) QueueWriteBuffer(id, 0, (nint)p, data.Length);   // pinned only for the call
}
```

```js
// main.js — `api` is the RuntimeAPI returned by dotnet.create()
setModuleImports('webgpu-shim', {
  queueWriteBuffer: (id, offset, ptr, len) => {
    const heap = api.localHeapViewU8();               // fresh view each call
    device.queue.writeBuffer(buffers[id], offset, heap.buffer, ptr, len); // or heap.subarray(ptr, ptr+len)
  }
});
```

Facts behind this pattern:
- `localHeapViewU8()` and friends return "a short term view of the WASM linear memory. Don't store the reference, don't use it after await."[^dotnet-d-ts]
- Memory can grow (`-s ALLOW_MEMORY_GROWTH=1`).[^emcc-link-rsp] Growth replaces the underlying `ArrayBuffer`, so a cached `new Uint8Array(buffer, ptr, len)` becomes detached and reads zero length. Emscripten's `updateMemoryViews` re-assigns `Module.HEAPU8` and the other `HEAP*` views after growth (`Module["HEAPU8"]=HEAPU8=new Uint8Array(b)`).[^dotnet-native-js] **Re-acquire views on every call**, and never across an `await` or any call back into .NET (which can allocate and grow memory).
- `Module.HEAP*` exist at run time but are not typed in `dotnet.d.ts`. Prefer `localHeapView*()` / `getHeap*()` / `setHeap*()`.[^dotnet-d-ts]
- Addresses must stay stable for the duration of use. Use `fixed`/`stackalloc` for per-call data, and `NativeMemory.Alloc` or a pinned `GCHandle` for buffers JS reads later. UNVERIFIED for Mono-wasm specifically: that SGen's nursery copying can move unpinned objects. Treat managed arrays as movable unless pinned.
- With threads enabled the heap is a `SharedArrayBuffer`. UNVERIFIED: which WebGPU entry points accept shared views (out of scope; single-threaded recommended).

# Cost per call

- The only Microsoft number: a loop calling a `[JSImport]` method that takes a `JSObject`, 1,000,000 times, printed `.000254 ms per operation` (≈254 ns). The same work in pure .NET took `.000021 ms`. Creating a JS object and calling it took `.002169 ms` per iteration. The device is unspecified. Microsoft's summary: "interop operations are roughly an order of magnitude slower than those that remain within the .NET boundary, but … remain relatively fast."[^ms-dotnet-interop]
- Interop calls made from interpreted C# also pay interpreter overhead; AOT reduces the C# side only. UNVERIFIED: no published per-call numbers exist for primitive-only signatures, or for interpreter vs AOT. The experiments in [/experiments/](/experiments/) should measure them.
- **Design implication for Vulkan-on-WebGPU.** Vulkan command recording (`vkCmd*`) should not become one JSImport per command. Encode commands into a C#-side byte buffer and flush once per `vkQueueSubmit` with one `[JSImport]` carrying `(ptr, len)`. A JS decoder then replays them onto a `GPUCommandEncoder`/`GPURenderPassEncoder`. Object creation (`vkCreate*`) is rare and can be one call each. The [/webgpu/](/webgpu/) notes cover the JS side.

# Lifetimes

- `JSObject` implements `IDisposable`. Dispose it deterministically. JS and .NET memory pressure "isn't shared", so GC alone may never collect proxies.[^ms-dotnet-interop]
- `ArraySegment` MemoryViews pin until `dispose()`; use `try/finally` in JS.[^ms-dotnet-interop]
- `Action`/`Func` passed to JS stay alive as long as JS holds them. UNVERIFIED: explicit release semantics for delegates held by JS.
- Keep WebGPU objects in a JS-side table indexed by small integers (the "handle" C# stores in the Vulkan handle struct). Then no `JSObject` proxies are needed for the hot path.

# Threading rules

- Single-threaded runtime (default): everything, including every interop call, happens on the browser main thread.
- Multi-threaded runtime: "JavaScript interop with managed code via `[JSExport]`/`[JSImport]` is currently limited to the main thread even if multi-threading support is enabled".[^wasm-features-md] JS objects have thread (worker) affinity. A `JSSynchronizationContext` keeps continuations on the owning thread, and a prototype `JSWebWorker` API exists for dedicated JS-capable threads.[^wasm-threads-md] `WasmEnableJsInteropByValue` defaults to `true` with threads.[^browserwasmapp-targets]

# Errors

A JS exception thrown inside a `[JSImport]` surfaces in C# as `JSException`. A C# exception escaping a `[JSExport]` becomes a JS `Error`. UNVERIFIED: exact type names and stack-trace preservation in .NET 10 were not tested.

# Legacy APIs (note only)

Blazor's `IJSRuntime`/`IJSInProcessRuntime` remain the recommended interop *inside Blazor components*. `IJSUnmarshalledRuntime` has been obsolete since .NET 7 and is replaced by `[JSImport]/[JSExport]`.[^ms-blazor-jsimport] Neither applies to a non-Blazor game host.

## Open questions

- UNVERIFIED: per-call cost of primitive-only `[JSImport]` calls (interpreter vs AOT) on the target machine.
- UNVERIFIED: whether Mono-wasm's GC moves unpinned arrays during a JS call that allocates.
- UNVERIFIED: WebGPU acceptance of `SharedArrayBuffer`-backed views (threads build).
- UNVERIFIED: release semantics of delegates passed to JS; `JSException` details.

[^ms-dotnet-interop]: Microsoft Learn — JS [JSImport]/[JSExport] interop in .NET WebAssembly (.NET 10)
[^ms-blazor-jsimport]: Microsoft Learn — JSImport/JSExport interop with Blazor (.NET 10)
[^ms-wasm-browser-app]: Microsoft Learn — WebAssembly Browser App project (.NET 10)
[^js-test-helper]: dotnet/runtime JavaScriptTestHelper.cs (release/10.0)
[^marshaler-single-10]: JSMarshalerArgument.Single.cs (release/10.0)
[^marshaler-single-main]: JSMarshalerArgument.Single.cs (main)
[^marshaler-byte-10]: JSMarshalerArgument.Byte.cs (release/10.0)
[^dotnet-d-ts]: dotnet.d.ts (runtime pack 10.0.13)
[^dotnet-native-js]: dotnet.native.js (runtime pack 10.0.13)
[^emcc-link-rsp]: emcc-link.rsp (runtime pack 10.0.13)
[^wasm-features-md]: dotnet/runtime src/mono/wasm/features.md (release/10.0)
[^wasm-threads-md]: dotnet/runtime src/mono/wasm/threads.md (release/10.0)
[^browserwasmapp-targets]: BrowserWasmApp.targets 10.0.13
[^jsfunctionbinding-search]: dotnet/runtime JSFunctionBinding.cs (release/10.0)
