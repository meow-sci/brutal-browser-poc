using Brutal.GltfApi;
using Brutal.Numerics;

namespace HelloMesh.Assets;

/// <summary>Loads a GLB into a <see cref="GlbModel"/> with Brutal.Gltf (desktop and browser).</summary>
public static class ModelLoader
{
    /// <summary>
    /// Parses <paramref name="glb"/>. Brutal.Gltf only opens files (and re-reads the BIN chunk from disk), so the bytes go through a
    /// temporary file (the browser has an in-memory virtual file system).
    /// </summary>
    public static GlbModel Load(byte[] glb)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, glb);
            using var loader = new GltfLoader(path);
            return Convert(loader);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static GlbModel Convert(GltfLoader loader)
    {
        var gltf = loader.GltfJson;
        var images = (gltf.Images ?? []).Select(image => PngDecoder.Decode(loader.GetBufferView(image.BufferView
            ?? throw new NotSupportedException("Only images stored in a buffer view are supported.")))).ToList();
        var materials = (gltf.Materials ?? []).Select(material => ConvertMaterial(gltf, material)).ToList();
        var nodes = (gltf.Nodes ?? []).Select(ConvertNode).ToList();
        var defaultMaterial = materials.Count;

        var primitives = new List<GlbPrimitive>();
        GlbScene.Traverse(nodes, SceneRoots(gltf, nodes), (mesh, world) =>
        {
            foreach (var primitive in gltf.Meshes[mesh].Primitives)
                primitives.Add(ConvertPrimitive(loader, primitive, world, defaultMaterial));
        });
        if (primitives.Any(p => p.MaterialIndex == defaultMaterial))
            materials.Add(GlbMaterial.Create(new float4(1f, 1f, 1f, 1f), null));
        return new GlbModel(primitives, materials, images);
    }

    private static GlbMaterial ConvertMaterial(Gltf gltf, Material material)
    {
        var pbr = material.PbrMetallicRoughness;
        int? image = pbr?.BaseColorTexture is { } texture ? gltf.Textures[texture.Index].Source : null;
        return GlbMaterial.Create(pbr?.BaseColorFactor ?? new float4(1f, 1f, 1f, 1f), image);
    }

    private static GlbNode ConvertNode(Node node)
    {
        var local = node.Matrix.IsIdentity ? GlbScene.Compose(node.Translation, node.Rotation, node.Scale) : node.Matrix;
        return new GlbNode(node.Children ?? [], node.Mesh, local);
    }

    private static IEnumerable<int> SceneRoots(Gltf gltf, List<GlbNode> nodes)
    {
        if (gltf.Scenes is { Length: > 0 } scenes)
            return scenes[gltf.Scene ?? 0].Nodes ?? [];
        var children = nodes.SelectMany(n => n.Children).ToHashSet();
        return Enumerable.Range(0, nodes.Count).Where(i => !children.Contains(i)).ToArray();
    }

    private static GlbPrimitive ConvertPrimitive(GltfLoader loader, MeshPrimitive primitive, float4x4 world, int defaultMaterial)
    {
        if (primitive.Mode != MeshPrimitive.ModeEnum.Triangles)
            throw new NotSupportedException("Only TRIANGLES primitives are supported.");
        var positions = ReadAccessor(loader, primitive.Attributes["POSITION"], AccessorData.ReadFloat3);
        var normals = primitive.Attributes.TryGetValue("NORMAL", out var normal) ? ReadAccessor(loader, normal, AccessorData.ReadFloat3) : null;
        var uvs = primitive.Attributes.TryGetValue("TEXCOORD_0", out var uv) ? ReadAccessor(loader, uv, AccessorData.ReadFloat2) : null;
        var indices = primitive.Indices is { } index ? ReadAccessor(loader, index, AccessorData.ReadIndices) : AccessorData.Sequence(positions.Length);
        return GlbPrimitive.Create(positions, normals, uvs, indices, primitive.Material ?? defaultMaterial, world);
    }

    private static T[] ReadAccessor<T>(GltfLoader loader, int index, Func<ReadOnlySpan<byte>, AccessorLayout, T[]> read)
    {
        var accessor = loader.GltfJson.Accessors[index];
        var view = loader.GltfJson.BufferViews[accessor.BufferView ?? throw new NotSupportedException("Sparse accessors are not supported.")];
        var layout = new AccessorLayout(accessor.Count, ComponentType(accessor.ComponentType), TypeName(accessor.Type), view.ByteStride ?? 0);
        return read(loader.GetAccessorBufferSpan(index), layout);
    }

    private static int ComponentType(Accessor.ComponentTypeEnum type) => type switch
    {
        Accessor.ComponentTypeEnum.UnsignedByte => 5121,
        Accessor.ComponentTypeEnum.UnsignedShort => 5123,
        Accessor.ComponentTypeEnum.UnsignedInt => 5125,
        Accessor.ComponentTypeEnum.Float => 5126,
        _ => (int)type,
    };

    private static string TypeName(Accessor.TypeEnum type) => type switch
    {
        Accessor.TypeEnum.Scalar => "SCALAR",
        Accessor.TypeEnum.Vec2 => "VEC2",
        Accessor.TypeEnum.Vec3 => "VEC3",
        Accessor.TypeEnum.Vec4 => "VEC4",
        _ => type.ToString(),
    };
}
