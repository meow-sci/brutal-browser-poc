---
type: Technology Assessment
title: Tint (Dawn) SPIR-V reader and WGSL writer as an alternative translator, and Tint as the validator
description: Evaluates Dawn's Tint for SPIR-V to WGSL in 2025 to 2026 (reader rewritten onto Tint IR, still in the tint CLI when built), its documented limits for combined samplers and push constants, the absence of a published wasm build, and how this project used the browser's own Tint as the conformance check.
tags: [shaders, tint, dawn, spirv, wgsl, validation, wasm, immediates]
sources:
  - id: chrome141
    resource: https://developer.chrome.com/blog/new-in-webgpu-141
    title: "What's new in WebGPU (Chrome 141), 2025-09-24: SPIR-V frontend converted from AST to Tint IR, float16 support"
  - id: tint-main
    resource: https://dawn.googlesource.com/dawn/+/refs/heads/main/src/tint/cmd/tint/main.cc
    title: Dawn src/tint/cmd/tint/main.cc (tint CLI options, input/output formats, SPIR-V reader flags, immediates)
  - id: tint-reader-dir
    resource: https://github.com/google/dawn/tree/main/src/tint/lang/spirv/reader
    title: Dawn src/tint/lang/spirv/reader (directories common, lower, parser; no ast_parser)
  - id: tint-reader-parser-dir
    resource: https://github.com/google/dawn/tree/main/src/tint/lang/spirv/reader/parser
    title: Dawn SPIR-V reader parser directory listing (image_sampler_test.cc, var_test.cc, ...)
  - id: tint-combimg
    resource: https://dawn.googlesource.com/tint/+/4651a6ee8b53c6a089048fd57e33eb37fa20f24a%5E%21
    title: "tint commit: spirv-reader: explicitly reject combined-image-sampler (tint:442)"
  - id: tint-reader-readme
    resource: https://dawn.googlesource.com/dawn/+/0a7364815ce9/src/tint/reader/spirv/README.md
    title: Dawn SPIR-V Reader README (older snapshot; Vulkan 1.1 validation requirement)
  - id: tint-io
    resource: https://dawn.googlesource.com/dawn/+/refs/heads/main/docs/tint/spirv-input-output-variables.md
    title: Tint doc, SPIR-V input/output variables (pseudo-in/out private variables, entry-point wrapper, clip/cull distance)
  - id: tint-push-ext
    resource: https://dawn.googlesource.com/dawn/+/1884c964d7e64ff97f69c8e1240d1073dace1ad7/docs/tint/extensions/chromium_experimental_push_constant.md
    title: Tint extension doc, chromium_experimental_push_constant (snapshot; experimental, internal transforms only)
  - id: tint-dynbind
    resource: https://dawn.googlesource.com/dawn/+/cb66e36c25952abbb95bfbeb00e85e4042affe6c/docs/tint/extensions/chromium_experimental_dynamic_binding.md
    title: Tint extension doc, chromium_experimental_dynamic_binding (runtime-sized binding_array; no WebGPU spec yet)
  - id: chrome-immediates
    resource: https://developer.chrome.com/blog/new-in-webgpu-149-150
    title: "What's new in WebGPU (Chrome 149 and 150): var<immediate>, setImmediates()"
  - id: slang-notices
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Slang/LICENSES/BRUTAL-SLANG-THIRD-PARTY-NOTICES.txt
    title: Brutal.Slang notices (slang-tint, the Tint WGSL compiler, ships on win-x64 only)
  - id: exp02-browser
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/browser-check.result.json
    title: exp-02 headless Edge 155 results (Tint compile of generated WGSL)
  - id: exp02-ksa-tint
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/ksa-tint-summary.json
    title: exp-02 Tint compile results for 187 KSA-derived WGSL modules (names and messages only)
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:38:44Z }
status: draft
---

# Verdict

Tint's SPIR-V reader is alive and has been modernised, but it is **not a drop-in alternative to naga for this project**: no published wasm build exists, it must be built from the Dawn tree, its public documentation says it rejects combined image samplers, and its push-constant story is an internal experimental extension that was superseded by WGSL immediates. Use Tint for what it is uniquely good at here: **the authoritative WGSL validator**, by calling `GPUDevice.createShaderModule` (+ `getCompilationInfo`) in a Chromium-family browser. The pipeline in [build-time vs runtime](/shaders/build-time-vs-runtime-cross-compilation.md) does exactly that as an optional CI step.

# Status of the SPIR-V reader (2025 to 2026)

