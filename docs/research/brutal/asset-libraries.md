---
type: Code Analysis
title: BRUTAL asset libraries (Gltf, Render.Mesh, Texture/Ktx/Gli/Stb, MeshOptimizer) — browser implications and the minimal GLB path
description: Which BRUTAL asset libraries are managed vs native-backed, how Brutal.Gltf parses .glb/.gltf (System.Text.Json, file-path based), the exact API to pull positions/normals/UVs/indices for one primitive without touching native texture decoders, and fallbacks.
tags: [brutal, gltf, glb, mesh, texture, ktx, stb, meshoptimizer, assets, browser]
sources:
  - id: gltf-loader
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Gltf/GltfApi/GltfLoader.cs
    title: Brutal.Gltf GltfLoader / IGltfLoader / GltfLoaderExtensions.GetAccessorBufferSpan
  - id: gltf-utility
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Gltf/GltfApi/GltfUtility.cs
    title: GltfUtility (LoadModel(path|Stream), GLB header/chunk parsing, JSON options, LoadBinaryBuffer)
  - id: gltf-accessor
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Gltf/GltfApi/Accessor.cs
    title: Accessor.ElementSize()
  - id: gltf-schema
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Gltf/GltfApi/GeneratedSchema/
    title: Generated glTF 2.0 schema classes (Gltf, Mesh, MeshPrimitive, Accessor, BufferView, ...)
  - id: gltf-json
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Gltf/GltfApi/JsonHelpers.cs
    title: System.Text.Json converters for float3/float4/floatQuat/float4x4
  - id: gltf-transform
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Gltf/GltfApi/GltfLoader.Transform.cs
    title: Axis flip/swap/scale helpers on GltfLoader
  - id: texture-loader
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Texture/TextureLoader.cs
    title: Brutal.Texture TextureLoader (dispatch to Stb/Ktx/Gli)
  - id: stb
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Stb/Stb.cs
    title: Brutal.Stb (LibraryImport "stb")
  - id: mesh
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Render.Mesh/Mesh.cs
    title: Brutal.Render.Mesh Mesh<TKey>, IVertexBufferMap, IIndexBufferStorage
  - id: procgen
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Render.Mesh/ProcGenMesh.Primitives.cs
    title: ProcGenMeshLibrary primitives (plane, cube, sphere, torus, disk, cone)
  - id: procgen-core
    resource: /Users/asherwin/repos/meow-sci/BRUTAL/Brutal.Render.Mesh/ProcGenMesh.cs
    title: ProcGenMesh struct and helpers
  - id: asminfo
    resource: /private/tmp/claude-501/-Users-asherwin-repos-meow-sci-brutal-browser/f33c3ef4-2b97-4ca2-967a-de9382fcf833/scratchpad/r4/asminfo-full.txt
    title: Metadata dump (references/ModuleRefs of Gltf, Texture, Render.* DLLs)
  - id: inventory
    resource: /Users/asherwin/repos/meow-sci/brutal-browser/docs/research/brutal/nuget-package-inventory.md
    title: NuGet package inventory
generated: { by: claude-agent/opus-5.5, at: 2026-10-16T16:16:59Z }
status: draft
---

# Summary table

The native-backed packages below ship binaries only for `win-x64`, `linux-x64` and `osx-arm64`.[^inventory]

