---
type: How-To
title: Inner dev loop and debugging for Vite + .NET WebAssembly
description: How to get a fast edit-build-reload loop (dotnet build/watch, Vite full-reload on .NET output change, forwardConsole), what Hot Reload and the C# debugger require, what could not be verified, and the headless test-as-dev-loop pattern.
tags: [dev-loop, dotnet-watch, hmr, debugging, devtools, hot-reload, source-maps, vite, dotnet-wasm]
sources:
  - id: exp05-plugin
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-05-vite-packaging/app/plugins/dotnet-framework.ts
    title: exp-05 dotnet-framework Vite plugin
  - id: exp05-results
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/research/web-packaging/experiment-results-exp-05.md
    title: Experiment results exp-05
  - id: learn-wasm-browser-app
    resource: https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/wasm-browser-app?view=aspnetcore-10.0
    title: JSImport/JSExport interop with a WebAssembly Browser App project (WasmEnableHotReload note)
  - id: dotnet-features-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/wasm/features.md
    title: Configuring and hosting .NET WebAssembly applications (features.md)
  - id: dotnet-browser-template
    resource: https://github.com/dotnet/runtime/tree/release/10.0/src/mono/wasm/templates/templates/browser
    title: wasmbrowser template (launchSettings.json inspectUri)
  - id: dotnet-wasm-host
    resource: https://github.com/dotnet/runtime/tree/release/10.0/src/mono/wasm/host
    title: WasmAppHost (DevServerStartup.cs, DebugProxyLauncher.cs, README.md)
  - id: dotnet-debugger-md
    resource: https://github.com/dotnet/runtime/blob/release/10.0/src/mono/browser/debugger/debugger.md
    title: Mono Wasm Debugger (native debugging steps)
  - id: dotnet-d-ts
    resource: https://github.com/dotnet/runtime/blob/main/src/mono/browser/runtime/dotnet.d.ts
    title: dotnet.d.ts (withDebugging, withDiagnosticTracing, MonoConfig.debugLevel)
  - id: blazor-debug-search
    resource: https://learn.microsoft.com/aspnet/core/blazor/debug
    title: Debug ASP.NET Core Blazor apps (proxy, remote debugging port, no source maps for C#)
  - id: vite-server-options
    resource: https://vite.dev/config/server-options
    title: Vite server options (server.watch, server.forwardConsole)
  - id: vite-api-plugin
    resource: https://vite.dev/guide/api-plugin
    title: Vite plugin API (configureServer, ws.send full-reload)
  - id: runtime-issue-132789
    resource: https://github.com/dotnet/runtime/issues/132789
    title: dotnet/runtime#132789 (watch build workflow, internal copy property)
  - id: yamachu-readme
    resource: https://github.com/yamachu/vite-plugin-dotnet-wasm
    title: "@yamachu/vite-plugin-dotnet-wasm (watch option)"
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:55:00Z }
status: draft
---

# Inner dev loop and debugging

## The loop that was verified

```text
edit C#  -->  dotnet build -c Debug (or dotnet watch build)  -->  Vite plugin sees _framework/manifest change  -->  full page reload  -->  new C#
edit TS  -->  Vite HMR / reload (normal)
```

