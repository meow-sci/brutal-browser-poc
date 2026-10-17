---
type: Experiment Report
title: "exp-02: SPIR-V to WGSL cross-compilation experiments (glslc, naga, Slang, Tint-in-browser)"
description: Exact commands, outputs, failures, sizes and timings from the exp-02 project, which compiles the PoC shaders and variants with glslc and Slang, translates them with naga (CLI and wasm), patches the WGSL, and validates everything with the browser's own Tint on a real GPU, plus a characterisation run over the KSA shader corpus.
tags: [experiment, shaders, naga, naga-wasm, slang, glslc, tint, wgsl, spirv, headless-edge, ksa]
sources:
  - id: project
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/README.md
    title: exp-02 project README (layout, environment variables, reproduce)
  - id: runsh
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/run.sh
    title: run.sh (every command used below)
  - id: logs
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/logs
    title: out/logs (00-versions ... 13-ksa-*)
  - id: browser-result
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/browser-check.result.json
    title: headless Edge 155 result (Tint, render test, pipelines, std140 probe, limits, language features)
  - id: browser-naga-result
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/browser-naga.result.json
    title: naga-wasm inside Edge 155
  - id: manifest
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/web-shaders/manifest.json
    title: build-pipeline prototype manifest
  - id: shaders
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/shaders
    title: shader sources (GLSL, Slang, probes)
  - id: naga-wasm-npm
    resource: https://www.npmjs.com/package/naga-wasm
    title: naga-wasm 30.2.0 (npm)
  - id: naga-options
    resource: https://docs.rs/naga/30.0.1/naga/front/spv/struct.Options.html
    title: naga::front::spv::Options
  - id: slang-release
    resource: https://github.com/shader-slang/slang/releases/tag/v2026.19
    title: Slang v2026.19 release
  - id: swt
    resource: https://github.com/davnotdev/spirv-webgpu-transform
    title: spirv-webgpu-transform 0.1.7
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:38:44Z }
status: draft
---

# Environment

| Tool | Version (from `out/logs/00-versions.txt`) |
|---|---|
| glslc | shaderc v2023.8 (`v2025.3-10-gc7e73e8`), glslang 11.1.0-1302, SPIR-V target default 1.0; `/usr/local/bin/glslc` and the Vulkan SDK 1.4.328.1 copy report the same |
| SPIRV-Tools | v2025.4 (`spirv-val`, `spirv-opt`, `spirv-dis`, `spirv-cross` and `spirv-reflect` from SDK 1.4.328.0/1) |
| naga CLI | 30.0.1 (`cargo install naga-cli --root <scratch>`, built in a few minutes) |
| naga-wasm | npm 30.2.0; bundles naga 30.0.1 (`nagaVersion`) |
| Slang | `/usr/local/bin/slangc` **2025.17.2**; **2026.19** (macOS aarch64 release tarball) in a scratch directory |
| spirv-webgpu-transform | 0.1.7 (`cargo install`) |
| Node | v24.18.0 |
| Browser | Microsoft Edge 155.0.4283.45 headless (`HeadlessChrome/155`), adapter vendor apple, architecture metal-3 |
| Host | macOS on arm64 |

Project: `projects/exp-02-shader-xcompile/` (README explains the environment variables). One command reproduces everything: `NAGA_CLI=... SWT=... SLANGC26=... BROWSER_CHECK=1 KSA=1 ./run.sh`.[^project][^runsh]

# 7a. The PoC shaders through glslc and naga

Sources: `mesh.vert` (UBO `{model, view, proj, lightDir}` at set 0 binding 0; inputs 0 vec3, 1 vec3, 2 vec2), `mesh.frag` (set 0 binding 1 `texture2D`, binding 2 `sampler`, lambert, output location 0), variants `mesh_combined.frag` (`sampler2D` at binding 1), `mesh_push.vert`/`.frag` (push-constant block `{mat4 model; vec4 tint;}`), `mesh_push_array.vert` (adds `float weights[4]`).[^shaders]

```
glslc --target-env=vulkan1.3 -O shaders/mesh.vert -o out/mesh.vert.spv && spirv-val --target-env vulkan1.3 out/mesh.vert.spv   # OK, 1640 B, SPIR-V 1.6
glslc ... mesh.frag  -> 1460 B ; mesh_combined.frag -> 1348 B ; mesh_push.vert -> 1820 B ; mesh_push.frag -> 828 B   # all spirv-val OK
node src/spv2wgsl.mjs out/mesh.vert.spv out/wasm/mesh.vert.wgsl      # naga-wasm
naga out/mesh.vert.spv out/cli/mesh.vert.wgsl                         # naga CLI: byte-identical output
```

