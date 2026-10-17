---
type: Technology Assessment
title: How a BRUTAL app compiles shaders today (shaderc, Slang, KSA runtime GLSL) and what breaks in a browser
description: Documents the exact API surface through which BRUTAL and the KSA reference game turn GLSL (or Slang) into SPIR-V at runtime, how that SPIR-V reaches vkCreateShaderModule, and which parts cannot exist in a browser-wasm build.
tags: [shaders, brutal, shaderc, slang, glsl, spirv, ksa, runtime-compilation, wasm]
sources:
  - id: shaderc-highlevel
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ShaderC/HighLevel.cs
    title: Brutal.ShaderC HighLevel.cs (Compile, CompileFile, Preprocess, Assemble, ThrowIfFailed)
  - id: shaderc-native
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ShaderC/ShaderC.cs
    title: Brutal.ShaderC ShaderC.cs (native shaderc_shared loading, CreateCompiler, CreateCompilerOptions)
  - id: shaderc-options
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ShaderC/Types/CompileOptions.cs
    title: Brutal.ShaderC CompileOptions.cs (SetDefaultIncludeCallbacks)
  - id: shaderc-structs
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ShaderC/Generated/Structs.gen.cs
    title: Brutal.ShaderC generated CompileOptions members (SetTargetEnv, SetTargetSpirv, SetOptimizationLevel, SetInvertY, ...)
  - id: shaderc-callbacks
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ShaderC/Utilities/Callbacks.cs
    title: Brutal.ShaderC default include resolver (disk-relative, depth limit 200)
  - id: shaderc-kind
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ShaderC/ShaderKindInference.cs
    title: ShaderKind inference from file extension
  - id: shaderc-notices
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ShaderC/LICENSES/BRUTAL-SHADERC-THIRD-PARTY-NOTICES.txt
    title: Brutal.ShaderC third-party notices (native libs shipped per RID)
  - id: slang-bindings
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Slang/Slang.cs
    title: Brutal.Slang Slang.cs (COM-style global session, native slang library loading)
  - id: slang-session-ext
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Slang/ISessionExtensions.cs
    title: Brutal.Slang ISessionExtensions.cs (LoadModule, LoadModuleFromSourceString, CreateCompositeComponentType)
  - id: slang-enums
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Slang/Generated/Enumerations.gen.cs
    title: Brutal.Slang generated enums (CompileTarget.Wgsl = 28, WgslSpirv = 30, PassThrough.Tint)
  - id: slang-notices
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Slang/LICENSES/BRUTAL-SLANG-THIRD-PARTY-NOTICES.txt
    title: Brutal.Slang third-party notices (slang-tint ships on win-x64 only)
  - id: vk-shadermodule-ext
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Vulkan.Abstractions/Extensions/ShaderModuleExtensions.cs
    title: ShaderModuleExtensions.FromSpirv (VkShaderModuleCreateInfo from a byte span)
  - id: ksa-moduleutils
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/RenderCore/ShaderModuleUtils.cs
    title: KSA ShaderModuleUtils (decompiled; default CompileOptions, target env / SPIR-V version selection, FromFile/FromString)
  - id: ksa-resolve
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/RenderCore.Shaders/ShaderCompilerResolve.cs
    title: KSA ShaderCompilerResolve (decompiled; include callback wrapper)
  - id: ksa-shaderref
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA/ShaderReference.cs
    title: KSA ShaderReference (decompiled; compile on mod load, include dependency tracking)
  - id: ksa-reloader
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA.AssetReloader/ShaderReloader.cs
    title: KSA ShaderReloader (decompiled; file watcher + recompile)
  - id: ksa-pbr
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/KSA.Rendering.PBR/PbrSpheres.cs
    title: KSA PbrSpheres (decompiled; per-pipeline macro definitions and specialization constants)
  - id: ksa-textureset
    resource: /Users/asherwin/repos/meow-sci/ksa-linux/Content/Core/Shaders/Common/TextureSet.glsl
    title: KSA bindless texture library header (runtime arrays of texture2D/sampler)
  - id: glslang-npm
    resource: https://www.npmjs.com/package/@webgpu/glslang
    title: "@webgpu/glslang (WebAssembly build of glslang; version and date from `npm view`)"
  - id: slang-release
    resource: https://github.com/shader-slang/slang/releases/tag/v2026.19
    title: Slang v2026.19 release (assets include slang-2026.19-wasm.zip; slang-wasm.wasm measured locally at 24,518,850 bytes)
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:38:44Z }
status: draft
---

