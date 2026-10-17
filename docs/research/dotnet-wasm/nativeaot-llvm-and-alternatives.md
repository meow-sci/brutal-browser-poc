---
type: Technology Assessment
title: NativeAOT-LLVM for WebAssembly and other C#-to-browser options (status October 2026)
description: Status, packages, host requirements, interop features and maturity of the experimental dotnet/runtimelab NativeAOT-LLVM toolchain for browser-wasm, compared with Mono AOT, the upcoming CoreCLR-on-wasm and community stacks (Uno Bootstrap, Avalonia, Blazor, bflat), judged for running a BRUTAL-based game.
tags: [dotnet-wasm, nativeaot, nativeaot-llvm, runtimelab, aot, coreclr-wasm, uno, avalonia, alternatives]
sources:
  - id: naot-readme
    resource: https://github.com/dotnet/runtimelab/blob/feature/NativeAOT-LLVM/README.md
    title: dotnet/runtimelab feature/NativeAOT-LLVM README
  - id: naot-branch
    resource: https://github.com/dotnet/runtimelab/tree/feature/NativeAOT-LLVM
    title: feature/NativeAOT-LLVM branch (head commit 2026-09-22 "Avoid publishing additional files from IntermediateArtifacts (#3307)")
  - id: naot-compiling
    resource: https://github.com/dotnet/runtimelab/blob/feature/NativeAOT-LLVM/docs/using-nativeaot/compiling.md
    title: Compiling with NativeAOT-LLVM
  - id: naot-prereq
    resource: https://github.com/dotnet/runtimelab/blob/feature/NativeAOT-LLVM/docs/using-nativeaot/prerequisites.md
    title: NativeAOT-LLVM prerequisites (Emscripten, WASI SDK)
  - id: naot-emsdk-script
    resource: https://github.com/dotnet/runtimelab/blob/feature/NativeAOT-LLVM/eng/pipelines/runtimelab/install-emscripten.ps1
    title: install-emscripten.ps1 (emsdk install/activate 3.1.56)
  - id: naot-wasm-interop
    resource: https://github.com/dotnet/runtimelab/blob/feature/NativeAOT-LLVM/docs/using-nativeaot/wasm-interop.md
    title: WebAssembly interop in NativeAOT (DirectPInvoke / WasmImportLinkage)
  - id: naot-limitations
    resource: https://github.com/dotnet/runtimelab/blob/feature/NativeAOT-LLVM/docs/using-nativeaot/limitations.md
    title: Limitations of Native AOT
  - id: naot-feed
    resource: https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-experimental/nuget/v3/flat2/microsoft.dotnet.ilcompiler.llvm/index.json
    title: dotnet-experimental feed index for Microsoft.DotNet.ILCompiler.LLVM (queried 2026-10-16)
  - id: naot-issue-2434
    resource: https://github.com/dotnet/runtimelab/issues/2434
    title: "[NativeAOT LLVM] Support dotnet.js (open checklist)"
  - id: naot-issue-3279
    resource: https://github.com/dotnet/runtimelab/issues/3279
    title: "Publish runtime.osx-arm64.microsoft.dotnet.ilcompiler.llvm for the 10.x NativeAOT-LLVM line (open, 2026-07-14)"
  - id: naot-issue-2806
    resource: https://github.com/dotnet/runtimelab/issues/2806
    title: "[NativeAOT-LLVM] Threads and GC Poll (open)"
  - id: naot-issue-2627
    resource: https://github.com/dotnet/runtimelab/issues/2627
    title: "[NativeAOT-LLVM] DotnetJS Minor incompatibilities with WASM SDK on Mono runtime (open)"
  - id: naot-issue-2505
    resource: https://github.com/dotnet/runtimelab/issues/2505
    title: "[NativeAOT-LLVM] Implement timers on Browser (open)"
  - id: issue-121511
    resource: https://github.com/dotnet/runtime/issues/121511
    title: 'dotnet/runtime tracking issue ".NET WebAssembly on CoreCLR interpreter"'
  - id: issue-124219
    resource: https://github.com/dotnet/runtime/issues/124219
    title: "[wasm][coreclr] GetUnmanagedCallersOnlyThunk: unknown thunk for unmanaged callers only method (open)"
  - id: issue-132777
    resource: https://github.com/dotnet/runtime/issues/132777
    title: "[Browser][CoreCLR] publish+workload should not trigger native relink unless there is new UCO/PInvoke (open)"
  - id: infoworld-net11p7
    resource: https://www.infoworld.com/article/4208694/microsoft-rolls-out-dotnet-11-preview-7.html
    title: InfoWorld — Microsoft rolls out .NET 11 Preview 7
  - id: maui-coreclr-blog
    resource: https://devblogs.microsoft.com/dotnet/dotnet-maui-moves-to-coreclr-in-dotnet-11/
    title: .NET Blog — .NET MAUI moves to CoreCLR in .NET 11 (states WASM continues to use Mono)
  - id: uno-threading
    resource: https://platform.uno/docs/articles/external/uno.wasm.bootstrap/doc/features-threading.html
    title: Uno.Wasm.Bootstrap — threading support
  - id: ms-build-tools-aot
    resource: https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot?view=aspnetcore-10.0
    title: Blazor WebAssembly build tools and AOT (.NET 10)
  - id: bflat-site
    resource: https://flattened.net/
    title: bflat home page (targets listed; no WebAssembly)
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:05:57Z }
status: draft
---

