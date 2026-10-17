---
type: Research Note
title: Main loop patterns for a BRUTAL game in .NET wasm (rAF tick, async loops, Asyncify, JSPI)
description: How to drive a BRUTAL frame loop in the browser without blocking, how the reference game's blocking loop and nested frame pumps must be restructured, how to start, stop and recover the runtime, and why Asyncify and JSPI are not recommended for the PoC.
tags: [main-loop, requestanimationframe, jsexport, asyncify, jspi, dotnet-wasm, game-loop, runmain, glfw]
sources:
  - id: dotnet-dts
    resource: https://raw.githubusercontent.com/dotnet/runtime/release/10.0/src/mono/browser/runtime/dotnet.d.ts
    title: dotnet/runtime release/10.0 dotnet.d.ts (host builder and runtime API docs)
  - id: dotnet-scheduling
    resource: https://raw.githubusercontent.com/dotnet/runtime/release/10.0/src/mono/browser/runtime/scheduling.ts
    title: dotnet/runtime release/10.0 scheduling.ts (single-threaded timers and background jobs)
  - id: dotnet-features
    resource: https://raw.githubusercontent.com/dotnet/runtime/release/10.0/src/mono/wasm/features.md
    title: dotnet/runtime release/10.0 src/mono/wasm/features.md (threading, blocking, templates)
  - id: dotnet-targets
    resource: https://raw.githubusercontent.com/dotnet/runtime/release/10.0/src/mono/browser/build/BrowserWasmApp.targets
    title: dotnet/runtime release/10.0 BrowserWasmApp.targets (no JSPI/Asyncify properties found)
  - id: ms-jsinterop
    resource: https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/?view=aspnetcore-10.0
    title: JavaScript JSImport/JSExport interop in .NET WebAssembly (aspnetcore-10.0)
  - id: emsc-asyncify
    resource: https://emscripten.org/docs/porting/asyncify.html
    title: Emscripten - Asynchronous Code (Asyncify and JSPI)
  - id: v8-jspi
    resource: https://v8.dev/blog/jspi
    title: V8 blog - JavaScript Promise Integration
  - id: caniuse-jspi
    resource: https://caniuse.com/wf-wasm-jspi
    title: Can I use - JavaScript promise integration (WebAssembly)
  - id: coreclr-wasm
    resource: https://github.com/dotnet/runtime/issues/121511
    title: dotnet/runtime #121511 - .NET WebAssembly migration from Mono to CoreCLR
  - id: mdn-raf
    resource: https://developer.mozilla.org/en-US/docs/Web/API/Window/requestAnimationFrame
    title: MDN Window.requestAnimationFrame()
  - id: mdn-settimeout
    resource: https://developer.mozilla.org/en-US/docs/Web/API/Window/setTimeout
    title: MDN Window.setTimeout()
  - id: mdn-getcurrenttexture
    resource: https://developer.mozilla.org/en-US/docs/Web/API/GPUCanvasContext/getCurrentTexture
    title: MDN GPUCanvasContext.getCurrentTexture()
  - id: pongasoft-usage
    resource: https://raw.githubusercontent.com/pongasoft/emscripten-glfw/master/docs/Usage.md
    title: contrib.glfw3 docs/Usage.md (Events and main loop)
  - id: sdl-emscripten
    resource: https://raw.githubusercontent.com/libsdl-org/SDL/main/docs/README-emscripten.md
    title: SDL docs/README-emscripten.md (main loop options)
  - id: ksa-app
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/Core/App.cs
    title: KSA Core/App.cs (reference only)
  - id: ksa-program
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA/Program.cs
    title: KSA KSA/Program.cs (reference only)
  - id: ksa-loading
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA/Loading.cs
    title: KSA KSA/Loading.cs (reference only)
  - id: ksa-audio
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA/GameAudio.cs
    title: KSA KSA/GameAudio.cs (reference only)
  - id: brutal-glfw-window
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Glfw/Manual/Libraries/Glfw.Window.cs
    title: Brutal.Glfw Glfw.Window.cs (PollEvents, WaitForWindow)
  - id: emsc-libopenal
    resource: https://raw.githubusercontent.com/emscripten-core/emscripten/main/src/lib/libopenal.js
    title: Emscripten libopenal.js (setInterval-based scheduling)
  - id: sib-webgpu-sync
    resource: /webgpu/synchronization-and-async-shape.md
    title: Synchronization and the async API shape (sibling note)
  - id: sib-jsinterop
    resource: /dotnet-wasm/js-interop-jsimport-jsexport.md
    title: JS interop on .NET 10 browser-wasm (sibling note)
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:08:04Z }
status: draft
stale_after: 2027-01-31
---

