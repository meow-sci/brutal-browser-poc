---
type: Technology Assessment
title: naga (SPIR-V front end, WGSL back end) and naga-wasm as the SPIR-V to WGSL translator
description: Measured behaviour of naga 30.0.1 (CLI and the naga-wasm npm package) translating Vulkan SPIR-V to WGSL, including the default Y flip, push constants as var<immediate>, the combined-image-sampler failure and its fix, layout hazards, an unsupported-capability list, browser load cost, and Tint validation of the output.
tags: [shaders, naga, naga-wasm, spirv, wgsl, wasm, push-constants, combined-sampler, y-flip, std140]
sources:
  - id: naga-options
    resource: https://docs.rs/naga/30.0.1/naga/front/spv/struct.Options.html
    title: naga::front::spv::Options (adjust_coordinate_space, strict_capabilities, block_ctx_dump_prefix)
  - id: naga-spv-src
    resource: https://docs.rs/naga/30.0.1/src/naga/front/spv/mod.rs.html
    title: naga 30.0.1 front/spv/mod.rs source (SUPPORTED_CAPABILITIES, SUPPORTED_EXTENSIONS, Options::default, Error variants)
  - id: naga-wasm-readme
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/node_modules/naga-wasm/README.md
    title: naga-wasm 30.2.0 README (API, handles, browser init, license)
  - id: naga-wasm-dts
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/node_modules/naga-wasm/dist/wasm/naga.d.ts
    title: naga-wasm 30.2.0 type declarations (option shapes; no SPIR-V parse options)
  - id: naga-wasm-npm
    resource: https://www.npmjs.com/package/naga-wasm
    title: naga-wasm on npm (version 30.2.0, modified 2026-09-30, MIT OR Apache-2.0; metadata via npm view)
  - id: naga-wasm-repo
    resource: https://github.com/kekkon-nexus/naga-wasm
    title: kekkon-nexus/naga-wasm repository
  - id: exp02-readme
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/README.md
    title: exp-02 project README (how to reproduce)
  - id: exp02-logs
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/logs
    title: exp-02 raw logs (01-glslc ... 13-ksa-*)
  - id: exp02-browser
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/browser-check.result.json
    title: Tint + GPU results (headless Edge 155, Apple Metal) for every generated WGSL, the winding test, pipelines and the std140 probe
  - id: exp02-browser-naga
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/browser-naga.result.json
    title: naga-wasm running inside headless Edge 155 (load, translate and Tint timings)
  - id: swt
    resource: https://github.com/davnotdev/spirv-webgpu-transform
    title: spirv-webgpu-transform (SPIR-V rewriting for naga/Tint; BSD-2-Clause)
  - id: chrome-immediates
    resource: https://developer.chrome.com/blog/new-in-webgpu-149-150
    title: "What's new in WebGPU (Chrome 149 and 150): immediates"
  - id: webgpu-spec
    resource: https://www.w3.org/TR/webgpu/
    title: WebGPU W3C specification (limits table, immediateSize validation)
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:38:44Z }
status: draft
---

# Verdict

naga is the only SPIR-V to WGSL translator that is available today as (a) a maintained Rust crate, (b) a CLI, and (c) a ready-made WebAssembly package (`naga-wasm`) usable from a browser. On the PoC shader pair it works first time and the output compiles in a real browser. It needs a small SPIR-V pre-pass (combined image samplers, write-only buffers) and a WGSL post-pass (module directives), and it has hard gaps (bindless arrays, `SpecConstantOp`, `gl_DrawID`, `OpCopyLogical`, scalar-array std140 layout) that [KSA](/shaders/ksa-shader-portability-assessment.md) triggers. Recommended role: the translator in the build pipeline and, if runtime translation is needed, the same translator shipped as wasm ([build-time vs runtime](/shaders/build-time-vs-runtime-cross-compilation.md)).

# Versions and packaging

| Item | Value | Source |
|---|---|---|
| naga used | 30.0.1 (CLI built with `cargo install naga-cli`; the same version is bundled in naga-wasm) | [^exp02-logs] |
| naga-wasm (repository `kekkon-nexus/naga-wasm`)[^naga-wasm-repo] | 30.2.0, published 2026-09-30, `MIT OR Apache-2.0`, `engines.node >= 24.12`; "the major version follows naga's major version" | [^naga-wasm-npm][^naga-wasm-readme] |
| Front ends / back ends | in: WGSL, GLSL, SPIR-V; out: WGSL, GLSL, SPIR-V, HLSL, MSL | [^naga-wasm-readme] |
| `naga_bg.wasm` size | 2,053,200 bytes raw; 756,207 gzip -9; 592,380 brotli q11 (all front/back ends are compiled in; a build with only `spv-in` + `wgsl-out` would be smaller, UNVERIFIED) | [^exp02-logs] |

