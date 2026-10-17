# brutal-browser

Research, plans and experiments for running the open-source [BRUTAL](https://github.com/ahwootech/BRUTAL) C# game framework in a web browser (WebAssembly + WebGPU).

The proof of concept runs BRUTAL's own Vulkan API in the browser through a managed Vulkan-on-WebGPU ICD: the sample game `samples/HelloMesh` compiles the same game sources for the desktop (MoltenVK/Vulkan, GLFW, OpenAL) and for the browser (.NET `browser-wasm`, WebGPU, Web Audio). Building a game on it (where the code goes, which BRUTAL libraries and Vulkan features exist in the browser, performance rules, debugging): [`docs/game-guide.md`](docs/game-guide.md). Component contracts: [`docs/architecture.md`](docs/architecture.md).

Live demo (WebGPU browser required): https://meow.science.fail/brutal-browser-poc/ , deployed to GitHub Pages by `.github/workflows/pages.yml` on every push to `main` ([Deployment](samples/HelloMesh.Web/README.md#deployment-github-pages)).

## Quick start

Prerequisites (nothing global beyond these): .NET SDK 10 (`global.json`; the `wasm-tools` workload is optional, it only enables the native relink), Node 24, `glslc` and `spirv-val` on the PATH (LunarG Vulkan SDK; on macOS it also provides the Vulkan loader and MoltenVK), `python3`, the BRUTAL source tree at commit `a25cdf8` next to this repository (`../BRUTAL`, or `-p:BrutalSourceRoot=...` / `BRUTAL_SRC`), and Chrome/Edge 113+ for WebGPU. npm packages are installed per folder (`tools/` is installed by the first browser build).

```sh
dotnet build BrutalBrowser.slnx -c Release                  # everything: src/, samples/, tests/ (incl. the WebAssembly apps)

# desktop
dotnet run --project samples/HelloMesh/HelloMesh.Desktop -c Release          # a window with the sunglasses; --frames 120 --validation for a smoke run

# browser
cd samples/HelloMesh.Web
npm install
npm run build:dotnet && npm run dev                         # http://localhost:5173, click "Click to start"
npm run test:e2e                                            # Playwright end-to-end (headless Chromium); test:e2e:chrome for Google Chrome
```

Unit tests: `dotnet test BrutalBrowser.slnx` (the three xunit projects; `dotnet test` takes one project or solution, so run single projects as `dotnet test tests/Brutal.Vulkan.WebGpu.Tests`). Shader pipeline: `cd tools && npm test`. Playwright uses its cached Chromium build (1248 for playwright-core 1.64.1) or Google Chrome; it never downloads browsers here.

| Folder | Content |
| --- | --- |
| [`src/`](src/) | `Brutal.Vulkan.Browser` + `Brutal.Vulkan.Abstractions.Browser` (bindings from BRUTAL source, managed function pointers), `Brutal.Vulkan.WebGpu` (the web ICD), `Brutal.Browser` (host: canvas, loop, input, audio), `Brutal.Glfw.Browser` (GLFW drop-in). Each has a README. |
| [`samples/`](samples/) | `HelloMesh` (game + desktop and browser hosts; assets and their licences in [`samples/HelloMesh/Assets/`](samples/HelloMesh/Assets/LICENSES.md), including the freesound.org explosion sound `874222__samrad20__generic-explosion.ogg`, the test sound of the PoC) and `HelloMesh.Web` (Vite page, dev server, Playwright e2e). |
| [`tests/`](tests/) | xunit tests of the bindings, the ICD and the host; browser smoke apps of the ICD and the host. |
| [`tools/`](tools/README.md) | Shader pipeline (glslc -> SPIR-V -> naga -> WGSL + manifest, MSBuild targets) and generators. |
| `patches/brutal/`, `profiles/` | Source patches/transform for the BRUTAL bindings; the `VP_BRUTAL_web` Vulkan profile for desktop validation. |
| [`docs/research/`](docs/research/index.md) | OKF v0.2 knowledge bundle: overview, decisions, experiment reports, and research notes on BRUTAL internals, .NET 10 browser-wasm, Vulkan→WebGPU, shaders, input/audio and Vite packaging. Start at `index.md`. |
| [`plans/`](plans/README.md) | Implementation proposals. Proposal A (managed Vulkan-on-WebGPU ICD) is recommended; B/C are alternatives. |
| [`projects/`](projects/) | Experiments `exp-01` … `exp-06` with their own READMEs, raw results and headless-browser test drivers. They are evidence, not the product. |
| `REFERENCES.md` | The original brief and the locations of the reference material (BRUTAL source, KSA decompiled assemblies, KSA Linux build, ImCode, brutal-imgui-standalone). |

Conventions: the BRUTAL tree and the KSA material are read-only reference; only the `Ahwoo.BRUTAL.*` NuGet packages (and source builds of the open-source tree) may be linked. Experiments use a project-local `nuget.config` (nuget.org only) and project-local npm packages; no global tools were installed.