| Library | Managed / native | NuGet deps | Browser rating | Notes |
|---|---|---|---|---|
| `Brutal.Gltf` (2026.10.2) | **Managed**. JSON goes through **System.Text.Json reflection-based** `JsonSerializer.Deserialize<Gltf>`; there is no `JsonSerializerContext`[^gltf-utility] | Core, Numerics, **Texture** | **YELLOW** | Parsing is pure managed. Loading uses `File.*` (works on the wasm virtual FS). Texture decode goes through native Stb/Ktx/Gli and is avoidable. Trimming must keep the schema types |
| `Brutal.Render.Mesh` (2026.10.1) | Managed (`Mesh<TKey>`, `NativeStrideList` storage, `ProcGenMeshLibrary`)[^mesh][^procgen] | Render.Common (an **empty** placeholder DLL), Collections, Memory, Numerics[^asminfo] | **GREEN** (pending the Collections/Memory audit) | Procedural cube, sphere, plane, torus, disk and cone are useful as a fallback model |
| `Brutal.Texture` (2026.10.2) | Managed dispatcher with no ModuleRefs. `TextureLoader` static ctor only builds lookup dictionaries[^texture-loader][^asminfo] | Gli, Ktx, Stb, Interop, Core, Numerics | **YELLOW** (needs natives only when decoding) | `LoadFromFile`/`LoadFromMemory` dispatch by extension or `FormatType` to `StbTextureLoader`, `KtxTextureLoader` or `GliTextureLoader` |
| `Brutal.Texture.Abstractions` | Managed | Texture | YELLOW | — |
| `Brutal.Stb` | Native `stb`; 17 imports; no custom loader (default probing)[^stb] | Core, Numerics, Strings | **RED** | stb_image is single-file C and trivially compiled with Emscripten. Alternatively, decode in the browser with `createImageBitmap` |
| `Brutal.Ktx` | Native `Ktx` (KTX-Software); 89 imports | Core, Numerics | **RED** | KTX-Software has upstream wasm builds (for JS). Linking into .NET wasm is UNVERIFIED |
| `Brutal.Gli` | Native `gli` (C wrapper over the header-only C++ GLI); 22 imports | Core, Numerics | **RED** | Recompile with Emscripten, or drop (DDS/KTX1) |
| `Brutal.MeshOptimizer` | Native `meshoptimizer`; 82 imports | Core, Interop, Numerics | **RED** | Pure C++ with no deps, so an easy Emscripten build. Only needed for mesh processing, or for decoding `EXT_meshopt_compression` assets |

# How Brutal.Gltf loads a file

* `GltfUtility.LoadModel(string filePath)` calls `File.OpenRead` and then `LoadModel(Stream)`. It reads the 4-byte magic, rewinds, and either reads the text or calls `ParseBinary`. `ParseBinary` validates the GLB header: magic, version 2, and a declared length equal to the stream length. It then walks the chunks until the `JSON` chunk, UTF-8-decodes it, and calls `JsonSerializer.Deserialize<Gltf>(json, JsonOptions)`. `JsonOptions` uses `WhenWritingNull` plus four `float3`/`float4`/`floatQuat`/`float4x4` converters (`GltfUtility.cs:41–174`).[^gltf-utility][^gltf-json]
* **The GLB `BIN` chunk is not kept.** `LoadBinaryBuffer(bufferIndex, baseFilePath)` on a buffer without a URI **re-opens the file from disk** (`File.OpenRead(baseFilePath)`) and reads the `BIN` chunk with the private `ReadBinaryHeader`/`ReadBinaryChunk` (`GltfUtility.cs:200–222`). The overload `LoadBinaryBuffer(bufferIndex, Func<string, byte[]> resolver)` calls `resolver(buffer.Uri)` with a **null** URI for GLB, so the caller must supply the BIN chunk bytes itself (`:193–198, :259–271`). Base64 `data:` URIs are decoded inline.[^gltf-utility]
* `GltfLoader` (`GltfLoader.cs:48–286`) has only a **`GltfLoader(string filePath)`** constructor. It caches buffers (`GetOrLoadBuffer`, `LoadedBuffers`) and images (`GetOrLoadImage`, which calls into `Brutal.Texture`), and offers `ExternalResolveBuffer`/`ExternalResolveImage` hooks. Its `ITexture?[] LoadedImages` field means the `Brutal.Texture` **assembly** must be present for the type to load. Native decoders are only touched by `GetOrLoadImage`/`Dispose` with loaded images.[^gltf-loader]
* Accessor reading is raw: `loader.GetAccessorBufferSpan(accessorIndex)` returns `bufferViewSpan.Slice(accessor.ByteOffset, accessor.Count * stride)` with `stride = bufferView.ByteStride ?? accessor.ElementSize()`. **Sparse accessors throw** ("Sparse accessors not supported"). There are no typed or normalized readers. `Accessor.ElementSize()` handles every component type and rejects padded matrix layouts.[^gltf-loader][^gltf-accessor]
* Extras: `GltfLoaderExtensions.FlipHandedness/FlipZAxis/FlipXAxis/FlipYAxis/Swap*Axes/Scale` rewrite the loaded buffers in place.[^gltf-transform] `SaveAsGlb`, `SaveModel`, `Pack` and `Unpack` write files.