- Chrome 141 (2025-09-24): "the SPIR-V frontend (used by applications to convert SPIR-V to WGSL) was converted from generating an AST representation to generating directly to IR", which "also introduces long-awaited features like float 16 support".[^chrome141]
- The Dawn tree now has `src/tint/lang/spirv/reader/{common, lower, parser}` and no `ast_parser` directory.[^tint-reader-dir] The parser directory carries per-feature tests (atomics, barriers, builtins, images/samplers, structs, vars...).[^tint-reader-parser-dir]
- The `tint` CLI still accepts SPIR-V: `--input-format spirv|spvasm|wgsl` (inferred from `.spv`/`.spvasm`/`.wgsl`), `--format wgsl|ir|spirv|spvasm|msl|hlsl|hlsl-fxc|glsl|none`; the `spirv`/`spvasm` inputs exist "only when the SPIR-V reader is built".[^tint-main]
- Relevant flags: `--entry-point`, `--overrides ID=VALUE,...`, `--validate`, `--parse-only`, `--dump-ir`, `--rename-all`, WGSL-writer `--allow-non-uniform-derivatives` (inserts a module-scope directive that suppresses uniformity violations), SPIR-V-reader `--sampler-mapping`, `--treat-samplers-as-filtering`, `--treat-textures-as-unfilterable`.[^tint-main]
- Input requirements: the CLI validates against Vulkan 1.1 / SPIR-V 1.3 (1.4 with Vulkan 1.1 + 1.4 variant; "SPIR-V 1.5 validation not yet supported" is a code comment).[^tint-main] An older README snapshot states the same expectation (input must pass Vulkan 1.1 validation, SPIR-V 1.4+ unsupported); that text predates the IR rewrite and may be stale.[^tint-reader-readme] So like naga, compile with SPIR-V 1.3 (`--target-env=vulkan1.1`).

# Documented limits relevant to Vulkan input

| Topic | Tint behaviour | Source |
|---|---|---|
| Combined image samplers | A Tint commit made the reader "explicitly reject combined-image-sampler" with a clear message because WGSL has no such type (tracked as tint:442). The same pre-pass as for naga is required. | [^tint-combimg] |
| Arrays of textures/samplers | Marked invalid input in the reader tests (WGSL has none); Dawn has an experimental `chromium_experimental_dynamic_binding` extension with runtime-sized `binding_array` and states that no WebGPU spec exists yet. | [^tint-combimg][^tint-dynbind] |
| Push constants | `chromium_experimental_push_constant` is "highly experimental and only meant to be used in internal transforms"; superseded for web content by the standard `immediate_address_space` (`var<immediate>`, `setImmediates`, Chrome 149 and 150). Tint's own SPIR-V writer calls the feature "immediate data". | [^tint-push-ext][^chrome-immediates][^tint-main] |
| Entry-point I/O | SPIR-V `Input`/`Output` globals become `private` pseudo variables copied in/out by a generated wrapper entry point; `ClipDistance`/`CullDistance` are copied as whole arrays. The document does not mention flipping `gl_Position.y` or `gl_PointSize`. | [^tint-io] |
| Y flip | No flip option found in the CLI flags or the I/O document: UNVERIFIED that Tint's reader leaves `gl_Position.y` untouched, but nothing suggests it flips (contrast naga, which flips by default). A wrapper patch (`patch-wgsl.mjs --y-flip`) would be required. | [^tint-main][^tint-io] |

# Packaging and build complexity

- **No published wasm/npm build was found.** npm has no `tint-wasm` or `@webgpu/tint` package (404 from `npm view`), and web searches found no Emscripten build of Tint; a Chrome 141 post mentions Tint only as part of the browser. UNVERIFIED beyond those searches.
- Building the `tint` tool means a Dawn checkout (depot_tools / GN, or the CMake files present in the tree) with the SPIR-V reader enabled (it needs SPIRV-Tools/SPIRV-Headers). No `tint` binary was available on the machine used here, so no local Tint CLI measurements exist.
- Slang already embeds Tint: `slang-tint` is a prebuilt shared library obtained from upstream GitHub releases, shipped in Brutal.Slang on **win-x64 only**.[^slang-notices] That is a possible way to get a Tint-validated WGSL on a Windows build machine ([Slang](/shaders/slang-wgsl-target.md)).
- Browsers do not accept SPIR-V; their Tint is reachable only through WGSL.

# Tint as the validator: what the browser's Tint said

The generated WGSL was compiled by Tint inside headless Edge 155 (Chromium 155, Apple Metal) with `createShaderModule` + `getCompilationInfo`:[^exp02-browser]

