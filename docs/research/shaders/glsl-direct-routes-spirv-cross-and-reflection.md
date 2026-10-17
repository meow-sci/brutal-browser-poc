---
type: Research Note
title: Direct GLSL-to-WGSL routes, SPIRV-Cross (no WGSL backend) and reflection options
description: Evaluates glslang-in-wasm, naga's GLSL front end and other GLSL-to-WGSL paths against the recommended glslc to SPIR-V to naga route, confirms SPIRV-Cross has no WGSL backend, and compares reflection sources (spirv-cross --reflect, spirv-reflect, Slang JSON, WGSL text) for generating bind group and vertex layouts.
tags: [shaders, glsl, glslang, naga, spirv-cross, reflection, wgsl, build-pipeline]
sources:
  - id: glslang-npm
    resource: https://www.npmjs.com/package/@webgpu/glslang
    title: "@webgpu/glslang 0.0.15 (npm view: modified 2026-03-18, unpacked 5,085,507 bytes, repo kainino0x/-webgpu-glslang)"
  - id: npm-search
    resource: https://www.npmjs.com/search?q=glsl%20wgsl
    title: npm registry search "glsl wgsl" and "spirv wgsl" run on 2026-10-16 (results listed in the text)
  - id: exp02-glsl
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/logs/07-naga-glsl-frontend.txt
    title: exp-02 step 07, naga GLSL front end results
  - id: exp02-reflect-json
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/reflect/mesh.frag.spirv-cross.json
    title: spirv-cross --reflect JSON for the PoC fragment shader
  - id: exp02-slang-log
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/logs/05-slang.txt
    title: exp-02 step 05 (includes slangc -reflection-json)
  - id: naga-wasm-readme
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/node_modules/naga-wasm/README.md
    title: naga-wasm README (GLSL parse options, reflection only for GLSL writer)
  - id: spirv-cross-help
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/projects/exp-02-shader-xcompile/out/logs/10-reflect.txt
    title: spirv-cross (vulkan-sdk-1.4.328.0) option listing and failed --wgsl invocation
generated: { by: claude-agent/sonnet-5.5, at: 2026-10-16T16:38:44Z }
status: draft
---

# Summary

There is **no maintained direct GLSL to WGSL compiler** worth adopting. The route that works, and that the rest of the pipeline assumes, is `glslc` (shaderc, the same compiler BRUTAL ships) to SPIR-V and then [naga](/shaders/naga-spirv-to-wgsl.md) to WGSL. SPIRV-Cross cannot help (no WGSL backend). Reflection for layouts should come from the final WGSL plus a build-time SPIR-V reflection pass.

# GLSL to WGSL: options evaluated

| Option | What it is | Result here | Verdict |
|---|---|---|---|
| `glslc` + naga | shaderc compiles GLSL 450 (Vulkan dialect) to SPIR-V; naga writes WGSL | works; see experiments | **recommended** |
| `@webgpu/glslang` | npm WebAssembly build of glslang, version 0.0.15, last modified 2026-03-18, 5.1 MB unpacked, license "glslang/LICENSE.txt"; produces **SPIR-V**, not WGSL[^glslang-npm] | not run (no WGSL output) | only useful for runtime GLSL to SPIR-V in a browser, which then still needs naga-wasm; alive but slow-moving |
| naga GLSL front end (`parseGlsl(source, { stage, defines })`, also in naga-wasm) | direct GLSL to naga IR to WGSL | PoC `mesh.vert`, `mesh.frag`, `mesh_push.vert` translate (struct and variable names preserved, `var<immediate>` for push constants); `layout(set=0,binding=1) uniform sampler2D tex;` fails with `Not implemented: variable qualifier`; **no Y flip** (the front end assumes GL-style clip space)[^exp02-glsl] | not for Vulkan GLSL; also no help for SPIR-V arriving at runtime |
| Tint | no GLSL front end | n/a | no |
| Slang | can ingest GLSL-ish code poorly; native source is Slang/HLSL | not tested | see [Slang](/shaders/slang-wgsl-target.md) |
| Other npm packages | search of "glsl wgsl" and "spirv wgsl" found loaders/minifiers (`vite-plugin-glsl`, `rsbuild-plugin-glsl`), WGSL tooling (`wgsl_reflect`, `wesl`, `typegpu`), `naga-wasi-cli`, but no GLSL-to-WGSL transpiler[^npm-search] | | none |