# Minimal path: one primitive's positions/normals/UVs/indices, no native libraries

```csharp
using System.Runtime.InteropServices;
using Brutal.GltfApi;
using Brutal.Numerics;

// Browser: bytes come from fetch(); write them to the wasm virtual file system so the
// path-based GltfLoader (and its BIN-chunk re-read) works unchanged.
File.WriteAllBytes("/tmp/model.glb", glbBytes);
using var loader = new GltfLoader("/tmp/model.glb");    // parses JSON only
var gltf  = loader.GltfJson;
var prim  = gltf.Meshes[0].Primitives[0];               // MeshPrimitive: Dictionary<string,int> Attributes, int? Indices, ModeEnum Mode

ReadOnlySpan<byte> Raw(int accessor) => loader.GetAccessorBufferSpan(accessor); // honours ByteStride

int posAcc = prim.Attributes["POSITION"];               // Accessor: ComponentType Float, Type Vec3
var posView = gltf.BufferViews[gltf.Accessors[posAcc].BufferView!.Value];
// Fast path only when tightly packed (ByteStride null or 12); otherwise de-interleave with the stride.
ReadOnlySpan<float3> positions = MemoryMarshal.Cast<byte, float3>(Raw(posAcc));

prim.Attributes.TryGetValue("NORMAL", out var nrmAcc);      // Vec3 Float
prim.Attributes.TryGetValue("TEXCOORD_0", out var uvAcc);   // Vec2 Float (or normalized UnsignedByte/UnsignedShort)

var idxAcc = gltf.Accessors[prim.Indices!.Value];       // UnsignedByte/UnsignedShort/UnsignedInt
ReadOnlySpan<byte> idx = Raw(prim.Indices.Value);
// idxAcc.ComponentType == Accessor.ComponentTypeEnum.UnsignedShort  => MemoryMarshal.Cast<byte, ushort>(idx)
// ... UnsignedInt => Cast<byte, uint>, UnsignedByte => widen to ushort (Vulkan without VK_EXT_index_type_uint8)
```

Property names come from the generated schema: `Gltf.Meshes/Accessors/BufferViews/Buffers/Nodes/Scenes/Materials/Images`; `Mesh.Primitives`; `Accessor.BufferView/ByteOffset/ComponentType/Normalized/Count/Type/Min/Max`; `BufferView.Buffer/ByteOffset/ByteLength/ByteStride/Target`.[^gltf-schema]

What this needs at runtime: `Brutal.Gltf.dll`, `Brutal.Texture.dll` (type load only) plus its managed dependencies (`Brutal.Stb`, `Brutal.Ktx`, `Brutal.Gli` and `Brutal.Interop` assemblies, because `Brutal.Texture` references them), `System.Text.Json` with **reflection-based serialization enabled**, and the wasm VFS. **No native code is executed.** Node transforms are not applied by the loader; walk `gltf.Nodes[*].Matrix/Translation/Rotation/Scale` yourself.

## Browser caveats for Brutal.Gltf