| Input | naga-wasm result |
|---|---|
| `mesh.vert` | OK; 1534 B WGSL; `@group(0) @binding(0) var<uniform> global: type_3;`, `@vertex fn main(@location(0) .. @location(2) ..)`; the entry ends with `global_6.member.y = -(_e11);` (Y flip) |
| `mesh.frag` | OK; 921 B; `@group(0) @binding(1) var global_2: texture_2d<f32>;` `@group(0) @binding(2) var global_3: sampler;` `textureSample(...)`; `@fragment fn main(...) -> @location(0) vec4<f32>` |
| `mesh_combined.frag` | **FAIL** `kind=parse message=InvalidId(39)` (the id is the `OpLoad` of the combined sampled image) |
| `mesh_push.vert` | OK; `var<immediate> global: type_3;` (`member: mat4x4<f32>, member_1: vec4<f32>`); UBO unchanged |
| `mesh_push.frag` | **FAIL** `InvalidId(14)` (combined `sampler2D` again) |
| `spirv-opt --split-combined-image-sampler` then naga | **FAIL** validation: `Bindings for [3] conflict with other resource` (image and sampler both keep set 0 binding 1) |
| `spv-prepare.mjs` (split + sampler binding +500) then naga | OK: `@group(0) @binding(1) var global_3: texture_2d<f32>;` and `@group(0) @binding(501) var global_2: sampler;` |
| `spirv-webgpu-transform combimg` | OK: sampler gets binding 2 (summary `Binding 1 <- [SplitCombined]`), correction map reported[^swt] |
| naga GLSL front end on the GLSL files | `mesh.vert`, `mesh.frag`, `mesh_push.vert` OK (no Y flip); `sampler2D` fails: `Not implemented: variable qualifier` |

naga `--keep-coordinate-space` (CLI) removes exactly the two flip lines; naga-wasm has no such option (`parseSpirv` takes no options), and its output equals the CLI default output.[^naga-options]

# 7a (continued). Feature probes (`run-probes.sh`, glslc `vulkan1.1`, naga-wasm)

| Probe | Result |
|---|---|
| builtins: `gl_VertexIndex`, `gl_InstanceIndex`, `gl_FragCoord`, `gl_FrontFacing`, `discard`, `dFdx/dFdy/fwidth`, `gl_FragDepth`, `flat` | OK (builtins mapped; `discard;`) |
| `gl_PointSize = 2.0` write | FAIL write: `Unsupported builtin value: PointSize` |
| `gl_ClipDistance` | OK: `enable clip_distances;` + `@builtin(clip_distances)` |
| atomics + shared memory + `barrier()` | OK |
| `uint64_t` / `double` | naga OK but emits `u64` / `f64`; Tint: `expected ')'` |
| subgroup (`subgroupAdd`) | naga OK, **no `enable subgroups;`**; Tint: `cannot call built-in function 'subgroupAdd' without extension 'subgroups'` |
| `texture2D texs[4]` constant index | naga OK as `enable wgpu_binding_array;` + `binding_array<texture_2d<f32>, 4>`; Tint: `expected extension` |
| `texture2D texs[]` + `nonuniformEXT` | FAIL `UnsupportedCapability(ShaderNonUniform)` |
| spec constants used directly (int, float, bool) | OK: `@id(8) override ...: f32`, `@id(9) ...: bool`, `@id(7) ...: i32` |
| spec constants with folded expressions / `local_size_x_id` | FAIL `UnsupportedInstruction(Type, SpecConstantOp)` / `Constant [4] '' is invalid` |
| `writeonly buffer` | FAIL validation (write-only storage); OK after `spv-prepare.mjs` |
| storage images (`rgba8` write-only, `r32f` read-write) | OK: `texture_storage_2d<rgba8unorm,write>`, `<r32float,read_write>` |
| cube, 2D array, 3D, depth + `samplerShadow` | OK: `texture_cube`, `texture_2d_array`, `texture_3d`, `texture_depth_2d` + `sampler_comparison`, `textureSampleCompare` |
| `texture2DMS` + `gl_SampleID` | OK: `texture_multisampled_2d`, `@builtin(sample_index)` |
| `layout(push_constant)` with `mat2` | OK in std430 (the `spirv-webgpu-transform` README says it fails in older naga) |
| `mat2` in a std140 UBO | FAIL `UnsupportedMatrixStride { stride: 16, columns: 2, rows: 2, width: 4 }` |
| `GL_EXT_buffer_reference` | FAIL `UnsupportedCapability(PhysicalStorageBufferAddresses)` |
| whole-struct copy (`OpCopyLogical`) | `vulkan1.1` (SPIR-V 1.3) OK; `vulkan1.2` (1.5) and `vulkan1.3` (1.6) FAIL `UnsupportedInstruction(Function, CopyLogical)` |
| std140 `float[4]`, `vec2[2]`, `vec3`, `float`, `mat3` | naga OK, **reads wrong offsets on a GPU** (see below) |