# Summary

A BRUTAL application does **not** ship pre-compiled shaders by default. It links a native `shaderc_shared` (GLSL/HLSL to SPIR-V) or native `slang` library, compiles source at run time, and hands the resulting SPIR-V bytes to `vkCreateShaderModule`.[^shaderc-native][^shaderc-highlevel][^vk-shadermodule-ext] None of those native libraries ships for a browser RID (the notices list win-x64, linux-x64 and osx-arm64 only),[^shaderc-notices][^slang-notices] so a `browser-wasm` build has three choices: compile before shipping (build time), compile in the browser with a wasm toolchain, or avoid runtime compilation entirely. See [build-time vs runtime](/shaders/build-time-vs-runtime-cross-compilation.md) for the recommendation.

The Vulkan layer only ever sees finished SPIR-V in `vkCreateShaderModule`; everything below is about how that SPIR-V comes to exist.

# Brutal.ShaderC (GLSL / HLSL to SPIR-V)

| Aspect | What the binding offers | Source |
|---|---|---|
| Native library | `shaderc_shared` (macOS: `shaderc_shared.1`), loaded in a static constructor via `NativeLibrary.Load(..., DllImportSearchPath.AssemblyDirectory)`; a missing library surfaces as `TypeInitializationException` | [^shaderc-native] |
| Entry points | `ShaderC.CreateCompiler()`, `ShaderC.CreateCompilerOptions()`, `ShaderC.GetSpvVersion`, `ShaderC.TryParseVersionProfile` | [^shaderc-native] |
| Compile | extension methods on `Compiler`: `Compile(span or string, ShaderKind, inputFileName, entryPoint = "main", CompileOptions)`, `CompileFile(path, ShaderKind, ...)`, `Preprocess(...)`, `Assemble(spirvAssemblyUtf8, options)`; each throws a typed `ShaderException` subclass (`ShaderCompilationException`, `ShaderValidationException`, ...) via `ThrowIfFailed` | [^shaderc-highlevel] |
| Stage inference | `ShaderC.InferShaderKind(path)` from `.vert .frag .comp .geom .tesc .tese .mesh .task .rgen .rmiss .rchit .rahit .rint .rcall` | [^shaderc-kind] |
| Options (`CompileOptions`) | `SetTargetEnv(TargetEnv, version)`, `SetTargetSpirv(SpirvVersion)`, `SetOptimizationLevel`, `AddMacroDefinition(name, value)`, `SetGenerateDebugInfo`, `SetSourceLanguage`, `SetForcedVersionProfile`, `SetAutoBindUniforms`, `SetAutoCombinedImageSampler`, `SetAutoMapLocations`, `SetBindingBase(ForStage)`, `SetPreserveBindings`, `SetHlsl*`, `SetVulkanRulesRelaxed`, `SetInvertY`, `SetNanClamp`, `SetLimit`, `SetMaxIdBound`, `Clone()` | [^shaderc-structs] |
| Includes | **off by default**: with default options a source containing `#include` fails. `CompileOptions.SetDefaultIncludeCallbacks()` installs a resolver that reads the file relative to the requesting source (so the input file name must be a real path); nesting depth is capped at 200 to break cycles | [^shaderc-options][^shaderc-callbacks] |
| Result | `CompilationResult`: `GetBytes()` / `GetLength()` raw SPIR-V while undisposed, `GetCompilationStatus()` | [^shaderc-highlevel] |