* **Trimming and reflection:** the schema classes are bound by reflection, and `Dictionary<string, object> Extensions` deserializes to `JsonElement`. In a trimmed wasm publish, keep `Brutal.Gltf` untrimmed (`<TrimmerRootAssembly Include="Brutal.Gltf" />`) and make sure `JsonSerializerIsReflectionEnabledByDefault` is `true`. UNVERIFIED: whether .NET 10 browser-wasm defaults it to false when trimming is on; see `/dotnet-wasm/`.
* **File I/O:** `File.OpenRead`, `File.ReadAllBytes` and `Path.Combine` all work against Emscripten's in-memory FS. The "write fetched bytes to `/tmp` first" pattern above avoids any patch.
* **Native decoders:** `GetOrLoadImage` → `TextureLoader.LoadFromMemory` → Stb/Ktx/Gli P/Invokes. On browser these fail at call time unless the natives are linked. For a textured PoC, decode images in JavaScript (`createImageBitmap`) and upload them through the Vulkan implementation, or link an Emscripten-built `stb`.

## Fallback: a self-written GLB reader

If `Brutal.Gltf` misbehaves under trimming, a minimal GLB reader is about **150–250 lines of C#**. It needs: header and chunk walk (12-byte header + `JSON`/`BIN` chunks); `System.Text.Json` `JsonDocument` navigation of `meshes[0].primitives[0]`, `accessors`, `bufferViews`; stride-aware accessor copy for float/uint16/uint32 components; and optional node TRS. This mirrors `GltfUtility.ParseBinary`/`ReadBinaryChunk` and `GetAccessorBufferSpan`.[^gltf-utility][^gltf-loader] For the PoC, the BRUTAL-native path above is preferred because it keeps BRUTAL "front and centre".

# Brutal.Render.Mesh

* `Mesh<TKey> : IMeshStorage<TKey>` stores one `NativeStrideList` per vertex stream key (`Dictionary<TKey, NativeStrideList> VertexBufferMap`) plus an optional `NativeStrideList IndexBuffer`. Extension helpers `SetVerticesFromData`, `AddVerticesFromData`, `SetIndicesFromData` and `AddIndicesFromData` copy spans in.[^mesh]
* `ProcGenMesh` (readonly struct: `OutPositions`, `OutNormals?`, `OutUVs?`, `OutIndices?`) and `ProcGenMeshLibrary.GeneratePlane/GenerateCube/GenerateSphere/GenerateTorus/GenerateDisk/GenerateCone`, `CalculateTangents`, `Offset/Scale/TransformWorldCoords`.[^procgen-core][^procgen] `ProcGenMesh.cs` imports `System.Linq.Expressions`. On Mono-wasm, `Expression.Compile` falls back to the interpreter, which is slower but works (UNVERIFIED that it is actually used on a hot path).
* No GPU code, so no Vulkan calls. Mesh upload is the app's job (KSA's `RenderCore.Mesh`; see [/brutal/ksa-bootstrap-reference.md](/brutal/ksa-bootstrap-reference.md)).

# Open questions

* UNVERIFIED: whether .NET 10 browser-wasm with `PublishTrimmed` keeps reflection-based System.Text.Json enabled by default.
* UNVERIFIED: the best way to link Emscripten-built `stb`/`meshoptimizer` static libraries so that `[LibraryImport("stb")]` binds (`NativeFileReference` naming rules); see `/dotnet-wasm/`.

[^gltf-loader]: Brutal.Gltf GltfLoader.cs
[^gltf-utility]: Brutal.Gltf GltfUtility.cs
[^gltf-accessor]: Brutal.Gltf Accessor.cs
[^gltf-schema]: Brutal.Gltf generated schema
[^gltf-json]: Brutal.Gltf JsonHelpers.cs
[^gltf-transform]: GltfLoader.Transform.cs
[^texture-loader]: Brutal.Texture TextureLoader.cs
[^stb]: Brutal.Stb Stb.cs
[^mesh]: Brutal.Render.Mesh Mesh.cs
[^procgen]: ProcGenMesh.Primitives.cs
[^procgen-core]: ProcGenMesh.cs
[^asminfo]: Metadata dump
[^inventory]: NuGet package inventory