- naga output for the PoC pair, split-sampler variant, Y-flip wrapper, and Slang 2026.19 output: **all OK**.
- `var<immediate>` as emitted by naga (no `requires` line) **compiles**; adding `requires immediate_address_space;` also compiles. The browser lists `immediate_address_space` in `navigator.gpu.wgslLanguageFeatures` (Chrome says the `requires` directive is how a shader opts in, but the compile did not demand it).
- A `var<uniform>` struct containing `array<f32, 4>` compiles in this browser because it also lists `uniform_buffer_standard_layout`; naga's own validator rejects the same text (stride 4 is not a multiple of 16). Other browsers: UNVERIFIED.
- Real failures Tint reports for naga output: `subgroupAdd` without `enable subgroups;`, `enable wgpu_binding_array;` (expected extension), `f64`/`u64` types, non-uniform `fwidth`, and `workgroupBarrier` under divergent control flow.
- Over the KSA corpus (187 WGSL modules naga produced from Vulkan 1.1 SPIR-V after pre-pass): Tint OK **156 raw, 164 after** adding `diagnostic(off, derivative_uniformity);`, `enable subgroups;` and stripping `@coherent`. The remaining 23: 12 binding arrays, 5 barrier-uniformity, 4 immediate blocks containing arrays (`arrays cannot be used in the <immediate> address space`; use a storage-buffer fallback), 2 constant-range errors.[^exp02-ksa-tint]

# Comparison with naga for this project

| Criterion | naga | Tint |
|---|---|---|
| Ready wasm for a runtime option | yes (`naga-wasm`, 2.05 MB) | none published |
| CLI | `cargo install naga-cli` (built in minutes) | build Dawn |
| Combined image sampler | fails, fixable with pre-pass | rejected by design |
| Y flip | default on (SPIR-V front end) | no option found |
| Push constants | emits `var<immediate>` | no standard path from SPIR-V push constants documented |
| WGSL conformance for browsers | mostly; emits native-only constructs (`wgpu_binding_array`, `@coherent`, `f64`, missing `enable subgroups`) | exact (it is the browser compiler) |

Decision: **naga translates, Tint validates.** See [naga](/shaders/naga-spirv-to-wgsl.md).

## Open questions

- UNVERIFIED: whether current Tint's SPIR-V reader accepts SPIR-V 1.4 to 1.6 (the main.cc comment lists 1.3 and 1.4 only; the IR rewrite may have relaxed it).
- UNVERIFIED: how Tint's reader treats `gl_Position.y`, `OpCopyLogical`, `OpSpecConstantOp`, `DrawParameters` and `DemoteToHelperInvocation`; it was not run. A Dawn build would answer this and would let the project compare Tint against naga on the KSA corpus.
- UNVERIFIED: whether a community Emscripten build of Tint exists outside npm.
- Does the `requires immediate_address_space;` line become mandatory in some browser (Firefox, Safari) even though Chromium 155 compiled the module without it?

[^chrome141]: What's new in WebGPU (Chrome 141), 2025-09-24: SPIR-V frontend converted from AST to Tint IR, float16 support
[^tint-main]: Dawn src/tint/cmd/tint/main.cc (tint CLI options, input/output formats, SPIR-V reader flags, immediates)
[^tint-reader-dir]: Dawn src/tint/lang/spirv/reader (directories common, lower, parser; no ast_parser)
[^tint-reader-parser-dir]: Dawn SPIR-V reader parser directory listing (image_sampler_test.cc, var_test.cc, ...)
[^tint-combimg]: tint commit: spirv-reader: explicitly reject combined-image-sampler (tint:442)
[^tint-reader-readme]: Dawn SPIR-V Reader README (older snapshot; Vulkan 1.1 validation requirement)
[^tint-io]: Tint doc, SPIR-V input/output variables (pseudo-in/out private variables, entry-point wrapper, clip/cull distance)
[^tint-push-ext]: Tint extension doc, chromium_experimental_push_constant (snapshot; experimental, internal transforms only)
[^tint-dynbind]: Tint extension doc, chromium_experimental_dynamic_binding (runtime-sized binding_array; no WebGPU spec yet)
[^chrome-immediates]: What's new in WebGPU (Chrome 149 and 150): var<immediate>, setImmediates()
[^slang-notices]: Brutal.Slang notices (slang-tint, the Tint WGSL compiler, ships on win-x64 only)
[^exp02-browser]: exp-02 headless Edge 155 results (Tint compile of generated WGSL)
[^exp02-ksa-tint]: exp-02 Tint compile results for 187 KSA-derived WGSL modules (names and messages only)