# naga-wasm API surface

Handle-based, plus a one-shot `translate`:[^naga-wasm-readme][^naga-wasm-dts]

```ts
import { translate, parseSpirv, validate, writeWgsl, NagaError, init, nagaVersion } from "naga-wasm";
const wgsl = translate({ from: "spirv", source: spvBytes /* Uint8Array | Uint32Array */, to: "wgsl" });
// or: using m = parseSpirv(spv); using info = validate(m); const wgsl = writeWgsl(m, info);
```

- Node: the wasm is loaded synchronously on import. Browser: call `await init()` once; `init({ module_or_path: url | Response | bytes })` lets the host serve the wasm; the binary is exported as `naga-wasm/naga_bg.wasm`.[^naga-wasm-readme]
- `parseSpirv(words)` takes **no options**: `adjust_coordinate_space` and `strict_capabilities` cannot be changed from JS (see below). The WGSL writer has one flag (`explicitTypes`).[^naga-wasm-dts]
- Handles hold wasm memory; release with `using` or `.free()`.[^naga-wasm-readme]
- Errors are `NagaError { kind: "parse" | "validation" | "write", message, formatted }`. A wasm `RuntimeError` (unreachable) escapes unwrapped when naga panics; the harness treats it as a hard failure (4 of 267 KSA shaders panic inside naga's `front/spv/image.rs` or `proc/emitter.rs`).[^exp02-logs]
- `bindingMap` (explicit binding slots) exists only for `writeGlsl`; for WGSL there is no binding-map option, so group/binding numbers come straight from the SPIR-V decorations and any renumbering must happen in SPIR-V (pre-pass) or in the WGSL text.[^naga-wasm-readme]
- No reflection API for WGSL output (reflection is offered only for the GLSL writer). Bindings can be read back from the WGSL text (`reflectBindings` in `projects/exp-02-shader-xcompile/src/patch-wgsl.mjs`) or from the SPIR-V (see [other routes and reflection](/shaders/glsl-direct-routes-spirv-cross-and-reflection.md)).

# Measured cost

| Measurement | Result | Source |
|---|---|---|
| Node 24: import + wasm compile + instantiate | 3.3 to 4.4 ms (several runs) | [^exp02-logs] |
| Node 24: PoC shader first call (JIT warm-up) | 8 to 10 ms for the first shader, about 0.2 to 0.9 ms for the next | [^exp02-logs] |
| Node 24: warm translate, 1.5 KB SPIR-V | 0.05 to 0.13 ms (p95 0.21 ms) | [^exp02-logs] |
| Node 24: warm translate, 195 KB SPIR-V (largest KSA module that translated) to 357 KB WGSL | 5.7 ms | [^exp02-logs] |
| **Headless Edge 155 (Apple GPU)**: `import()` of the ESM package, no bundler | 15.7 to 17.5 ms (two runs) | [^exp02-browser-naga] |
| Edge 155: `fetch` + streaming compile + instantiate of the 2.05 MB wasm from localhost | 11.6 to 12.7 ms (localhost; network transfer not included; 592 KB brotli on the wire) | [^exp02-browser-naga] |
| Edge 155: first translate of the PoC vertex shader / warm | 8.7 to 9.3 ms / 0.1 to 0.3 ms; Tint's first `createShaderModule` in a fresh page took 0.4 to 21 ms, later modules 0.2 to 0.4 ms | [^exp02-browser-naga] |

naga-wasm therefore runs in the browser as a plain ES module plus one `fetch` of the wasm; the translation itself is negligible next to pipeline creation.

# Behaviour on Vulkan SPIR-V (all observed, naga 30.0.1)

| Topic | Observation | Consequence |
|---|---|---|
| **Y flip** | `Options::default()` has `adjust_coordinate_space: true` ("the IR coordinate space matches all the APIs except SPIR-V, so by default we flip the Y coordinate").[^naga-options][^naga-spv-src] The generated vertex entry point ends with `global_6.member.y = -(_e11);`. The CLI flag `--keep-coordinate-space` removes it. **naga-wasm cannot disable it** (`parseSpirv` has no options; `adjustCoordinateSpace` exists only on the `writeSpirv`/`writeGlsl` options, not on `writeWgsl`[^naga-wasm-dts]). naga's GLSL front end does **not** flip. | For Vulkan-convention shaders this is exactly what WebGPU needs: a rendered triangle appears where Vulkan puts it and `frontFace` stays 1:1 (verified on a real GPU,[^exp02-browser] see [conventions](/shaders/wgsl-conventions-for-vulkan-layer.md)). Do not add a second flip (and do not combine with shaderc `SetInvertY`). |
| **Push constants** | `layout(push_constant)` becomes `var<immediate> global: type_3;` (naga 30.0.1 uses the immediate address space; older naga releases wrote `var<push_constant>`, UNVERIFIED which release renamed it). No `requires` line is emitted. Struct members are anonymous (`member`, `member_1`) unless names survive (`glslc` without `-O`). | Browsers accept `var<immediate>` only where immediates ship (Chrome 149/150)[^chrome-immediates] and only up to `maxImmediateSize` (spec default 64 B[^webgpu-spec], 64 B on the test adapter). Patch to a uniform/storage buffer otherwise, see [conventions](/shaders/wgsl-conventions-for-vulkan-layer.md). |
| **Combined image sampler** (`sampler2D`) | SPIR-V declares a variable of `OpTypeSampledImage`; naga fails with the unhelpful `InvalidId(39)` (parse error). `spirv-opt --split-combined-image-sampler` splits it but gives both new variables the **same** (set, binding), which naga then rejects (`Bindings for [3] conflict with other resource`). | Needs a pre-pass that also moves the sampler to a new binding. `src/spv-prepare.mjs` does split + `binding + 500`; `spirv-webgpu-transform combimg` is the third-party alternative (it renumbers sequentially and returns a correction map).[^swt] |
| **Specialization constants** | `OpSpecConstant` (scalar int/float/bool) is imported as `@id(N) override ...` keeping the Vulkan `SpecId` as the WGSL `@id`. `OpSpecConstantOp` (what glslang emits for `1.0/GAMMA`, `FLAG ? a : b`, `N * 2`) fails: `UnsupportedInstruction(Type, SpecConstantOp)`. `local_size_x_id` fails validation (`Constant [4] '' is invalid`). | Keep spec constants simple and used directly; 22 of 267 KSA shaders hit `SpecConstantOp`. The override names are synthetic (`override_type_3_`), only `@id` is stable. |
| **Uniform layout** | naga builds WGSL types from SPIR-V types and **ignores `ArrayStride` / `MatrixStride`**. Verified on a GPU:[^exp02-browser] a std140 block `{ float f[4]; vec2 v2[2]; vec3 v3; float tail; mat3 m3; }` read at the wrong offsets (observed slots 1, 3, 6, 8, 11, 17 instead of the std140 slots 4, 12, 20, 24, 27, 33). `mat2` in std140 (column stride 16) is rejected: `UnsupportedMatrixStride { stride: 16, columns: 2, rows: 2 }`. A companion probe with `vec4`, `mat4`, `vec3` + `float` packing, `mat3` and `vec4[2]` read exactly the std140 offsets on the GPU (slots 1, 10, 22, 23, 29, 43 as expected), so those members are safe. | Layout hazard, silent. Use `vec4` array elements in UBOs, or lint SPIR-V (`src/spv-layout-lint.mjs`). KSA modules: 0 of 258 flagged. |
| **Builtins** | `gl_VertexIndex`, `gl_InstanceIndex`, `gl_FragCoord`, `gl_FrontFacing`, `gl_SampleID`, `gl_FragDepth` become `@builtin(vertex_index / instance_index / position / front_facing / sample_index / frag_depth)`; `flat` becomes `@interpolate(flat)`. `gl_PointSize` **write** fails in the WGSL writer (`Unsupported builtin value: PointSize`). `gl_ClipDistance` becomes `enable clip_distances;` + `@builtin(clip_distances)` (needs the `clip-distances` device feature). `gl_DrawID`/`gl_BaseVertex`/`gl_BaseInstance` (capability `DrawParameters`) fail: `UnsupportedCapability(DrawParameters)`. | Shaders must not write `gl_PointSize`; draw parameters need ICD emulation (immediates carrying firstInstance/draw index). |
| **Textures / samplers** | separate `texture2D` + `sampler` map 1:1 to `texture_2d<f32>` + `sampler` at the same (group, binding). Cube, 2D-array, 3D, depth + `samplerShadow` (`texture_depth_2d` + `sampler_comparison`, `textureSampleCompare`), `texture2DMS` (`texture_multisampled_2d`, `textureLoad` with `sample_index`), storage images (`texture_storage_2d<rgba8unorm,write>`, `r32float,read_write`) all translate. | Fine for the PoC. |
| **Arrays of resources** | constant-size `texture2D tex[4]` -> `enable wgpu_binding_array;` + `binding_array<texture_2d<f32>, 4>`: **native wgpu only, rejected by Tint** (`expected extension`). Runtime arrays with `nonuniformEXT` -> `UnsupportedCapability(ShaderNonUniform)`. | No bindless. `spirv-webgpu-transform bindingarray` can split small constant arrays into N bindings.[^swt] |
| **Atomics / shared / barriers** | `atomic<u32>`, `var<workgroup>`, `workgroupBarrier()` translate. 64-bit: `int64`/`double` produce `u64`/`f64` in WGSL which browsers reject (`expected ')'`). Subgroup ops (`subgroupAdd`) translate **without** an `enable subgroups;` directive; Tint then errors. | Patch `enable subgroups;`; forbid 64-bit types. |
| **Discard / helper invocations** | `discard` (`OpKill`) -> `discard;`. SPIR-V 1.6 builds use `OpDemoteToHelperInvocation`: `UnsupportedCapability(DemoteToHelperInvocation)` and unsupported instruction. | Avoid SPIR-V >= 1.6 for naga (below). |
| **Derivatives in non-uniform flow** | GLSL freely calls `dFdx`/`fwidth`/implicit-LOD `texture()` after `discard` or in branches; Tint rejects it (`'fwidth' must only be called from uniform control flow`). Prepending `diagnostic(off, derivative_uniformity);` fixes it (verified in a browser). `workgroupBarrier()` under divergent control flow is a hard error that the diagnostic does **not** suppress. | Add the directive to every module; barriers in divergent flow need a shader fix (5 of 187 translated KSA shaders). |
| **Write-only SSBO** | `writeonly buffer` -> `Storage address space doesn't support write-only access` (validation). Stripping the `NonReadable` decoration (`src/spv-prepare.mjs`) yields `var<storage, read_write>`. | Pre-pass. |
| **`coherent`** | `coherent buffer` becomes a native-only `@coherent var<storage...>` attribute that browsers reject (`expected attribute`). | Post-pass strips it (`--directives`). |
| **`isnan` / `isinf`** | `Unsupported relational function: IsNan / IsInf` (write error). | `spirv-webgpu-transform isnanisinf` exists.[^swt] |
| **Other unsupported** | `PhysicalStorageBufferAddresses` (buffer device address), `Tessellation`, ray tracing (`RayTracingKHR`, `SPV_KHR_ray_tracing`), `GroupNonUniformElect`, `ImageGatherExtended`, `UnsupportedStorageClass`, execution models other than Vertex, Fragment, GLCompute, TaskEXT, MeshEXT. | Hard gaps. |
| **Entry points** | The WGSL function keeps the `OpEntryPoint` name (`main` from glslc; `vsMain`/`fsMain` from a multi-entry Slang module). All entry points of a module are emitted; the CLI can select one (`--entry-point`). | `GPUProgrammableStage.entryPoint` must use the same name from `VkPipelineShaderStageCreateInfo::pName`. |
| **Names** | `glslc -O` strips `OpName`, so WGSL has `global_1`, `type_3`; `-O0` keeps `Globals`, `u`, `tex`. | Cosmetic; keep names in debug builds. |

# SPIR-V version and capability limits

- naga accepts SPIR-V 1.0 to 1.3 output of glslc/Slang without issue and also read 1.5/1.6 modules of simple shaders (the PoC mesh pair at `vulkan1.3` = SPIR-V 1.6 works).
- `OpCopyLogical` (emitted by glslang for whole-struct copies between differently decorated types, SPIR-V 1.4+) is **unsupported** (`UnsupportedInstruction(Function, CopyLogical)`). On the KSA corpus, targeting Vulkan 1.1 instead of 1.3 raised naga success from 76 to 122 of 267 stage files (without pre-pass) and from 118 to 187 (with pre-pass).[^exp02-logs]
- `Options::default().strict_capabilities` is `true`: capabilities outside `SUPPORTED_CAPABILITIES` fail with `UnsupportedCapability`. Supported (30.0.1) include Shader, VulkanMemoryModel, ClipDistance, CullDistance, SampleRateShading, DerivativeControl, Matrix, ImageQuery, Sampled1D/Image1D, SampledCubeArray/ImageCubeArray, StorageImageExtendedFormats, Int8/16/64, Int64Atomics, Float16/64, AtomicFloat32AddEXT, Geometry, MultiView, 16-bit storage, GroupNonUniform{,Vote,Arithmetic,Ballot,Shuffle,ShuffleRelative}, RuntimeDescriptorArray, StorageImageMultisample, FragmentBarycentricKHR; extensions: `SPV_KHR_storage_buffer_storage_class`, `vulkan_memory_model`, `multiview`, `SPV_EXT_descriptor_indexing`, `SPV_EXT_shader_atomic_float_add`, `16bit_storage`, `non_semantic_info`, `fragment_shader_barycentric`; extended instruction sets: `GLSL.std.450` and `NonSemantic.Shader.DebugInfo.100`.[^naga-spv-src]
- Therefore the BRUTAL ICD should advertise **Vulkan 1.1 (SPIR-V 1.3)** to applications that derive the shader target from the device (KSA does, see [pipeline today](/shaders/brutal-shader-pipeline-today.md)); see also [limits and features](/webgpu/webgpu-limits-features-and-compat-mode.md).

# naga CLI options worth knowing (30.0.1)

`--keep-coordinate-space`, `--entry-point`, `--compact`, `--override name=value` (apply override constants at translation time), `--capabilities` (list or `all`/`none`), `--shader-stage`, `--input-kind spv|glsl|wgsl`, `--validate <bits>`, `--index-bounds-check-policy`, `--buffer-bounds-check-policy`, `--bulk-validate`, `-g` (debug symbols, SPIR-V out only), `--block-ctx-dir` (dump). Output kind follows the output file extension.[^exp02-logs] naga-wasm and the CLI produced **byte-identical WGSL** for the PoC shaders.

# What the PoC build should run

(Scripts and reproduction: [exp-02 README](/shaders/experiment-results-exp-02.md).[^exp02-readme])

1. `glslc --target-env=vulkan1.1 -O0|-O` (SPIR-V 1.3) or Slang `-profile spirv_1_3` (see [Slang](/shaders/slang-wgsl-target.md)).
2. `node spv-prepare.mjs` (split combined samplers, strip NonReadable) and `spv-layout-lint.mjs`.
3. naga (`spv2wgsl.mjs`) then `patch-wgsl.mjs --directives --push uniform|storage|keep`.
4. Optional: Tint in a browser (or `createShaderModule` + `getCompilationInfo`) as the final validator; in these experiments Tint accepted 164 of the 187 KSA modules that naga translated once directives were patched (raw: 156).[^exp02-logs]

## Open questions

- UNVERIFIED: naga-wasm load time over a real network and on a mid-range mobile device (only localhost in headless Edge 155 on an Apple GPU was measured).
- UNVERIFIED: size of a custom naga wasm build with only `spv-in` and `wgsl-out` (and with `panic=abort`, `opt-level=z`); not built here.
- UNVERIFIED: whether upstream naga will add `OpCopyLogical`, `OpSpecConstantOp` or `DrawParameters` support (issue trackers were not searched).
- UNVERIFIED: whether naga emits `enable subgroups;` for other subgroup builtins or only omits it for `subgroupAdd`; one probe was run.
- Is a wrapper crate (naga as a library compiled with wasm-bindgen) worth owning so that `adjust_coordinate_space`/`strict_capabilities` and a SPIR-V pre-pass can live inside the same wasm module?

[^naga-options]: naga::front::spv::Options (adjust_coordinate_space, strict_capabilities, block_ctx_dump_prefix)
[^naga-spv-src]: naga 30.0.1 front/spv/mod.rs source (SUPPORTED_CAPABILITIES, SUPPORTED_EXTENSIONS, Options::default, Error variants)
[^naga-wasm-readme]: naga-wasm 30.2.0 README (API, handles, browser init, license)
[^naga-wasm-dts]: naga-wasm 30.2.0 type declarations (option shapes; no SPIR-V parse options)
[^naga-wasm-npm]: naga-wasm on npm (version 30.2.0, modified 2026-09-30, MIT OR Apache-2.0; metadata via npm view)
[^naga-wasm-repo]: kekkon-nexus/naga-wasm repository
[^exp02-readme]: exp-02 project README (how to reproduce)
[^chrome-immediates]: What's new in WebGPU (Chrome 149 and 150): immediates
[^webgpu-spec]: WebGPU W3C specification (limits table, immediateSize validation)
[^exp02-logs]: exp-02 raw logs (01-glslc ... 13-ksa-*)
[^exp02-browser]: Tint + GPU results (headless Edge 155, Apple Metal) for every generated WGSL, the winding test, pipelines and the std140 probe
[^exp02-browser-naga]: naga-wasm running inside headless Edge 155 (load, translate and Tint timings)
[^swt]: spirv-webgpu-transform (SPIR-V rewriting for naga/Tint; BSD-2-Clause)