- **`dotnet build -c Debug`** after a one-line C# change took about 1.1 s wall clock (0.8 to 1.0 s MSBuild time) for the tiny fixture. A real BRUTAL-sized project will be slower; the fixture only bounds the fixed overhead of the WASM SDK targets.[^exp05-results]
- **`dotnet watch build --non-interactive -c Debug`** (the SDK's `dotnet-watch`) rebuilt on every save (three consecutive builds, 0.84 to 0.90 s each) and then printed `Waiting for a file to change before restarting`. This is the same `watch build` workflow `@yamachu/vite-plugin-dotnet-wasm` drives from inside Vite.[^exp05-results][^yamachu-readme]
- **Vite side**: the plugin adds the .NET output directory (or the directory of the static web assets manifest) to Vite's chokidar watcher, debounces 300 ms, and calls `server.ws.send({ type: 'full-reload', path: '*' })`. Observed log: `[dotnet-framework] dotnet.js.gz changed -> full reload` (one reload per build, because `dotnet build` touches many files). After the reload the new code ran (`hostLog` changed from `tick dt=` to `tick2 dt=` and back).[^exp05-plugin][^vite-api-plugin]
- Vite's `server.watch` ignores `node_modules`, `.git`, `cacheDir` and `outDir`; files outside `root` can be added with `server.watcher.add()` (what the plugin does). `usePolling` is only needed on WSL2 or network drives.[^vite-server-options]
- Build output paths for dev: `bin/Debug/net10.0/wwwroot/_framework` (all files, in .NET 10) or the manifest `bin/Debug/net10.0/<Project>.staticwebassets.runtime.json`. Prefer the manifest because .NET 11 will not copy framework assets to `bin/wwwroot` on build.[^runtime-issue-132789][^exp05-plugin]
- **`server.forwardConsole`**: Vite 8 forwards browser `console.error`/`console.warn` (and unhandled errors) to the terminal; it is on automatically when a coding agent is detected. During the experiment .NET runtime warnings showed up in the Vite terminal. `console.log` output from C# `Console.WriteLine` is a normal browser console message and was visible to puppeteer (`Fixture: Main() ran`).[^vite-server-options][^exp05-results]

### Hot Reload (.NET) and Vite

- In .NET 10 the `WasmEnableHotReload` MSBuild property defaults to `true` for the Debug configuration. Microsoft's docs say to set it to `false` to "produce bundler-compatible build output, for example for use with webpack or Vite".[^learn-wasm-browser-app]
- Observed with Hot Reload left on: `dotnet.js`'s inline config lists a library initializer `_content/Microsoft.DotNet.HotReload.WebAssembly.Browser/Microsoft.DotNet.HotReload.WebAssembly.Browser.<hash>.lib.module.js` that is not under `_framework/`. Serving only `_framework/` gives `404` and the runtime warning `MONO_WASM: Failed to import library initializer`, but the app still ran. Serving the manifest's `_content/` route (manifest mode) removed the error.[^exp05-results]
- UNVERIFIED: whether true .NET Hot Reload (apply C# edits without a reload) can be made to work through Vite. It needs `dotnet watch` to inject its client and a websocket handshake; nothing here tests that. The practical loop is rebuild + full reload (about 1 to 2 s plus runtime start, ~100 ms in the fixture).

## Debugging C# in the browser

What is documented:

- `dotnet run` for a `Microsoft.NET.Sdk.WebAssembly` project starts `WasmAppHost`, a small ASP.NET Core server that serves the static web assets, sets `app.UseWebAssemblyDebugging()` and can launch `BrowserDebugHost.dll` (the debug proxy) on demand via `DebugProxyLauncher`. The template's `launchSettings.json` carries `"inspectUri": "{wsProtocol}://{url.hostname}:{url.port}/_framework/debug/ws-proxy?browser={browserInspectUri}"`.[^dotnet-wasm-host][^dotnet-browser-template]
- The proxy implements the Chrome DevTools Protocol and adds .NET information; Chrome must run with `--remote-debugging-port`; the app is debugged through the DevTools Sources tab; breakpoints during very early start-up may be missed; C# is interpreted IL so browser source maps do not apply to C#.[^blazor-debug-search] The runtime's own `dotnet.js.map` and `dotnet.runtime.js.map` exist for the JS parts (present in the Debug `_framework/`).[^exp05-results]
- Native (C/C++ and AOT) debugging uses the "C/C++ DevTools Support (DWARF)" Chrome extension and the same proxy; Debug builds have `debugLevel` -1 in the boot config (observed), `withDebugging(level)` on the JS builder and `MonoConfig.debugLevel` control it; `<WasmNativeDebugSymbols>true</WasmNativeDebugSymbols>` plus `<WasmNativeStrip>false</WasmNativeStrip>` keep native symbols.[^dotnet-debugger-md][^dotnet-features-md][^dotnet-d-ts]

What this means with Vite (UNVERIFIED, nothing below was tested):

1. The debug proxy is wired into `WasmAppHost`, not into Vite. The simplest dependable route is to keep the .NET project runnable with `dotnet run` as a **debug harness** (it serves its own `index.html`/`main.js`) while Vite remains the day-to-day host.
2. A Vite `server.proxy` entry for `/_framework/debug` (with `ws: true`) forwarding to a running `dotnet run` host might let the Vite origin reuse the proxy; not tried. Vite documents `server.proxy` with `ws` and warns it does not check WebSocket origins.[^vite-server-options]
3. For print-style debugging: `Console.WriteLine` reaches `console.log`; `dotnet.withDiagnosticTracing(true)` and `withConfig({ environmentVariables: { MONO_LOG_LEVEL: 'debug', MONO_LOG_MASK: 'all' } })` give runtime logs; `globalThis.getDotnetRuntime(0)` exposes `collectCpuSamples`, `collectGcDump` etc. in the browser console.[^dotnet-features-md]
4. Keep PDBs available in dev (they are in the Debug `_framework/`; `ignorePdbLoadErrors` exists on `MonoConfig`) and strip them from release (`excludeSuffixes: ['.pdb', '.map']` in the plugin).[^dotnet-d-ts][^exp05-plugin]

## Headless test as part of the loop

`npm run test:headless` starts a Vite dev server programmatically on port 5199, launches Chrome for Testing 151 via `puppeteer-core`, loads the page, waits for `window.__e2e.done`, asserts a WebGPU clear-colour readback and that `Tick` returned 42, and exits 0 or 1. The whole run took about 0.9 s against a Debug build, so it is cheap enough to run after every `dotnet build`. See [/web-packaging/headless-webgpu-testing.md](/web-packaging/headless-webgpu-testing.md).[^exp05-results]

Pitfalls found while building the loop:

- A server started inside the same Node process as the test runner must not be starved: running the browser via `spawnSync` deadlocks the in-process Vite server (use async `execFile`/`spawn`).[^exp05-results]
- `parseArgs` rejects `--flags "--x"`; use `--flags="--x --y"`.[^exp05-results]
- The first build after switching `-p:WasmBundlerFriendlyBootConfig` rewrites `dotnet.js`; switching the property back and forth changes the file the plugin serves, so keep one property value per output folder (use separate `-o` publish directories).[^exp05-results]

## Open questions

- UNVERIFIED: C# breakpoints through Vite (proxying `/_framework/debug`, launching `BrowserDebugHost` manually, VS Code `launch.json`).
- UNVERIFIED: .NET Hot Reload (delta apply) with Vite as host.
- UNVERIFIED: end-to-end latency (save to reloaded page) on a BRUTAL-sized project; only the 1 s fixture build was measured.
- UNVERIFIED: whether Chrome DevTools can show C# sources for a Vite-served app without the proxy (believed no).

[^exp05-results]: Experiment results exp-05
[^exp05-plugin]: exp-05 dotnet-framework Vite plugin
[^learn-wasm-browser-app]: JSImport/JSExport interop with a WebAssembly Browser App project (WasmEnableHotReload note)
[^dotnet-features-md]: Configuring and hosting .NET WebAssembly applications (features.md)
[^dotnet-browser-template]: wasmbrowser template (launchSettings.json inspectUri)
[^dotnet-wasm-host]: WasmAppHost (DevServerStartup.cs, DebugProxyLauncher.cs, README.md)
[^dotnet-debugger-md]: Mono Wasm Debugger (native debugging steps)
[^dotnet-d-ts]: dotnet.d.ts (withDebugging, withDiagnosticTracing, MonoConfig.debugLevel)
[^blazor-debug-search]: Debug ASP.NET Core Blazor apps (proxy, remote debugging port, no source maps for C#)
[^vite-server-options]: Vite server options (server.watch, server.forwardConsole)
[^vite-api-plugin]: Vite plugin API (configureServer, ws.send full-reload)
[^runtime-issue-132789]: dotnet/runtime#132789 (watch build workflow, internal copy property)
[^yamachu-readme]: "@yamachu/vite-plugin-dotnet-wasm (watch option)"
