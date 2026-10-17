---
type: Project Brief
title: BRUTAL-in-the-browser PoC — brief, constraints, success criteria
description: The user's goal, hard constraints, acceptance criteria and deliverable layout for the proof of concept that runs BRUTAL-based C# game code in a browser via WebAssembly and WebGPU.
tags: [brutal, webgpu, wasm, poc, requirements]
sources:
  - id: brief
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/REFERENCES.md
    title: User brief (REFERENCES.md, 2026-10-16)
  - id: brutal-readme
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/README.md
    title: BRUTAL Framework README
generated: { by: claude-fable/5.1, at: 2026-10-16T19:05:00Z }
status: draft
---

# Goal

Produce an initial proof of concept (PoC) that gets the open-source BRUTAL C# game framework running **in a web browser**, so that a developer writes C# against BRUTAL APIs (in particular `Brutal.Vulkan` / `Brutal.Vulkan.Abstractions`) and the result renders in-browser through WebGPU.[^brief] The PoC must be "a minimal amount of work" yet "a viable path for a full game", not a hack, and must put BRUTAL front and centre as the way the game is written.[^brief]

BRUTAL itself is a deliberately small, kernel-like framework: interop, memory, maths, strings, collections, job scheduling and bindings to native libraries (Vulkan, GLFW, Dear ImGui, FMOD, OpenAL, Jolt, ...). It has no renderer, no scene graph and no editor; today it targets Windows, Linux and macOS desktops only.[^brutal-readme]

# Hard constraints

| Constraint | Detail |
| --- | --- |
| Open-source BRUTAL only | Use the `Ahwoo.BRUTAL.*` NuGet packages (and/or the open-source tree for forks). Never link the Kitten Space Agency (KSA) game DLLs; the KSA decompiled sources and the Linux game build are **reference material only**.[^brief] |
| WebAssembly | The C# code must compile to WASM (the exact .NET-to-wasm pipeline was to be researched; see `/dotnet-wasm/`). |
| WebGPU | Rendering must target WebGPU through a translation layer so that BRUTAL's Vulkan bindings remain the API the game code uses.[^brief] |
| Shader compromise allowed | A BRUTAL-compatible shader language plus an automatic cross-compiler to a WebGPU-compatible format is preferred over hand-written WGSL.[^brief] |
| Vite 8+ | The web app must be bundled and served (dev server) with Vite 8 or later.[^brief] |
| Meaningful BRUTAL leverage | The end user must be writing C# against BRUTAL types and get an in-browser result; a design that merely reuses BRUTAL's maths library would not qualify.[^brief] |

# PoC acceptance criteria

1. Render a 3D model on screen.
2. Keyboard and mouse input proven by something simple (e.g. spin the model with the mouse, pan/zoom with arrow keys).
3. Play audio: `KSA_SFX_ROCKET_START_V2_01.ogg` (Ogg Vorbis, stereo, 48 kHz, 3.4 s) in the workspace root.
4. Bundle the web app and run the dev server with Vite 8+.

# Deliverables and layout

| Path | Content |
| --- | --- |
| `docs/research/` | This OKF v0.2 knowledge bundle: research notes, code analysis, experiment reports, decisions. |
| `plans/` | Implementation plan(s) with phases and tasks (one or more proposals). |
| `projects/` | Experiments and test projects used to validate ideas (`exp-NN-*`). |

# Working agreements (from the brief)

- Ask the user before installing tools, workloads or system packages; project-local npm/NuGet packages are fine.
- Subagent policy: opus-class agents for reasoning-heavy coding, sonnet-class for general/validation work, haiku-class for trivial tasks, always at high effort with precise instructions.
- Reference code lives outside this workspace and must not be modified (see [corpus map](/overview/corpus-map.md)).

# Related

- [Toolchain inventory](/overview/toolchain-inventory-2026-10-16.md)
- [Corpus map](/overview/corpus-map.md)
- [Architecture options and recommendation](/decisions/architecture-options.md)

[^brief]: User brief (REFERENCES.md, 2026-10-16)
[^brutal-readme]: BRUTAL Framework README