# 7b. Slang

Commands (also in `run.sh` step 05; failures kept):

```
slangc shaders/mesh.slang -target spirv -profile spirv_1_5 -entry vsMain -stage vertex -entry fsMain -stage fragment -o out/slang/mesh.slang.spv   # spirv-val OK
slangc shaders/mesh.slang -target wgsl  -entry vsMain -stage vertex -entry fsMain -stage fragment -o out/slang/mesh.slang.wgsl
```

First attempt failed on my source: `[[vk::location(0)]]` on the fragment function is "not valid here" (error 31002); `SV_Target` already maps to location 0.

| Experiment | slangc 2025.17.2 | slangc 2026.19 |
|---|---|---|
| mesh.slang SPIR-V vs WGSL bindings | UBO 0, texture 1, sampler 2 in both; locations 0..2 / 0..1; WGSL valid (naga) and compiled by Tint | same |
| Slang SPIR-V through naga | OK (3470 B WGSL) | OK (2382 B) |
| `[[vk::push_constant]]` + `Sampler2D` (mesh_push.slang) | WGSL `var<uniform> pc_0 : Push_std430_0;` **without group/binding**; naga: `Binding decoration is missing or not applicable`; `Sampler2D` becomes `tex_texture_0` at binding 1 + `tex_sampler_0` at binding 2 | identical |
| Portable push pattern (`-DTARGET_WGSL`, `[[vk::binding(0,3)]]`) | `@group(3) @binding(0) var<uniform> pc_0 : Push_std140_0;` but **invalid**: vertex output member `uv_0` has no `@location` (Tint: `missing entry point IO attribute`) | **valid** (naga and Tint) |
| `register(b0/t1/s2)` + `ParameterBlock` + `[vk::constant_id(7)]` + `SV_VertexID/InstanceID` | 6 warnings 39029; bindings 0/1/2 in set 0, ParameterBlock at group 1 (0/1/2); `@id(7) override`; WGSL **invalid** (vertex output locations missing) | valid |
| SPIR-V of the same probe through naga | FAIL `UnsupportedCapability(DrawParameters)` | same |
| `SV_VulkanVertexID/InstanceID` | no DrawParameters; naga OK | n/a (same) |
| `-fvk-b-shift 10 -fvk-t-shift 20 -fvk-s-shift 30 0` | SPIR-V bindings 10/21/32; WGSL: tex/sampler lose `@binding/@group` | WGSL bindings 10/21/32 |
| `-fvk-use-entrypoint-name` (single entry) | SPIR-V entry `main` by default, `vsMain` with the flag | same |
| `-fvk-invert-y` | SPIR-V gains one `OpFNegate`; WGSL unchanged | same |
| `-matrix-layout-column-major` | SPIR-V byte-identical to the default | not re-run |
| `-profile spirv_1_3` (also 1_5 and 1_6 interactively) | accepted by naga for mesh.slang, no `OpCopyLogical` | not re-run |
| `-reflection-json` | 14,739 B (parameters with `descriptorTableSlot` index, struct field offsets, `varyingInput` counts) | 33,873 B |

Slang wasm (not run, measured): `slang-2026.19-wasm.zip` is 10.17 MB; `slang-wasm.wasm` 24,518,850 B; `slang-wasm.js` 154,529 B.[^slang-release]

# 7c. KSA gap characterisation

Pipeline: `glslc --target-env=<env> -O -I Core/Shaders -I Content ...`, optional `spv-prepare.mjs`, naga-wasm; Tint via headless Edge. Details and interpretation in [KSA assessment](/shaders/ksa-shader-portability-assessment.md).