# Problem statement

A native BRUTAL game owns the thread: `while (!window.ShouldClose) { Glfw.PollEvents(); Frame(); }`. In a browser the page's single main thread must return to the event loop for input events to be delivered, for the canvas to be composited and for timers (including the OpenAL-on-Web-Audio scheduler) to fire. .NET's own features note says blocking on the main thread (`Task.Wait`, `Monitor.Enter`) is "not supported by browsers and very dangerous", and JS interop is main-thread only.[^dotnet-features]

# What the reference game does (and what must change)

| Native pattern | Where | Browser consequence |
| --- | --- | --- |
| Blocking `while (!_exitCode.HasValue) { OnFrame(dt); if (FrameLimit > 0) Thread.Sleep(...) }` | `Core.App.Run()`[^ksa-app] | Split into `Tick(dt)`; drop `Thread.Sleep` (rAF already paces to the display) |
| `OnFrame` returns `OnFrameResult.Exit/Continue`; exit by `RequestExit` | `Program.OnFrame`[^ksa-program] | `Exit` stops the rAF chain and shows a "stopped" state |
| `Glfw.PollEvents()` once per frame at a fixed point, `_window.ShouldClose` check | `Program.OnFrame`[^ksa-program] | Works unchanged with the pull-queue shim ([input design](/io-audio/managed-browser-input-shim-design.md)) |
| Nested synchronous frame pumps during startup: `new Loading()` calls `OnFrame()` which does `PollEvents`, builds ImGui, acquires a swapchain frame, renders and presents, repeatedly while heavy init runs on the same thread | `Loading.OnFrame`, `ConfirmMods`, `SelectSystem`[^ksa-loading] | Presentation only happens when the task ends; N pumps in one task show at most the last frame (see "Presentation constraint"). Init must be turned into slices across ticks or an `async` coroutine awaiting the next frame |
| `Environment.Exit(0)` when `ShouldClose` inside pumps | `Loading.OnFrame`[^ksa-loading] | Replace by `Browser.Exit()` |
| Spin-waits with `Thread.Sleep(1)` (FMOD stream open) | `GameAudio.CreateFmodSound`[^ksa-audio] | Never block the main thread; make loads awaitable |
| GPU waits: `_renderer.WaitForPreviousPresent(...)`, `Device.WaitIdle()` | `Program`[^ksa-program] | WebGPU completion is asynchronous, so the Vulkan layer must not block: the sibling design signals upload/render-only fences at submit and returns without waiting, so these calls complete immediately[^sib-webgpu-sync] |
| Worker pools (`JobSystems.*`) | `Program`[^ksa-program] | Need wasm threads (experimental; COOP/COEP headers; JS interop still main-thread only) or a single-threaded job executor[^dotnet-features] |

# Pattern 1 (recommended): requestAnimationFrame calls a `[JSExport]` tick

```js
import { dotnet } from './_framework/dotnet.js';
const runtime = await dotnet.create();                     // starts the runtime, does NOT run Main
runtime.setModuleImports('brutal-browser', api);            // [JSImport] targets (input, audio, canvas)
const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
await exports.Poc.App.InitAsync();                          // async init (fetch assets) is fine here
let running = true, raf = 0;
function frame(t) {
  raf = 0;
  if (!running) return;
  try { if (!exports.Poc.App.Tick(t)) running = false; }    // Tick returns false on Exit
  catch (e) { running = false; reportFatal(e); return; }    // unhandled managed exception => stop, show error
  if (running) raf = requestAnimationFrame(frame);
}
raf = requestAnimationFrame(frame);
```

