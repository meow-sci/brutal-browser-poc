using Brutal.Numerics;

namespace HelloMesh.Assets;

/// <summary>A decoded RGBA8 image (4 bytes per pixel, rows tightly packed).</summary>
public sealed record GlbImage(int Width, int Height, byte[] Rgba);

/// <summary>A material reduced to a base colour factor and an optional base colour texture.</summary>
/// <param name="BaseColor">Linear RGBA factor that multiplies the texture.</param>
/// <param name="TextureIndex">Index into <see cref="GlbModel.Images"/>, or null for an untextured material.</param>
public sealed record GlbMaterial(float4 BaseColor, int? TextureIndex)
{
    private static readonly float4 White = new(1f, 1f, 1f, 1f);
    private static readonly float4 UntexturedDefault = new(0.8f, 0.8f, 0.8f, 1f);

    /// <summary>Builds a material; an untextured material without an explicit colour (glTF default white) is rendered as 0.8 grey.</summary>
    public static GlbMaterial Create(float4 baseColorFactor, int? textureIndex) =>
        new(textureIndex is null && baseColorFactor == White ? UntexturedDefault : baseColorFactor, textureIndex);
}

/// <summary>A triangle list with per-vertex normals and UVs, placed in the model by <see cref="World"/>.</summary>
public sealed class GlbPrimitive
{
    public float3[] Positions { get; }
    public float3[] Normals { get; }
    public float2[] Uvs { get; }
    public uint[] Indices { get; }
    public int MaterialIndex { get; }

    /// <summary>Node-to-world transform in BRUTAL's row-vector convention (<c>v * World</c>).</summary>
    public float4x4 World { get; }

    private GlbPrimitive(float3[] positions, float3[] normals, float2[] uvs, uint[] indices, int materialIndex, float4x4 world)
    {
        Positions = positions;
        Normals = normals;
        Uvs = uvs;
        Indices = indices;
        MaterialIndex = materialIndex;
        World = world;
    }

    /// <summary>Validates the streams; missing normals are derived from the triangles and missing UVs are zero.</summary>
    public static GlbPrimitive Create(float3[] positions, float3[]? normals, float2[]? uvs, uint[] indices, int materialIndex, float4x4 world)
    {
        if (indices.Length % 3 != 0)
            throw new InvalidDataException("Index count is not a multiple of 3.");
        foreach (var index in indices)
            if (index >= positions.Length)
                throw new InvalidDataException("Index out of range of the vertex streams.");
        if ((normals != null && normals.Length != positions.Length) || (uvs != null && uvs.Length != positions.Length))
            throw new InvalidDataException("Vertex streams have different lengths.");
        return new GlbPrimitive(positions, normals ?? ComputeNormals(positions, indices), uvs ?? new float2[positions.Length], indices, materialIndex, world);
    }

    private static float3[] ComputeNormals(float3[] positions, uint[] indices)
    {
        var normals = new float3[positions.Length];
        for (var i = 0; i < indices.Length; i += 3)
        {
            var (a, b, c) = ((int)indices[i], (int)indices[i + 1], (int)indices[i + 2]);
            var faceNormal = float3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            normals[a] += faceNormal;
            normals[b] += faceNormal;
            normals[c] += faceNormal;
        }
        for (var i = 0; i < normals.Length; i++)
            normals[i] = float3.NormalizeOrZero(normals[i]);
        return normals;
    }
}

/// <summary>Immutable CPU-side model: primitives with their world matrices, materials and decoded images.</summary>
public sealed class GlbModel
{
    public IReadOnlyList<GlbPrimitive> Primitives { get; }
    public IReadOnlyList<GlbMaterial> Materials { get; }
    public IReadOnlyList<GlbImage> Images { get; }

    /// <summary>Axis-aligned bounds of all primitives in world space.</summary>
    public (float3 Min, float3 Max) Bounds { get; }

    public GlbModel(IReadOnlyList<GlbPrimitive> primitives, IReadOnlyList<GlbMaterial> materials, IReadOnlyList<GlbImage> images)
    {
        foreach (var primitive in primitives)
            if (primitive.MaterialIndex < 0 || primitive.MaterialIndex >= materials.Count)
                throw new InvalidDataException("Primitive references a missing material.");
        foreach (var material in materials)
            if (material.TextureIndex is { } texture && (texture < 0 || texture >= images.Count))
                throw new InvalidDataException("Material references a missing image.");
        Primitives = primitives;
        Materials = materials;
        Images = images;
        Bounds = ComputeBounds(primitives);
    }

    private static (float3, float3) ComputeBounds(IReadOnlyList<GlbPrimitive> primitives)
    {
        var min = new float3(float.MaxValue);
        var max = new float3(float.MinValue);
        foreach (var primitive in primitives)
            foreach (var position in primitive.Positions)
            {
                var world = float3.Transform(position, primitive.World);
                min = float3.Min(min, world);
                max = float3.Max(max, world);
            }
        return primitives.Count == 0 ? (float3.Zero, float3.Zero) : (min, max);
    }
}

/// <summary>A glTF node reduced to what the loaders need: children, optional mesh and local matrix.</summary>
internal readonly record struct GlbNode(int[] Children, int? Mesh, float4x4 Local);

/// <summary>Scene-graph walk shared by both loaders.</summary>
internal static class GlbScene
{
    private const int MaxDepth = 64;

    /// <summary>Composes a local matrix from translation, rotation and scale (scale first, translation last in row-vector order).</summary>
    public static float4x4 Compose(float3 translation, floatQuat rotation, float3 scale) =>
        float4x4.CreateScale(scale) * float4x4.CreateFromQuaternion(rotation) * float4x4.CreateTranslation(translation);

    /// <summary>Calls <paramref name="visitMesh"/>(mesh index, world matrix) for every mesh node reachable from <paramref name="roots"/>.</summary>
    public static void Traverse(IReadOnlyList<GlbNode> nodes, IEnumerable<int> roots, Action<int, float4x4> visitMesh)
    {
        foreach (var root in roots)
            Visit(nodes, root, float4x4.Identity, 0, visitMesh);
    }

    private static void Visit(IReadOnlyList<GlbNode> nodes, int index, float4x4 parentWorld, int depth, Action<int, float4x4> visitMesh)
    {
        if (depth > MaxDepth)
            throw new InvalidDataException("Node hierarchy is too deep or cyclic.");
        if (index < 0 || index >= nodes.Count)
            throw new InvalidDataException($"Node index {index} out of range.");
        var node = nodes[index];
        var world = node.Local * parentWorld;
        if (node.Mesh is { } mesh)
            visitMesh(mesh, world);
        foreach (var child in node.Children)
            Visit(nodes, child, world, depth + 1, visitMesh);
    }
}