| Variant (267 stage files) | glslc fail | naga fail | naga OK |
|---|---|---|---|
| `vulkan1.3` (SPIR-V 1.6) | 4 | 187 | 76 |
| `vulkan1.3` + pre-pass | 4 | 145 | 118 |
| `vulkan1.1` (SPIR-V 1.3) | 9 | 136 | 122 |
| `vulkan1.1` + pre-pass | 9 | 71 | **187** |

Remaining naga failure classes (`vulkan1.1` + pre-pass): `SpecConstantOp` 22; vertex entry invalid 8 (draw index builtin); `DrawParameters` 8; fragment entry invalid 7 (bindless binding conflicts); tessellation 4; `InvalidImage` 4; naga panics 4; `ShaderNonUniform` 3; `InvalidId` 3; `GroupNonUniformElect` 2; `isnan`/`isinf` 2; others 4. Tint over the 187 WGSL modules: 156 OK raw, 164 with directive patches; push-constant sizes: 94 shaders, 29 over 64 B, max 128 B; layout lint: 0 of 258; translating all 187 modules took about 100 ms (98 and 109 ms in two runs).[^logs]

Named shaders: `FullScreenTriangle.vert`, `Line.vert/frag`, `Grid.vert`, `Sprite.vert`, `Star.vert` translate and compile; `Copy.frag` needs the pre-pass; `Grid.frag` (`IsInf`), `GenericMesh.vert` (`gl_DrawID`) and `GenericMesh.frag` (bindless) fail.

# 7d. naga-wasm: size, speed, browser

| Measurement | Result |
|---|---|
| `naga_bg.wasm` | 2,053,200 B raw; 756,207 B gzip -9; 592,380 B brotli q11; package unpacked 2,113,104 B; ESM, Node >= 24.12; licence MIT OR Apache-2.0[^naga-wasm-npm] |
| Node: import + compile + instantiate | 3.3 to 4.4 ms (several runs) |
| Node: PoC shaders, first call / warm mean (n = 200) | 8 to 10 ms first; 0.05 to 0.13 ms warm (p95 0.06 to 0.21 ms) |
| Node: largest translated KSA modules (195 KB and 176 KB SPIR-V) | 5.7 ms and 4.4 ms warm |
| Browser (Edge 155): `import('/node_modules/naga-wasm/dist/index.js')` | 15.7 to 17.5 ms (two runs) |
| Browser: `fetch` + `init({ module_or_path: response })` | 11.6 to 12.7 ms (localhost) |
| Browser: translate PoC vertex shader, first / warm | 8.7 to 9.3 ms / 0.3 ms; Tint first compile 0.4 to 21 ms |
| Browser: error path | `NagaError { kind: "parse", message: "InvalidId(39)" }` thrown for the combined-sampler module |

It runs unmodified as a plain ES module in a browser; `fetch` of the wasm is done by `init()` (the browser entry) while the Node entry reads the file synchronously.[^browser-naga-result]

# 7e. WGSL patches (`src/patch-wgsl.mjs`) validated by naga and by Tint

| Patch | Input | Result |
|---|---|---|
| Y-flip wrapper | naga `--keep-coordinate-space` output; Slang 2026.19 WGSL | `main_vk_inner` + wrapper negating `.y` of the position member; valid in naga and Tint; draws like the naga-flipped shader |
| Push to `uniform` group 3 binding 0 | `mesh_push.vert`, prepared `mesh_push.frag` | valid in naga; pipeline `[bgl0, empty, empty, bgl3]` accepted by the browser |
| Push block with `float[4]` | `mesh_push_array.vert` | `uniform`: naga `The array stride 4 is not a multiple of the required alignment 16` (Tint accepted it thanks to `uniform_buffer_standard_layout`); `storage`: valid in both |
| Keep as immediates + `requires immediate_address_space;` | `mesh_push.vert` | naga's WGSL parser rejects the directive (`unknown language extension`); Tint accepts both with and without it |
| `--directives` | builtins/subgroup probes, KSA modules | `diagnostic(off, derivative_uniformity);`, `enable subgroups;`, `@coherent` stripped; fixes 6+2 KSA modules and the two probes |

# Browser results (headless Edge 155, Apple Metal) not obtainable from naga alone