```csharp
public static partial class App
{
    [JSExport] public static async Task InitAsync() { /* create Vulkan-on-WebGPU device, load assets */ }
    [JSExport] public static bool Tick(double timestampMs)
    {
        var dt = Clock.Step(timestampMs);            // clamp after hidden periods
        Glfw.PollEvents();                           // drains the JS queue, raises callbacks
        return Game.OnFrame(dt) == FrameResult.Continue;
    }
}
```

Facts this relies on (the first two bullets from `dotnet.d.ts` on `release/10.0`, the rest as cited):

- `dotnet.run()` "runs the `Main()` method and exits the runtime ... after the runtime exits, it would reject all further calls to the API"; `runMain()` "will keep the .NET runtime alive and the APIs will be available for further calls". Using `create()` plus `getAssemblyExports`, or `runMain()`, is therefore required for a long-lived game; the stock template's `await dotnet.run()` would terminate the runtime when `Main` returns.[^dotnet-dts]
- `create()` returns a `RuntimeAPI` that also exposes the Emscripten `Module` object, `exit(code, reason)`, `setModuleImports`, `getConfig` and `getAssemblyExports`; `withMainAssembly(name)`, `withApplicationArguments(...)` and `withEnvironmentVariable(s)` configure the host builder.[^dotnet-dts]
- A `[JSExport]` method called from a rAF callback runs on the main thread and returns before the callback ends, so the browser can composite. Exceptions of managed code surface in JS as `Error` (the interop type table maps `Exception` to `Error`), so the JS `try/catch` above is the single place that decides whether to stop (the sibling interop note agrees: a C# exception escaping a `[JSExport]` becomes a JS `Error`, exact types untested);[^sib-jsinterop] by contrast an exception escaping a managed timer or thread-pool continuation reaches `mono_exit(1, ex)` in `scheduling.ts` and terminates the runtime.[^ms-jsinterop][^dotnet-scheduling] UNVERIFIED for synchronous `[JSExport]` exceptions in .NET 10.
- Keep `Tick` synchronous; an `async Task` export returns a Promise and creates a proxy per call (marshalling cost noted in the interop doc).[^ms-jsinterop]

Why rAF: it follows the display refresh (60/120/144 Hz are common), is paused in background tabs and gives a shared timestamp; animation must use the timestamp, otherwise it "will run faster on high refresh-rate screens".[^mdn-raf]

## Stop, pause, resume

- Stop: set `running = false`, `cancelAnimationFrame(raf)`; keep the runtime (to resume or inspect) or call `runtime.exit(0)`.
- Hidden tab: rAF stops by itself ("paused in most browsers when running in background tabs").[^mdn-raf] Do not simulate in the background; on becoming visible clamp the first `dt` (for example to 0.1-0.25 s) and let the input shim release keys. Anything that must keep ticking while hidden (network keep-alive, audio scheduling) needs timers, which are throttled to once per second (and once per minute under Chrome's intensive throttling) unless the page is playing audio.[^mdn-settimeout]
- HMR/dev server: tear down in reverse (cancel rAF, remove listeners, `runtime.exit`) before re-import.

## Presentation constraint (affects nested pumps)

Canvas presentation is implicit and happens at the browser's "update the rendering" step, i.e. only after the JS task (here the C# `Tick`) returns; the sibling webgpu note derives the frame shape from the WebGPU draft (acquire via `getCurrentTexture()`, record, submit, return).[^sib-webgpu-sync] MDN's `getCurrentTexture()` page itself says nothing about expiry.[^mdn-getcurrenttexture] Implication: all of acquire, record, submit and present for a frame should happen inside one `Tick`, and a synchronous `Loading.OnFrame()` pump inside a long init cannot animate (N pumps in one task present at most once).

# Pattern 2: `async` loop with `await Task.Yield()` / `Task.Delay` (not recommended)

On the single-threaded runtime, thread-pool work items (which is what `Task.Yield` continuations are, inferred) are scheduled by `Module.safeSetTimeout(fn, 0)` and managed timers (`Task.Delay`, `System.Threading.Timer`) by `Module.safeSetTimeout(tick, dueTime)`; the only throttling counter-measure in the runtime is a Chromium-only hack that spreads one-second timers for six minutes.[^dotnet-scheduling] Consequences:

- Not aligned with vsync: no knowledge of when the browser paints, so frame pacing judders (double-presents or skipped frames) and WebGPU's per-frame texture rule is easy to violate.
- Nested `setTimeout` is clamped to at least 4 ms after five nested calls and heavily throttled in background tabs.[^mdn-settimeout]
- `await` remains the right tool for loading (fetch, decode, `await Browser.NextFrame()` between slices), just not as the frame driver.

# Pattern 3: restructure the BRUTAL loop behind a small helper

Introduce a single abstraction so game code is identical on both targets:

```csharp
public static class FrameLoop
{
    public static void Run(Func<double, bool> frame);   // desktop: blocking while; browser: stores delegate, returns
    public static Task NextFrame();                     // completes at the start of the next Tick
    public static void Exit(int code = 0);
}
```

- Desktop: `while (frame(dt)) { }` with the original `Thread.Sleep` limiter.
- Browser: `Run` stores the delegate and returns immediately (so `Main` can return); the exported `Tick` calls it once, then completes the `NextFrame()` completion source *before* calling the delegate so coroutine-style init resumes inline at a known point in the frame.
- `Core.App.Run()` becomes `Run(frame)` plus a public `Tick`; `OnFrameResult.Exit` calls `Exit`.
- Nested pumps such as `Loading.OnFrame()` called from synchronous init code are rewritten as `async Task` init with `await FrameLoop.NextFrame()` at each pump point (or an explicit "run N ms of work, then yield" budget), so each slice runs inside a normal tick and presents.
- `Glfw.WaitForWindow()` (loops `WaitEvents` until the framebuffer is non-zero) must become "await first resize event" because `WaitEvents` cannot block in a browser.[^brutal-glfw-window]

# Pattern 4: Emscripten main loop (strategy A only)

If a native Emscripten GLFW/SDL is linked, the idiom is `emscripten_set_main_loop(fn, 0, ...)`, which uses `requestAnimationFrame`; contrib.glfw3 recommends calling `glfwPollEvents()` at the *top* of that function because it applies pending canvas resizes.[^pongasoft-usage][^sdl-emscripten] The loop function would be an `[UnmanagedCallersOnly]` managed method; functionally identical to Pattern 1 but with an extra native hop and a dependency on [strategy A prerequisites](/io-audio/emscripten-glfw-options.md). Note that Emscripten's OpenAL schedules audio from a 25 ms `setInterval`, so any blocking loop starves audio too.[^emsc-libopenal]

# Pattern 5: Asyncify (`-sASYNCIFY`) to keep a blocking `while` loop

What it is: a compile-time transformation that lets synchronous wasm code pause and resume around async JavaScript; `emscripten_sleep` yields to the event loop so timers and events fire during a synchronous loop. Costs and rules from Emscripten's docs: roughly 50% code-size and speed overhead, whole-program analysis limited by indirect calls, imports that may suspend must be listed (`ASYNCIFY_IMPORTS`), manual `ASYNCIFY_ADD/REMOVE/ONLY` lists are "error-prone", and "it is not safe to start an async operation while another is already running".[^emsc-asyncify]

Assessment for .NET:

- The .NET browser targets in `release/10.0` expose no Asyncify or JSPI property (a search of `BrowserWasmApp.targets` and `WasmApp.Common.targets` found none); the only hook would be raw `EmccExtraLDFlags`, i.e. an unsupported configuration that also needs the native relink.[^dotnet-targets]
- Mono's interpreter and AOT code reach managed code through many indirect calls and deep C recursion, so the instrumentation would be both large and conservative. UNVERIFIED (reasoned from the docs above, not measured).
- JS->managed re-entry (an input event or JSExport arriving while the loop is suspended) is exactly the unsafe re-entrancy case the docs warn about.[^emsc-asyncify]
- Verdict: do not use Asyncify; the restructuring in Pattern 3 is small and keeps the PoC on the supported path.

# Pattern 6: JSPI

JSPI suspends wasm when it calls a Promise-returning import and resumes it from the task queue; no code-size cost, but imports and exports must be marked explicitly (`-sJSPI`, `JSPI_IMPORTS`, `JSPI_EXPORTS`), V8 requires Emscripten 3.1.61 or later, and "it is not permitted to cause JavaScript code to be suspended by using JSPI", i.e. JS frames cannot sit between the promising export and the suspending import.[^emsc-asyncify][^v8-jspi] .NET's `[JSImport]`/`[JSExport]` marshalling goes through JS trampolines, so a managed call that tries to suspend would cross JS frames. UNVERIFIED, inferred.

Browser status (Can I use): Chrome and Edge 137+, Firefox 153+ (desktop; Android 157), Safari 27 (macOS and iOS), Samsung Internet 30; the V8 blog separately says Firefox 139, so the Firefox figure is conflicting between sources.[^caniuse-jspi][^v8-jspi] No .NET property or documentation for JSPI was found; the runtime is also being migrated from Mono to CoreCLR for wasm (early preview targeted at the .NET 11 timeframe, interpreter first), so any Mono-specific JSPI work would not carry over.[^coreclr-wasm]

Verdict: not for the PoC. Revisit only if a large synchronous-blocking code path remains after the Pattern 3 refactor and the CoreCLR-wasm runtime documents JSPI support.

# Frame pacing and time

- `Core.App.FrameLimit` (default 200) and `Thread.Sleep` are meaningless in a browser; if a cap is wanted, skip rAF callbacks (`if (t - last < 1000/limit - 1) return`).[^ksa-app]
- Measure `dt` from the rAF timestamp (same origin as `performance.now()`), clamp it, and feed a fixed-step accumulator for physics; see the time section of [the input design](/io-audio/managed-browser-input-shim-design.md).[^mdn-raf]

## Open questions

- UNVERIFIED: exception propagation from synchronous `[JSExport]` methods in .NET 10, and whether an unhandled exception inside `Tick` leaves the runtime usable for a restart.
- UNVERIFIED: whether Mono wasm threads (COOP/COEP) are acceptable for the job systems, or a single-threaded executor is needed.
- Exact `WaitForPreviousPresent` behaviour in the Vulkan-on-WebGPU layer (the sibling design says non-blocking early-signalled fences).

[^dotnet-dts]: dotnet/runtime release/10.0 dotnet.d.ts (host builder and runtime API docs)
[^dotnet-scheduling]: dotnet/runtime release/10.0 scheduling.ts (single-threaded timers and background jobs)
[^dotnet-features]: dotnet/runtime release/10.0 src/mono/wasm/features.md (threading, blocking, templates)
[^dotnet-targets]: dotnet/runtime release/10.0 BrowserWasmApp.targets (no JSPI/Asyncify properties found)
[^ms-jsinterop]: JavaScript JSImport/JSExport interop in .NET WebAssembly (aspnetcore-10.0)
[^emsc-asyncify]: Emscripten - Asynchronous Code (Asyncify and JSPI)
[^v8-jspi]: V8 blog - JavaScript Promise Integration
[^caniuse-jspi]: Can I use - JavaScript promise integration (WebAssembly)
[^coreclr-wasm]: dotnet/runtime #121511 - .NET WebAssembly migration from Mono to CoreCLR
[^mdn-raf]: MDN Window.requestAnimationFrame()
[^mdn-settimeout]: MDN Window.setTimeout()
[^mdn-getcurrenttexture]: MDN GPUCanvasContext.getCurrentTexture()
[^pongasoft-usage]: contrib.glfw3 docs/Usage.md (Events and main loop)
[^sdl-emscripten]: SDL docs/README-emscripten.md (main loop options)
[^ksa-app]: KSA Core/App.cs (reference only)
[^ksa-program]: KSA KSA/Program.cs (reference only)
[^ksa-loading]: KSA KSA/Loading.cs (reference only)
[^ksa-audio]: KSA KSA/GameAudio.cs (reference only)
[^brutal-glfw-window]: Brutal.Glfw Glfw.Window.cs (PollEvents, WaitForWindow)
[^emsc-libopenal]: Emscripten libopenal.js (setInterval-based scheduling)
[^sib-webgpu-sync]: Synchronization and the async API shape (sibling note)
[^sib-jsinterop]: JS interop on .NET 10 browser-wasm (sibling note)