# NativeAOT-LLVM (dotnet/runtimelab)

## Status in October 2026

- An **experimental fork** of Native AOT that uses LLVM. "Currently, the only supported target is WebAssembly." It is not part of the .NET SDK.[^naot-readme]
- It is actively maintained: the `feature/NativeAOT-LLVM` head is from 2026-09-22.[^naot-branch] Packages are published to the **`dotnet-experimental`** Azure Artifacts feed after each PR. The newest `Microsoft.DotNet.ILCompiler.LLVM` on 2026-10-16 is **`10.0.0-rc.1.26357.1`**; the same version exists for `runtime.{win-x64,linux-x64,linux-arm64,browser-wasm,wasi-wasm}.Microsoft.DotNet.ILCompiler.LLVM`.[^naot-feed] (Inference, UNVERIFIED: the Arcade date code `26357` decodes to 2026-07-07.)
- **Host platforms**: "Compilation using NativeAOT-LLVM is currently only supported on Windows x64 and Linux x64". linux-arm64 host packs now exist too. **There is no `osx-arm64` host pack**, and `osx-x64` stopped at `9.0.0-alpha.1.23574.2`. A community issue (2026-07-14) asks to add it; "Apple-Silicon developers can only run the C#→Wasm ILC inside a Linux container (Docker)."[^naot-compiling][^naot-issue-3279] **On this Mac it would have to run in a Linux container/VM.**
- Toolchain: an external emsdk, with "the same version that we test against" strongly recommended. The CI script installs **Emscripten 3.1.56** (the same version as Mono/.NET 10).[^naot-prereq][^naot-emsdk-script]

## How a project uses it

```xml
<!-- nuget.config: add https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-experimental/nuget/v3/index.json -->
<ItemGroup>
  <PackageReference Include="Microsoft.DotNet.ILCompiler.LLVM" Version="10.0.0-*" />
  <PackageReference Include="runtime.$(NETCoreSdkPortableRuntimeIdentifier).Microsoft.DotNet.ILCompiler.LLVM" Version="10.0.0-*" />
</ItemGroup>
<PropertyGroup>
  <PublishTrimmed>true</PublishTrimmed>
  <SelfContained>true</SelfContained>
  <MSBuildEnableWorkloadResolver>false</MSBuildEnableWorkloadResolver> <!-- "the wasm-tools workload is identified as a dependency even though its not used" -->
</PropertyGroup>
```

Do not set `<PublishAot>`. Publish with `dotnet publish -r browser-wasm -c Release`. Output is `publish/YourProject.js`, runnable with Node or opened via `emrun …/YourProject.html` (`WasmHtmlTemplate` customises the HTML). Use `/p:DebugType=none` to drop the large debug info, and `InvariantGlobalization` to drop ICU.[^naot-compiling]

