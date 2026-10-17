---
type: Decision Record
title: Build-time versus runtime SPIR-V to WGSL cross-compilation (recommendation and pipeline sketch)
description: Compares a build-time manifest keyed by SPIR-V content hash (strategy A) with naga-wasm in the browser (strategy B), recommends build time with a runtime fallback, names the authoring language for the PoC, sketches the MSBuild/Vite pipeline with a working prototype, and explains how a runtime-compiling game such as KSA would adapt.
tags: [shaders, decision, build-pipeline, naga-wasm, manifest, glsl, slang, msbuild, vite, ksa]
sources:
  - id: exp02-readme
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/README.md
    title: exp-02 project README
  - id: exp02-build
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/src/build-shaders.mjs
    title: build-shaders.mjs (prototype build pipeline emitting spv, wgsl, manifest.json keyed by sha256)
  - id: exp02-build-log
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/logs/11b-build-shaders.txt
    title: exp-02 step 11b output (7 shaders built, manifest lookup)
  - id: exp02-manifest
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/web-shaders/manifest.json
    title: generated manifest.json
  - id: exp02-logs
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/logs
    title: exp-02 raw logs (timings, KSA survey)
  - id: exp02-browser-naga
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/browser-naga.result.json
    title: naga-wasm in headless Edge 155 (load and translate timings)
  - id: shaderc-highlevel
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.ShaderC/HighLevel.cs
    title: Brutal.ShaderC HighLevel.cs
  - id: ksa-moduleutils
    resource: /Users/asherwin/repos/meow-sci/ksa-game-assemblies/current/decomp/RenderCore/ShaderModuleUtils.cs
    title: KSA ShaderModuleUtils (decompiled)
  - id: glslang-npm
    resource: https://www.npmjs.com/package/@webgpu/glslang
    title: "@webgpu/glslang 0.0.15 (SPIR-V only, 5.1 MB unpacked)"
  - id: slang-release
    resource: https://github.com/shader-slang/slang/releases/tag/v2026.19
    title: Slang v2026.19 release (wasm 24.5 MB raw)
  - id: spirv-tools-npm
    resource: https://www.npmjs.com/package/spirv-tools
    title: spirv-tools on npm (WASM build of SPIRV-Tools, version 20.4.1, last modified 2022-05-18)
  - id: swt
    resource: https://github.com/davnotdev/spirv-webgpu-transform
    title: spirv-webgpu-transform (Rust; no npm/wasm mentioned)
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:38:44Z }
status: draft
---

# Decision

1. **Primary path: build-time cross-compilation (strategy A).** A build step compiles GLSL (later Slang) to SPIR-V with `glslc`, prepares and validates it, translates with naga, patches the WGSL, and writes `shaders/<name>.spv`, `shaders/<name>.wgsl` and a `manifest.json` keyed by the SHA-256 of the SPIR-V bytes. The ICD hashes the bytes it receives in `vkCreateShaderModule` and looks up the WGSL, bindings, push-constant mode and override ids.
2. **Fallback path: runtime naga-wasm (strategy B)** behind the same interface for SPIR-V that is not in the manifest (user-supplied `.spv`, mods). It works only for modules that need no SPIR-V pre-pass the browser cannot do (see below), so it is a best-effort fallback, not the main design.
3. **Authoring language for the PoC: GLSL 450** (Vulkan dialect), compiled with `glslc --target-env=vulkan1.1`, written with separate `texture2D` + `sampler`, vec4-granular UBO arrays, no `gl_PointSize`, direct (unfolded) specialization constants. Slang is a supported second front end; see [Slang](/shaders/slang-wgsl-target.md).
4. **Translator: naga** ([naga](/shaders/naga-spirv-to-wgsl.md)); **validator: the browser's Tint** ([Tint](/shaders/tint-spirv-to-wgsl.md)).

# Evidence that drives the decision

| Fact | Value | Source |
|---|---|---|
| naga-wasm size | 2,053,200 B raw, 592,380 B brotli, 756,207 B gzip | [^exp02-logs] |
| Load + compile in headless Edge 155 (localhost) | `import()` 15.7 to 17.5 ms + fetch/compile/instantiate 11.6 to 12.7 ms | [^exp02-browser-naga] |
| Translate, PoC-sized module | 0.1 to 0.3 ms warm; first call 8.7 to 9.3 ms | [^exp02-browser-naga] |
| Translate **all 187** KSA modules naga can read (2.5 MB SPIR-V to 3.4 MB WGSL), one Node process | 98 to 109 ms total over two runs (mean 0.5 to 0.6 ms), worst 14.6 to 16.5 ms | [^exp02-logs] |
| Browser-side compile cost of generated WGSL (Tint) for the PoC pair | 0.2 to 0.4 ms per module after the first (the first module in a fresh page took up to 21 ms) | [^exp02-browser-naga] |
| Runtime GLSL compile in the browser | no shaderc/Slang native; `@webgpu/glslang` (5.1 MB unpacked) gives SPIR-V only; Slang wasm is 24.5 MB raw | [^glslang-npm][^slang-release] |
| Wasm SPIRV-Tools (needed for `--split-combined-image-sampler` in the browser) | npm `spirv-tools` 20.4.1, last modified 2022-05-18; no maintained build found | [^spirv-tools-npm] |
| `spirv-webgpu-transform` | Rust crate/CLI; README lists no npm or wasm package | [^swt] |