- **Limits**: `maxBindGroups 4`, `maxBindingsPerBindGroup 1000`, `maxImmediateSize 64`, `maxSampledTexturesPerShaderStage 48`, `maxSamplersPerShaderStage 16`, `maxStorageBuffersInVertexStage 10`, `maxUniformBufferBindingSize 65536`; WGSL language features include `immediate_address_space`, `uniform_buffer_standard_layout`, `subgroup_id`, `texture_and_sampler_let`, `readonly_and_readwrite_storage_textures`; device features `clip-distances`, `subgroups`, `shader-f16` present.[^browser-result]
- **Pipeline layouts**: `immediateSize: 64` OK, `80` rejected (`immediateSize (80) is larger than the maximum allowed (64)`). A real render pipeline with `{ubo@0, texture@1, sampler@501}` and another with push emulation at group 3 (sets 1 and 2 empty) were accepted.
- **Y flip / winding** (16 x 16 target, `cullMode: 'back'`): naga default and wrapper-flip: 36 red pixels with `frontFace: 'ccw'`, 0 with `'cw'`; no-flip: 0 with `'ccw'`, 36 with `'cw'`, image mirrored vertically.
- **std140 probes** (compute shaders reading a std140 UBO filled with value = slot index): hazard block `{float[4], vec2[2], vec3, float, mat3}` observed slots 1, 3, 6, 8, 11, 17 versus std140 expectation 4, 12, 20, 24, 27, 33; safe block `{vec4, mat4, vec3, float, mat3, vec4[2]}` observed 1, 10, 22, 23, 29, 43 = std140 expectation.
- **Tint verdicts for the 30 generated WGSL files** (24 OK; the 6 failures are exactly the expected ones: Slang 2025 portable-push, subgroup without `enable`, `binding_array`, `f64`, `u64`, non-uniform `fwidth`); the corresponding patched variants compile.

# Build pipeline prototype (strategy A)

`node src/build-shaders.mjs shaders out/web-shaders` (glslc vulkan1.1, spirv-val, prepare, layout lint, naga-wasm, patch, validate) built 7 shaders (`mesh.frag`, `mesh.vert`, `mesh_combined.frag`, `mesh_push.frag`, `mesh_push.vert`, `mesh_push_array.vert`, `tri.vert`); push mode was chosen automatically (`uniform` for the first four, `storage` for the `float[4]` block) and a lookup by `sha256(mesh_combined.frag.spv)` returned its WGSL and bindings `0:0 uniform, 0:1 texture_2d, 0:501 sampler`.[^manifest]

# Failures and wrong turns (kept for honesty)

1. `slangc` rejected `[[vk::location(0)]]` on a function (error 31002).
2. `spirv-opt --split-combined-image-sampler` alone produced modules naga rejects (same binding for image and sampler).
3. The first `spv-prepare.mjs` stripped nothing: SPIR-V decoration 24 is `NonWritable`, `NonReadable` is 25; fixed and verified.
4. Browser harness: first attempt used `--dump-dom` (async GPU work not finished) and later two template-literal escaping bugs; replaced by a local server receiving a POST.
5. A `std140` `mat2` probe failed in naga, so the layout probe was redone without it.
6. `grep` is aliased to ugrep in this shell; scripts call `/usr/bin/grep` or run under `bash`.

## Open questions

- UNVERIFIED: Firefox, Safari and non-Apple GPUs; only Edge 155 on Apple Metal was available.
- UNVERIFIED: browser timings over a network and on slower devices.
- UNVERIFIED: runtime behaviour of generated pipelines with real data beyond the three GPU tests (triangle, std140 probe, pipeline creation); the PoC itself will be the first end-to-end render of `mesh.vert`/`mesh.frag`.
- UNVERIFIED: naga/Tint behaviour on shader features not probed (geometry/mesh shaders, 16-bit storage, dual-source blending).

[^manifest]: build-pipeline prototype manifest
[^project]: exp-02 project README (layout, environment variables, reproduce)
[^runsh]: run.sh (every command used below)
[^shaders]: shader sources (GLSL, Slang, probes)
[^naga-options]: naga::front::spv::Options
[^swt]: spirv-webgpu-transform 0.1.7
[^logs]: out/logs (00-versions ... 13-ksa-*)
[^slang-release]: Slang v2026.19 release
[^naga-wasm-npm]: naga-wasm 30.2.0 (npm)
[^browser-naga-result]: naga-wasm inside Edge 155
[^browser-result]: headless Edge 155 result (Tint, render test, pipelines, std140 probe, limits, language features)
