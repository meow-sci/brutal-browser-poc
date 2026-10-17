---
type: Environment Inventory
title: Toolchain inventory on the development Mac (2026-10-16)
description: What is installed (and notably what is missing) on the macOS arm64 machine used for the PoC — .NET SDKs/workloads, Node, shader tools, Vulkan SDK, browsers, npm/NuGet package versions observed.
tags: [environment, toolchain, dotnet, wasm, vite, vulkan-sdk, browsers]
sources:
  - id: shell-probe
    resource: "scope: shell probes run on 2026-10-16 (dotnet --info, dotnet workload list, node -v, npm view, ls of SDK packs, otool, ffprobe)"
    title: Local shell probes, 2026-10-16
  - id: nuget-search
    resource: https://azuresearch-usnc.nuget.org/query?q=Microsoft.NET.Runtime.WebAssembly.Templates&prerelease=true
    title: NuGet search for WebAssembly template packages
generated: { by: claude-fable/5.1, at: 2026-10-16T19:05:00Z }
status: draft
stale_after: 2026-12-31
---

# Machine

- macOS 27.0.1 (Darwin 27.0.0), Apple Silicon (arm64).[^shell-probe]
- Shell: zsh. Note for scripts: unquoted `--include=*.cs` globs fail in zsh ("no matches found"); quote them.

# .NET

| Item | Value |
| --- | --- |
| SDKs | 10.0.100 (default), 9.0.307 — at `/usr/local/share/dotnet` |
| Workloads installed | `android` 36.1.2, `wasm-tools-net8` (manifest 10.0.101) |
| Workloads **not** installed | `wasm-tools` (the net10 one), `wasm-experimental*`, `wasm-tools-net9` |
| Packs present | `Microsoft.NET.Runtime.Emscripten.3.1.34.{Cache,Node,Python,Sdk}.osx-arm64` (for net8), `Microsoft.NET.Runtime.WebAssembly.Sdk`, `Microsoft.NETCore.App.Runtime.Mono.browser-wasm`, `Microsoft.NETCore.App.Runtime.AOT.osx-x64.Cross.browser-wasm` |
| Templates | `blazor`, `blazorwasm` present; `wasmbrowser` requires the NuGet template package `Microsoft.NET.Runtime.WebAssembly.Templates.net10` (10.0.13 on NuGet; `.net9` 9.0.21; legacy unsuffixed 8.0.32)[^nuget-search] |

Implication: plain `dotnet build` of a `browser-wasm` project works (runtime pack from NuGet), but AOT, native relinking (`WasmBuildNative`), `NativeFileReference` and Emscripten ports need the net10 `wasm-tools` workload, which must be installed by the user (see `/dotnet-wasm/native-linking-and-emscripten.md`).

# JavaScript / web

| Tool | Version |
| --- | --- |
| Node | v24.18.0 |
| npm | 12.0.2 |
| pnpm | 11.22.0 |
| bun | 1.4.2 |
| Vite (npm `latest`) | 8.3.4 (8.x line observed: 8.1.4 … 8.3.4; `previous` tag 6.4.4) |
| `naga-wasm` (npm) | 30.2.0 — "naga shader translator (wgpu) compiled to WebAssembly", repo github.com/kekkon-nexus/naga-wasm, modified 2026-09-30 |
| `@webgpu/types` | 0.1.74 |
| `@webgpu/glslang` | 0.0.15 (old GLSL→SPIR-V in wasm; historical) |
| npm `tint` | 0.2.1 — unrelated image-tinting package, **not** the Dawn Tint compiler |

# Native / shader tools

| Tool | Location | Version |
| --- | --- | --- |
| glslc | /usr/local/bin/glslc | shaderc v2025.3-10-gc7e73e8, spirv-tools v2025.4, glslang 11.1.0-1302 |
| glslangValidator | /usr/local/bin | (Vulkan SDK) |
| spirv-cross | /usr/local/bin | vulkan-sdk-1.4.328.0 |
| slangc | /usr/local/bin/slangc | 2025.17.2 |
| clang | /usr/bin/clang | Apple clang |
| cmake | /opt/homebrew/bin/cmake | present |
| cargo / rustc | ~/.cargo/bin | present |
| python3 | /usr/bin/python3 | present |
| ffprobe | /opt/homebrew/bin/ffprobe | present |
| **missing** | emcc/em++ (Emscripten), naga CLI, tint CLI, ninja, wasm-opt | — |

# Vulkan SDK (MoltenVK)

`/Users/asherwin/VulkanSDK/1.4.328.1/macOS/lib` contains the Khronos loader `libvulkan.1.4.328.dylib` (+ `libvulkan.1.dylib`, `libvulkan.dylib` symlinks), `libMoltenVK.dylib`, `MoltenVK.xcframework`, `MoltenVK_icd.json`, plus shaderc, glslang, spirv-cross and slang libraries. `setup-env.sh` at the SDK root sets the environment. The Ahwoo.BRUTAL.Vulkan package's `libVulkanEx.dylib` links `@rpath/libvulkan.1.dylib` with rpaths `/usr/local/lib` and `@loader_path` (see `/brutal/building-from-source.md` and experiment exp-04).

# Browsers available for automated testing

| Browser | Path | Notes |
| --- | --- | --- |
| Microsoft Edge 155.0.4283.45 | `/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge` | Chromium-based; WebGPU capable |
| Playwright Chromium build 1234 | `~/Library/Caches/ms-playwright/chromium-1234/chrome-mac-arm64/Google Chrome for Testing.app` | plus `chromium_headless_shell-1234` |
| Firefox | `/Applications/Firefox.app` | — |
| Safari | system | — |
| Google Chrome | **not installed** | — |

# Test asset

`KSA_SFX_ROCKET_START_V2_01.ogg` (88 KB): Ogg Vorbis, stereo, 48000 Hz, 3.41 s, ~206 kb/s, encoder libVorbis 1.3.3 (ffprobe).[^shell-probe]

[^shell-probe]: Local shell probes, 2026-10-16
[^nuget-search]: NuGet search for WebAssembly template packages
