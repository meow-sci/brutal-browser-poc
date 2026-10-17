# Alternative proposals (B, C) and the A2 variant

Status: documented 2026-10-16 as fallbacks and future options. Rationale and evidence: [architecture decision record](../docs/research/decisions/architecture-options.md).

## A2 — Managed ICD behind `[UnmanagedCallersOnly]` (variant of Proposal A)

Same architecture as [Proposal A](01-proposal-a-managed-web-icd.md) but the bindings keep their stock `delegate* unmanaged[Cdecl]` shape and the ICD's entry points are `[UnmanagedCallersOnly]` methods whose addresses `vkGet*ProcAddr` returns. Binary-compatible with the NuGet `Brutal.Vulkan.Abstractions` (only `Brutal.Vulkan` needs the loader patch).

Requirements established by the research (`/dotnet-wasm/function-pointers-and-native-callbacks.md`, exp-01, exp-04):

- The net10 `wasm-tools` workload and a native relink (`WasmBuildNative=true` or AOT): without it `&UcoMethod` is `0x0` and the call traps.
- "Anchor" declarations for the 29 interpreter call signatures that are neither pointer-shaped nor among the 52 prebuilt ones (55 of 814 wrappers): dummy `[UnmanagedFunctionPointer]` delegate types or `[DllImport]`s make the build emit the trampolines. AOT-compiled callers do not need them, but interpreter fallback paths do.
- Avoid the four wrappers that pass a 64-bit flags enum by value on the interpreter fast path (`vkCmdWriteTimestamp2`, `vkCmdResetEvent2`, …): values are truncated to 32 bits.
- UCO method names must be unique per type and parameter count; full trimming can drop unreferenced UCO methods (`TrimmerRootDescriptor`).

When to use it: once `wasm-tools` is installed, as a Phase 7.2 trial; keep it if AOT performance of the UCO path beats the managed-pointer path, or if shipping the stock NuGet `Abstractions` assembly matters more than the extra build step.

Phases if adopted instead of A1:

| Phase | Tasks |
| --- | --- |
| A2-1 | Install `wasm-tools`; add `WasmBuildNative=true` to the browser sample; verify `&UcoMethod` is non-null (re-run exp-01 T07/T08). |
| A2-2 | Add `[UnmanagedCallersOnly(CallConvs=[typeof(CallConvCdecl)])]` to the ICD entry points via a build define (`BRUTAL_ICD_UCO`); generate anchor delegates with `tools/gen-anchors.py` from the exp-01 signature classification. |
| A2-3 | Re-run the exp-01 signature probes under relink; confirm 45/45 pass; document any residual aborts. |
| A2-4 | Switch `Brutal.Vulkan.Browser` to `BrutalManagedFnptr=false`; drop the Abstractions flavor project; re-run Phase 3 tests. |

## Proposal B — Native ICD compiled with Emscripten

A C/C++ (or Rust) implementation of the same Vulkan subset on `webgpu.h`, statically linked into `dotnet.native.wasm` via `NativeFileReference` so that `[LibraryImport("vulkan")]` binds and `vkGetInstanceProcAddr` returns wasm table indices. Prior art: nikki (C++, compute only) — see `/webgpu/prior-art-and-related-layers.md`.

Why it is not the first choice (from `/dotnet-wasm/native-linking-and-emscripten.md`, `/brutal/vulkan-binding-architecture.md`, exp-04):

1. **ABI.** BRUTAL's structs are laid out for 64-bit pointers (`VkBufferCreateInfo.pNext` at offset 8, size 56) and its handles are 32-bit `nint` on wasm32, whereas C compiled for wasm32 expects 4-byte pointers and 8-byte non-dispatchable handles. The C side must be written against BRUTAL's layouts (custom struct definitions), not `vulkan.h`.
2. **Toolchain coupling.** .NET 10 pins Emscripten 3.1.56; the maintained WebGPU bindings (emdawnwebgpu) require ≥ 4.0.10 (Uno ships a patched copy); the workload's Emscripten cache is frozen, so ports need `WasmCachePath`; any mismatch is a hard error.
3. **Callbacks into C#** (debug messenger, allocation callbacks) still need UCO thunks and the relink.
4. **Debuggability and iteration**: two languages, native stack traces in the browser, slow relinks (minutes) versus plain `dotnet build`.
5. Uno Platform reports hangs when WebGPU events are pumped from a managed call stack; async operations must stay in JS either way.