Hand-off to Vulkan: `ShaderModuleExtensions.FromSpirv(device, ReadOnlySpan<byte> bytes, allocator)` builds a `VkShaderModuleCreateInfo { Code = bytes as int*, CodeSize }` and calls `device.CreateShaderModule`.[^vk-shadermodule-ext] A browser ICD therefore receives `(pCode, codeSize)` exactly as a native driver would (see [`vkCreateShaderModule` handling](/webgpu/vulkan-to-webgpu-object-mapping.md)).

`SetInvertY` maps to shaderc's invert-Y option (negating `gl_Position.y` in vertex shaders at compile time).[^shaderc-structs] Do **not** combine it with a translator that already flips Y (see [naga](/shaders/naga-spirv-to-wgsl.md)): the two flips cancel.

# Brutal.Slang (Slang to SPIR-V and WGSL)

- Native `slang` library loaded through `NativeLibrary.Load("slang", ..., AssemblyDirectory)`; sessions and modules are `[GeneratedComInterface]` COM proxies released by the GC (no `Dispose`).[^slang-bindings]
- High-level helpers: `Slang.CreateGlobalSession`, `ISession.LoadModule / LoadModuleFromSourceString / CreateCompositeComponentType`, `ISlangBlob.AsSpan()`.[^slang-session-ext]
- The generated enums already include `CompileTarget.Wgsl = 28`, `WgslSpirv = 30` and `PassThrough.Tint = 13`,[^slang-enums] so the **desktop** Brutal.Slang binding can emit WGSL itself; but `slang-tint` (the Tint WGSL compiler used for pass-through validation) is packed on win-x64 only.[^slang-notices]
- Nothing else in the BRUTAL repository consumes Brutal.Slang or Brutal.ShaderC (a grep for `SlangApi` / `ShaderCApi` outside their own projects finds no users); the KSA decompilation contains no Slang or HLSL shader source, only GLSL.

# How KSA (reference only) uses it