Reading: **translation speed is irrelevant** to the choice (a whole game's shaders translate in about 0.1 s). What decides it is (a) the SPIR-V pre-pass and (b) validation and determinism.

# Comparison

| Criterion | A. Build time + manifest | B. Runtime naga-wasm | Notes |
|---|---|---|---|
| Startup cost | none (fetch WGSL) | +592 KB download, about 30 ms load, plus per-shader translate | B cost is small |
| Bundle size | WGSL text (KSA: 3.4 MB uncompressed for 187 modules) | SPIR-V + wasm | A ships WGSL (compressible); B ships SPIR-V + 2 MB wasm |
| SPIR-V pre-pass (split combined samplers, strip NonReadable) | any native tool at build time (`spirv-opt`) | needs a wasm SPIRV-Tools or a reimplementation of the split pass in C#/JS; only the binding-offset and NonReadable steps are trivial (`spv-prepare.mjs` does them in about 60 lines of JS) | **main blocker for B** |
| Validation | full: naga, then optional headless-browser Tint in CI | failures appear at run time, in the player's browser | A is safer |
| Determinism | WGSL fixed; reviewable diffs | depends on naga-wasm version shipped | A |
| Transparency for unknown SPIR-V | only if hash is in the manifest | any SPIR-V naga can read | B |
| Hash matching | requires byte-identical SPIR-V at run time (same glslc, same options, same macro variant) | none | A needs the app to load the **build's** `.spv` (see KSA) |
| Debuggability | readable WGSL with names if built with `-O0` | same | |
| Failure mode | missing hash gives a clear error naming the shader | late translation error | |
| Hot reload / dev loop | dev server rebuilds changed shaders (Vite plugin) | translate in page | both possible |

# Pipeline sketch (prototype exists: `src/build-shaders.mjs`)[^exp02-build][^exp02-readme]

```
shaders/*.vert|frag|comp (+ *.glsl includes)
  1. strip a UTF-8 BOM if present (glslc then reports `#version` as too old; the three KSA files with a BOM also contain real GLSL errors once the BOM is removed, so they look like dead code)
  2. glslc --target-env=vulkan1.1 -O -I <includes>  -> <name>.spv        (SPIR-V 1.3; avoids OpCopyLogical, DemoteToHelperInvocation)
  3. spirv-val --target-env vulkan1.1
  4. spv-prepare.mjs   : spirv-opt --split-combined-image-sampler, sampler binding +500, strip NonReadable on SSBO members
  5. spv-layout-lint   : fail on std140 scalar/vec2 arrays and 2-row matrices
  6. naga (naga-wasm in Node, or naga CLI)  -> WGSL                      (Y flip applied here by default)
  7. patch-wgsl        : push constants -> uniform@group3 (else storage), diagnostic(off, derivative_uniformity), enable subgroups, strip @coherent
  8. validate          : naga WGSL front end; optional Tint via headless Edge/Chrome (run-browser-check.sh)
  9. emit shaders/<name>.spv (ORIGINAL bytes), <name>.wgsl, manifest.json {sha256(original spv) -> {wgsl, entryPoints, bindings, push, overrides}}
```

Prototype result on the exp-02 shaders (all pass, push mode chosen automatically):

```
OK mesh.frag  sha256=aa83ed139bad... bindings=3 push=none
OK mesh.vert  sha256=b0893f3f87ea... bindings=1 push=none
OK mesh_combined.frag  ... bindings=3 push=none      (sampler2D split: texture@1, sampler@501)
OK mesh_push.vert / mesh_push.frag ... push=uniform   (group 3 binding 0)
OK mesh_push_array.vert ... push=storage              (float[4] needs std430 packing)
```
and a runtime lookup of `sha256(mesh_combined.frag.spv)` returned the WGSL and bindings `0:0 uniform, 0:1 texture_2d, 0:501 sampler`.[^exp02-build-log][^exp02-manifest]

**MSBuild integration** (C# game repo): an `<Target Name="BuildWebShaders" BeforeTargets="BeforeBuild" Condition="'$(RuntimeIdentifier)' == 'browser-wasm'">` with `<ItemGroup><ShaderSource Include="Shaders\**\*.vert;Shaders\**\*.frag;Shaders\**\*.comp" /></ItemGroup>`, `Inputs="@(ShaderSource)" Outputs="$(IntermediateOutputPath)web-shaders\manifest.json"` for incremental builds, an `<Exec Command="node tools/build-shaders.mjs Shaders $(IntermediateOutputPath)web-shaders" />`, and `<Content Include="$(IntermediateOutputPath)web-shaders\**" CopyToOutputDirectory="PreserveNewest" Link="wwwroot\shaders\%(RecursiveDir)%(Filename)%(Extension)" />`. **Vite plugin** alternative: a plugin whose `buildStart`/`handleHotUpdate` runs the same script in-process (naga-wasm works in Node) and emits assets via `this.emitFile`; the dev server re-translates on change, replacing KSA's file-watcher hot reload. Both need `glslc` and `spirv-opt` on the build machine (Vulkan SDK).

**Runtime side (C#)**: `ShaderManifest.Lookup(SHA256(code))` in `vkCreateShaderModule`; on a miss, optionally call `[JSImport]`-ed naga-wasm (`translate({from:"spirv", ...})`, [JS interop](/dotnet-wasm/js-interop-jsimport-jsexport.md)) and apply the same C# patch rules. UNVERIFIED: that `SHA256.HashData` is fast and available on `browser-wasm` (the alternative is hashing in JS with `crypto.subtle.digest` via interop, or a cheap FNV/xxHash key stored beside the SHA-256).

# How a runtime-compiling game (KSA-style) adapts

KSA compiles GLSL files at mod load through `ShaderModuleUtils.FromFile`, with per-pipeline macro variants and `#include` resolved from disk.[^ksa-moduleutils][^shaderc-highlevel] None of that can run in the browser. Options, in order of preference:

1. **Precompile + shim.** Build all shader variants offline (every `(file, defines)` pair actually used, e.g. `INSTANCED=1`, `MULTISAMPLING`, `RAY_QUERIES_SUPPORTED`), store `.spv` + manifest, and replace the compile call with a lookup keyed by `(path, sorted defines, target env)`; the SPIR-V bytes come from the build, so hash lookup is exact. In BRUTAL terms: a browser build of `Brutal.ShaderC` whose `Compile`/`CompileFile` returns the precompiled blob (same `CompilationResult` surface).
2. **Dev-server compile.** During development a local service compiles GLSL on request (native shaderc) and returns SPIR-V + WGSL; production uses option 1.
3. **Ship compilers.** `@webgpu/glslang` + naga-wasm (about 7 MB raw) with an include resolver over fetch/virtual FS; still needs the combined-sampler split pass, so not recommended.

For KSA-scale content the real blocker is not the toolchain but the shaders themselves (bindless, `gl_DrawID`, `SpecConstantOp`, more than 4 descriptor sets): see [KSA portability assessment](/shaders/ksa-shader-portability-assessment.md).

# Risks and mitigations

| Risk | Mitigation |
|---|---|
| glslc version drift changes SPIR-V bytes (hash mismatch) | pin glslc in the build; ship the build's `.spv`; add a CI check that every `.spv` is in the manifest |
| naga upgrade changes WGSL | pin naga/naga-wasm; snapshot-test the PoC WGSL; Tint validation in CI |
| Layout hazards (std140 scalar arrays) | lint fails the build |
| Browser coverage of immediates / uniform standard layout | tier-2 push emulation (uniform/storage at group 3) works everywhere tested; immediates are an optimisation |
| Applications requesting Vulkan 1.3 and getting SPIR-V 1.6 | advertise Vulkan 1.1 from the ICD so shaderc targets SPIR-V 1.3 ([pipeline today](/shaders/brutal-shader-pipeline-today.md)) |

## Open questions

- UNVERIFIED: real-network cost and mobile timings of naga-wasm; only localhost headless Edge on Apple silicon was measured.
- UNVERIFIED: that a SPIR-V 1.3 target (Vulkan 1.1 advertised) is acceptable to BRUTAL consumers that request newer features (the layer would have to hide Vulkan 1.2/1.3 core features the shaders may use).
- UNVERIFIED: whether a wasm SPIRV-Tools exists in a maintained form (the npm `spirv-tools` is 2022); a C# port of the combined-sampler split is the alternative for strategy B.
- Should the runtime fallback reject modules that need the split pre-pass instead of attempting partial support?

[^exp02-logs]: exp-02 raw logs (timings, KSA survey)
[^exp02-build]: build-shaders.mjs (prototype build pipeline emitting spv, wgsl, manifest.json keyed by sha256)
[^exp02-readme]: exp-02 project README
[^exp02-browser-naga]: naga-wasm in headless Edge 155 (load and translate timings)
[^glslang-npm]: "@webgpu/glslang 0.0.15 (SPIR-V only, 5.1 MB unpacked)"
[^slang-release]: Slang v2026.19 release (wasm 24.5 MB raw)
[^spirv-tools-npm]: spirv-tools on npm (WASM build of SPIRV-Tools, version 20.4.1, last modified 2022-05-18)
[^swt]: spirv-webgpu-transform (Rust; no npm/wasm mentioned)
[^exp02-build-log]: exp-02 step 11b output (7 shaders built, manifest lookup)
[^exp02-manifest]: generated manifest.json
[^ksa-moduleutils]: KSA ShaderModuleUtils (decompiled)
[^shaderc-highlevel]: Brutal.ShaderC HighLevel.cs