Runtime GLSL in the browser, if ever needed, would be `@webgpu/glslang` (SPIR-V) then naga-wasm (WGSL): two wasm modules, about 5.1 MB + 2.05 MB uncompressed. That is the "ship compilers" variant of [strategy B](/shaders/build-time-vs-runtime-cross-compilation.md); it also needs the include resolver BRUTAL provides natively.

# SPIRV-Cross

- Backends in the installed build (Vulkan SDK 1.4.328): GLSL (default), `--msl`, `--hlsl`, `--reflect` (JSON) and a deprecated `--cpp`; **there is no `--wgsl`**: the invocation `spirv-cross mesh.frag.spv --wgsl` prints usage and exits with status 1.[^spirv-cross-help]
- Consequence: SPIRV-Cross cannot translate to WGSL; its roles are reflection and, for diagnosing, SPIR-V to GLSL/MSL. Its `--flip-vert-y` ("equivalent to using negative viewport height") and `--fixup-clipspace` options exist only for the GLSL/HLSL/MSL backends.

# Reflection sources

| Source | Command | What you get | Use |
|---|---|---|---|
| SPIRV-Cross JSON | `spirv-cross shader.spv --reflect` | top-level keys observed: `entryPoints`, `types` (members with `offset`, `matrix_stride`), `inputs`/`outputs` (type, `location`), `ubos` (`block_size`, `set`, `binding`), `separate_images`, `separate_samplers` (each with `set`, `binding`), `push_constants` (type id); the PoC UBO reports `block_size: 208`[^exp02-reflect-json] | build-time: vertex layouts, UBO member offsets, push block size (used for the KSA push-size survey) |
| spirv-reflect (SDK) | `spirv-reflect shader.spv` | summary and optional YAML: descriptor sets/bindings, types, entry points, push constants | alternative; text/YAML |
| Slang | `slangc ... -reflection-json out.json` | parameters with `binding: { kind: "descriptorTableSlot", index }`, struct fields with `offset`/`size`, entry point varying counts[^exp02-slang-log] | when the source is Slang |
| naga-wasm | none for WGSL output; `reflect: true` exists only for the GLSL writer and returns texture/sampler pairs and uniform blocks for WebGL hosts[^naga-wasm-readme] | | not applicable |
| The WGSL text | `reflectBindings` in `src/patch-wgsl.mjs` (regex over `@group/@binding var<...> name : type`), or the npm library `wgsl_reflect` (WGSL parser + reflection, 1.6.0, 2026-09-05; not evaluated) | group, binding, address space, resource type (`texture_2d<f32>`, `sampler_comparison`, `texture_storage_2d<rgba8unorm,write>`, `var<storage, read>`) | **the authoritative source for `GPUBindGroupLayoutEntry`** because sample type, storage access and view dimension are only fully known in WGSL types |

Recommended build output: one `manifest.json` entry per SPIR-V hash assembled from (a) the WGSL declarations (bindings, resource kinds, access), (b) `spirv-cross --reflect` for vertex inputs and push block size, (c) the patch report (push mode, Y-flip origin, directives). Shape in [conventions](/shaders/wgsl-conventions-for-vulkan-layer.md).

## Open questions

- UNVERIFIED: whether `@webgpu/glslang` 0.0.15 supports `#include`, `GL_EXT_*` extensions KSA uses, and SPIR-V 1.3 targets (not run).
- UNVERIFIED: whether the `wgsl_reflect` library handles naga-emitted WGSL (`var<immediate>`, `@id override`); a local regex was used instead.
- UNVERIFIED: that spirv-reflect exposes the same push-constant sizes as the SPIRV-Cross-based script used for the KSA survey.

[^exp02-reflect-json]: spirv-cross --reflect JSON for the PoC fragment shader
[^glslang-npm]: @webgpu/glslang 0.0.15 (npm view: modified 2026-03-18, unpacked 5,085,507 bytes, repo kainino0x/-webgpu-glslang)
[^npm-search]: npm registry search "glsl wgsl" and "spirv wgsl" run on 2026-10-16 (results listed in the text)
[^exp02-glsl]: exp-02 step 07, naga GLSL front end results
[^spirv-cross-help]: spirv-cross (vulkan-sdk-1.4.328.0) option listing and failed --wgsl invocation
[^exp02-slang-log]: exp-02 step 05 (includes slangc -reflection-json)
[^naga-wasm-readme]: naga-wasm README (GLSL parse options, reflection only for GLSL writer)