- One static `Compiler` and one default `CompileOptions`: include callbacks installed, `SetTargetEnv(Vulkan, version)` and `SetTargetSpirv(...)` chosen from the **Vulkan API version actually created**: Vulkan 1.0 maps to SPIR-V 1.0, 1.1 to 1.3, 1.2 to 1.5, 1.3 and 1.4 to **1.6**; `RAY_QUERIES_SUPPORTED` is defined when ray queries are available.[^ksa-moduleutils]
- Per-pipeline variants clone the default options and add macros (for example `INSTANCED=1` for the PBR sphere pipeline) and set Vulkan specialization constants separately (`SetSpecializationConstant<int>(FragmentBit, 10, ...)`).[^ksa-pbr]
- Entry point is always `main`; stage comes from the file extension. `FromFile` reads the file, compiles, and creates the `VkShaderModule`; failures throw `ShaderCompilationException` (messages carry shaderc's diagnostics).[^ksa-moduleutils]
- `ShaderCompilerResolve.SetIncludeCallback` wraps the stock resolver and raises `OnIncludeResolved(sourcePath, includePath)`; `ShaderReference` collects those into `IncludePaths` so `ShaderReloader` can recompile every shader that depends on a changed `.glsl` file (hot reload with a file watcher).[^ksa-resolve][^ksa-shaderref][^ksa-reloader]
- No on-disk SPIR-V cache appears in these files (UNVERIFIED: other code paths may cache).
- Shader source is shipped as text next to the game (`Content/Core/Shaders/**/*.vert|frag|comp|glsl`) and compiled when the mod loads. Includes are quoted `#include "Common/Global.glsl"` with `GL_GOOGLE_include_directive`. A header such as `TextureSet.glsl` demands a per-pipeline `#define SET_TEXTURE <n>` and otherwise `#error`s.[^ksa-textureset]

Consequence for a browser port: the SPIR-V that reaches the ICD for KSA-style code is **SPIR-V 1.6 with `OpCopyLogical`, `DemoteToHelperInvocation` and other post-1.3 features**, because the shader target version follows the Vulkan version the ICD reports. Choosing what Vulkan version the ICD advertises (1.1 instead of 1.3) therefore changes what naga can read; see [naga limitations](/shaders/naga-spirv-to-wgsl.md) and [KSA assessment](/shaders/ksa-shader-portability-assessment.md).

# What cannot exist in the browser

| Today | Browser reality | Replacement |
|---|---|---|
| `shaderc_shared` native (P/Invoke) | no native code in `browser-wasm` unless statically linked with Emscripten; no official shaderc wasm for .NET | compile at build time; or wasm toolchain in JS (`@webgpu/glslang` is a glslang wasm build, GLSL to SPIR-V only[^glslang-npm]) |
| `slang` native COM library | same; Slang does publish a wasm build (24.5 MB raw in release 2026.19)[^slang-release] | build time, or the Slang wasm module in JS |
| `File.ReadAllBytes` + disk include resolver | no filesystem; include files must be fetched or embedded | pre-flattened sources, or a virtual include map |
| `VkShaderModule` takes SPIR-V | WebGPU only accepts WGSL | SPIR-V to WGSL in the ICD (naga or Tint); see [conventions](/shaders/wgsl-conventions-for-vulkan-layer.md) |
| Hot reload via file watcher | no file watcher | out of PoC scope |

## Open questions

- UNVERIFIED: whether BRUTAL ships or plans a `browser-wasm` runtime identifier for `Brutal.ShaderC` / `Brutal.Slang` (the notices list only win-x64, linux-x64, osx-arm64).
- UNVERIFIED: whether any BRUTAL consumer other than KSA passes `SetInvertY(true)` or non-default `SetAutoCombinedImageSampler`; both change the SPIR-V the ICD sees.
- UNVERIFIED: whether KSA caches compiled SPIR-V anywhere outside the files read here.

[^shaderc-native]: Brutal.ShaderC ShaderC.cs (native shaderc_shared loading, CreateCompiler, CreateCompilerOptions)
[^shaderc-highlevel]: Brutal.ShaderC HighLevel.cs (Compile, CompileFile, Preprocess, Assemble, ThrowIfFailed)
[^vk-shadermodule-ext]: ShaderModuleExtensions.FromSpirv (VkShaderModuleCreateInfo from a byte span)
[^shaderc-notices]: Brutal.ShaderC third-party notices (native libs shipped per RID)
[^slang-notices]: Brutal.Slang third-party notices (slang-tint ships on win-x64 only)
[^shaderc-structs]: Brutal.ShaderC generated CompileOptions members (SetTargetEnv, SetTargetSpirv, SetOptimizationLevel, SetInvertY, ...)
[^shaderc-kind]: ShaderKind inference from file extension
[^shaderc-options]: Brutal.ShaderC CompileOptions.cs (SetDefaultIncludeCallbacks)
[^shaderc-callbacks]: Brutal.ShaderC default include resolver (disk-relative, depth limit 200)
[^slang-bindings]: Brutal.Slang Slang.cs (COM-style global session, native slang library loading)
[^slang-session-ext]: Brutal.Slang ISessionExtensions.cs (LoadModule, LoadModuleFromSourceString, CreateCompositeComponentType)
[^slang-enums]: Brutal.Slang generated enums (CompileTarget.Wgsl = 28, WgslSpirv = 30, PassThrough.Tint)
[^ksa-moduleutils]: KSA ShaderModuleUtils (decompiled; default CompileOptions, target env / SPIR-V version selection, FromFile/FromString)
[^ksa-pbr]: KSA PbrSpheres (decompiled; per-pipeline macro definitions and specialization constants)
[^ksa-resolve]: KSA ShaderCompilerResolve (decompiled; include callback wrapper)
[^ksa-shaderref]: KSA ShaderReference (decompiled; compile on mod load, include dependency tracking)
[^ksa-reloader]: KSA ShaderReloader (decompiled; file watcher + recompile)
[^ksa-textureset]: KSA bindless texture library header (runtime arrays of texture2D/sampler)
[^slang-release]: Slang v2026.19 release (assets include slang-2026.19-wasm.zip; slang-wasm.wasm measured locally at 24,518,850 bytes)
[^glslang-npm]: @webgpu/glslang on npm (version 0.0.15, last modified 2026-03-18, a WebAssembly build of glslang; queried with `npm view`)