## Interop features

| Feature | NativeAOT-LLVM | Notes |
|---|---|---|
| `[UnmanagedCallersOnly(EntryPoint="X")]` exports | ✅ | library mode (`OutputType=library` + `LinkerArg -sEXPORTED_RUNTIME_METHODS=cwrap`); other assemblies need `<UnmanagedEntryPointsAssembly Include="…" />`[^naot-compiling] |
| `&UcoMethod` / `delegate* unmanaged` calls | ✅ (inferred) | Everything is compiled to wasm, so function pointers are plain wasm table entries. No trampoline tables are needed, unlike Mono's interpreter. UNVERIFIED: not tested here |
| P/Invoke | requires a gesture | "WebAssembly, as a platform, does not support lazy resolution … by default, PInvoke calls … will throw `PlatformNotSupportedException`." Use `<DirectPInvoke />` for statically linked code, or `[WasmImportLinkage]` for imports supplied at instantiation[^naot-wasm-interop] |
| `[JSImport]/[JSExport]` + `dotnet.js` | partial | Tracking checklist: JSImport, JSExport, Tasks, delegates, JSObject, timers and yielding to the event loop are ticked. Command-line args, environment variables, runtime options and the System.Runtime.InteropServices.JavaScript library tests are open. A maintainer: "The only thing needed for dotnet.js is to set `DotnetJsApi=true`" (2024). A user hit "Missing wasm export … ReleaseJSOwnedObjectByGCHandle" (2024)[^naot-issue-2434] |
| dotnet.js parity | gaps | e.g. `config.mainAssemblyName` is empty (use `withMainAssembly`), `WasmRuntimeAssetsLocation` is ignored[^naot-issue-2627] |
| Timers on browser | in progress in 2024 | issue still open[^naot-issue-2505] |
| Threads | ✗ | "Threads and GC Poll" is open[^naot-issue-2806] |
| Reflection / dynamic code | NativeAOT limits | no `Assembly.LoadFile`, no `Reflection.Emit`, no unconstrained reflection[^naot-limitations] |

## Is it a sensible path for this game PoC?

**Not as the primary path. It is worth a later spike.**
- Pros: true AOT of all code; UCO/function pointers with no trampoline machinery (directly solves the Brutal.Vulkan `delegate* unmanaged` problem described in [/dotnet-wasm/function-pointers-and-native-callbacks.md](/dotnet-wasm/function-pointers-and-native-callbacks.md)); likely smaller and faster than Mono AOT (UNVERIFIED for this workload).
- Cons: experimental and unsupported; **no macOS host** (needs Docker/VM here); dotnet.js/JS-interop parity gaps; single-threaded; NativeAOT reflection rules (BRUTAL and KSA code must be trim/AOT-clean); package versions labelled `10.0.0-rc.1`; separate build pipeline (no `Microsoft.NET.Sdk.WebAssembly` static-web-assets output).

# Mono AOT (the supported AOT)

`RunAOTCompilation=true` with `wasm-tools`. It is publish-time, Release-only and trimming-only, mixed with the interpreter (`LLVMOnlyInterp`). It is the supported way to get native-speed C# on .NET 10 browser-wasm. Details in [/dotnet-wasm/performance-and-aot.md](/dotnet-wasm/performance-and-aot.md).[^ms-build-tools-aot]

# CoreCLR on WebAssembly (.NET 11 preview)

- "We are migrating .NET WebAssembly from the Mono runtime to CoreCLR, targeting an early preview in the .NET 11 timeframe. The initial phase will run on the CoreCLR interpreter, laying the groundwork for ReadyToRun (R2R) and RyuJIT … In the .NET 12 timeframe we will focus on stability, performance, and a seamless transition." The checklist shows Blazor support done and relink during build done; "All runtime tests pass" and the debugger are still open.[^issue-121511]
- InfoWorld (2026-08-12) on .NET 11 Preview 7: ".NET 11 is bringing CoreCLR to WebAssembly with a portable interpreter-plus-R2R" model that "could start up in Preview 6, and now runs the CoreCLR libraries test suite end-to-end in Preview 7."[^infoworld-net11p7] Microsoft's MAUI post is explicit for the shipping product: "WASM continues to use Mono, and that is not changing in .NET 11."[^maui-coreclr-blog] Expect Mono to remain the supported browser runtime through .NET 11, with CoreCLR-wasm as a preview.
- The same reverse-P/Invoke (UCO) thunk problem exists there: issues for "GetUnmanagedCallersOnlyThunk: unknown thunk" and "publish+workload should not trigger native relink unless there is new UCO/PInvoke" show that **UCO still needs build-time generated thunks** on CoreCLR-wasm.[^issue-124219][^issue-132777] A design that avoids `delegate* unmanaged` → UCO round trips (managed function pointers) is therefore forward-compatible.