When it becomes attractive: if C# command encoding under AOT is still the bottleneck (exp-03 measured the JS replay at parity with hand-written JS, and 60 ns per draw for the C# encoder in interpreter mode), or if the ICD should be shared with non-.NET wasm applications.

Phases:

| Phase | Tasks | Acceptance |
| --- | --- | --- |
| B0 | `wasm-tools` installed; confirm `NativeFileReference` + `_WasmPInvokeModules Include="vulkan"` binds a `vkEnumerateInstanceVersion` written in C; confirm Emscripten 3.1.56 can link `-sUSE_WEBGPU=1` (legacy bindings) or a vendored emdawnwebgpu. | exp-01 T06c passes with the C symbol |
| B1 | Define BRUTAL-layout C structs for the PoC subset (generate from `Structs/*.gen.cs` with a script: 64-bit slots for pointers/handles, explicit padding) and a unit test that compares `sizeof`/offsets against the C# explicit layouts. | layout test green for all structs used by HelloMesh |
| B2 | Port the ICD logic (object tables, command recording, memory shadowing) to C++ against `webgpu.h`; keep the same manifest/shader conventions. | HelloMesh renders (M3 equivalent) |
| B3 | Callbacks: debug utils messenger → UCO thunk; allocation callbacks ignored. | validation messages reach C# |
| B4 | Performance comparison with Proposal A under AOT; decide which to keep. | report |

## Proposal C — WebGPU-native render-hardware interface (RHI)

Build a BRUTAL-style C# binding for WebGPU (`Brutal.WebGpu`: `webgpu.h` via wgpu-native/Dawn on desktop, `[JSImport]` in the browser) and a thin C# RHI with two back ends (Vulkan via `Brutal.Vulkan.Abstractions`; WebGPU via `Brutal.WebGpu`). Games target the RHI.

Assessment:

- Conventional and well understood (bgfx, sokol, wgpu); no Vulkan emulation, no profile, direct use of WebGPU features (immediates, compat mode, resource tables when they ship).
- Does **not** satisfy the brief's requirement to leverage the BRUTAL Vulkan bindings: existing BRUTAL renderers (e.g. KSA's, which uses bindless, BDA, sync2, dynamic rendering) would be rewritten against the RHI.
- Conflicts with BRUTAL's "no renderer" scope for upstreaming; it would live outside BRUTAL.
- The ICD's JS glue from Proposal A is already most of a browser `Brutal.WebGpu`; Proposal C can be layered later for new titles without invalidating Proposal A.

Phases if pursued:

| Phase | Tasks |
| --- | --- |
| C1 | `Brutal.WebGpu` binding generated from `webgpu.h` (desktop: wgpu-native or Dawn shared library via `Brutal.Interop`; browser: the exp-03 handle-table glue). |
| C2 | RHI surface (device, buffers, textures, samplers, bind groups, pipelines, passes, command lists, submit/present) with WGSL as the shader language and Slang as the authoring front end. |
| C3 | Vulkan back end over `Brutal.Vulkan.Abstractions`; WebGPU back end over `Brutal.WebGpu`. |
| C4 | HelloMesh on the RHI; Vite packaging as in Proposal A Phase 6. |

## Rejected

- **Software Vulkan in wasm** (Lavapipe/SwiftShader): prior art reaches compute only, no GPU acceleration, tens of megabytes (`/webgpu/prior-art-and-related-layers.md`).
- **NativeAOT-LLVM** as the runtime: experimental, no macOS arm64 host package, JS-interop gaps (`/dotnet-wasm/nativeaot-llvm-and-alternatives.md`).
- **Emscripten GLFW/OpenAL ports for input and audio**: need the relink, miss 7–35 of the entry points BRUTAL binds, and BRUTAL's delegate-based callbacks do not work on the interpreter (`/io-audio/emscripten-glfw-options.md`, `/io-audio/browser-audio-options.md`).
- **Asyncify/JSPI to keep a blocking `while(!ShouldClose)` loop**: ~50 % overhead or unsupported by .NET's runtime (`/io-audio/main-loop-patterns-in-dotnet-wasm.md`).