# Community stacks (brief)

| Stack | What it is | Relevance |
|---|---|---|
| Uno.Wasm.Bootstrap | Uno Platform's bootstrapper and packager over the .NET wasm runtime; interpreter/AOT; threads via `WasmShellEnableThreads` ("Threading support is experimental, and .NET 9 support has changed the main thread's ability to execute managed code") | Proof that the same Mono-wasm toolchain hosts large apps. Uno's WebGPU backend links emdawnwebgpu into `dotnet.native.wasm` (see [/dotnet-wasm/native-linking-and-emscripten.md](/dotnet-wasm/native-linking-and-emscripten.md))[^uno-threading] |
| Avalonia.Browser | Avalonia UI's browser head on the standard .NET wasm SDK with SkiaSharp. UNVERIFIED: exact SDK and render path in 2026 | example of a non-Blazor host with a render loop |
| Blazor WebAssembly | Microsoft's SPA framework on the same runtime | unnecessary for a game; adds renderer/DI weight |
| bflat | C# → native compiler built on NativeAOT. Its site lists Linux, Windows, Android and UEFI targets; no evidence of WebAssembly support | not applicable[^bflat-site] |
| WASI (`wasi-experimental` workload, `wasi-wasm` RID) | server/CLI wasm | not a browser target |

## Open questions

- UNVERIFIED: NativeAOT-LLVM `DotnetJsApi` behaviour on the 10.0 line, and whether `delegate* unmanaged` → UCO round trips need any glue there.
- UNVERIFIED: size and speed of NativeAOT-LLVM vs Mono AOT for a BRUTAL-style workload.
- UNVERIFIED: the Emscripten version pinned by .NET 11 GA, and how mature CoreCLR-wasm will be at GA.
- UNVERIFIED: Avalonia.Browser's current architecture.

[^naot-readme]: runtimelab feature/NativeAOT-LLVM README
[^naot-branch]: runtimelab feature/NativeAOT-LLVM branch head (2026-09-22)
[^naot-compiling]: runtimelab docs/using-nativeaot/compiling.md
[^naot-prereq]: runtimelab docs/using-nativeaot/prerequisites.md
[^naot-emsdk-script]: runtimelab eng/pipelines/runtimelab/install-emscripten.ps1
[^naot-wasm-interop]: runtimelab docs/using-nativeaot/wasm-interop.md
[^naot-limitations]: runtimelab docs/using-nativeaot/limitations.md
[^naot-feed]: dotnet-experimental feed index (2026-10-16)
[^naot-issue-2434]: dotnet/runtimelab#2434
[^naot-issue-3279]: dotnet/runtimelab#3279
[^naot-issue-2806]: dotnet/runtimelab#2806
[^naot-issue-2627]: dotnet/runtimelab#2627
[^naot-issue-2505]: dotnet/runtimelab#2505
[^issue-121511]: dotnet/runtime#121511
[^issue-124219]: dotnet/runtime#124219
[^issue-132777]: dotnet/runtime#132777
[^infoworld-net11p7]: InfoWorld — .NET 11 Preview 7
[^maui-coreclr-blog]: .NET Blog — .NET MAUI moves to CoreCLR in .NET 11
[^uno-threading]: Uno.Wasm.Bootstrap threading docs
[^ms-build-tools-aot]: Microsoft Learn — Blazor WebAssembly build tools and AOT (.NET 10)
[^bflat-site]: bflat home page
