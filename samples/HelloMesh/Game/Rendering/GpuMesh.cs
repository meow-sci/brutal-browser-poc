using Brutal;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using HelloMesh.Assets;

namespace HelloMesh.Rendering;

/// <summary>Interleaved vertex layout: position float3, normal float3, uv float2 (32 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    public const int Stride = 32;

    public float3 Position;
    public float3 Normal;
    public float2 Uv;
}

/// <summary>Device-local vertex and index buffers of one primitive, filled through a staging buffer.</summary>
public sealed class GpuMesh : IDisposable
{
    private readonly BufferEx _vertices;
    private readonly BufferEx _indices;

    public int IndexCount { get; }
    public VkIndexType IndexType { get; }
    public int MaterialIndex { get; }
    public float4x4 World { get; }
    public VkBuffer VertexBuffer => _vertices.VkBuffer;
    public VkBuffer IndexBuffer => _indices.VkBuffer;

    private GpuMesh(BufferEx vertices, BufferEx indices, GlbPrimitive source, VkIndexType indexType)
    {
        _vertices = vertices;
        _indices = indices;
        IndexCount = source.Indices.Length;
        IndexType = indexType;
        MaterialIndex = source.MaterialIndex;
        World = source.World;
    }

    /// <summary>Creates the buffers and records the staging copies into <paramref name="cmd"/>; the caller submits it and waits.</summary>
    public static GpuMesh Upload(DeviceEx device, StagingPool staging, CommandBuffer cmd, GlbPrimitive primitive)
    {
        Debug.Assert(Unsafe.SizeOf<Vertex>() == Vertex.Stride);
        var vertexData = new Vertex[primitive.Positions.Length];
        for (var i = 0; i < vertexData.Length; i++)
            vertexData[i] = new Vertex { Position = primitive.Positions[i], Normal = primitive.Normals[i], Uv = primitive.Uvs[i] };

        var useShortIndices = vertexData.Length <= ushort.MaxValue + 1;
        var indexType = useShortIndices ? VkIndexType.UInt16 : VkIndexType.UInt32;
        var vertices = CreateDeviceBuffer(device, "mesh.vertices", ByteSize.Of(vertexData.Length * Vertex.Stride), VkBufferUsageFlags.VertexBufferBit);
        BufferEx indices;
        BufferEx stagedIndices;
        if (useShortIndices)
        {
            // Buffer sizes must be multiples of 4, so an odd index count gets one unused trailing index.
            var shortIndices = new ushort[(primitive.Indices.Length + 1) & ~1];
            for (var i = 0; i < primitive.Indices.Length; i++)
                shortIndices[i] = (ushort)primitive.Indices[i];
            indices = CreateDeviceBuffer(device, "mesh.indices", ByteSize.Of(shortIndices.Length * sizeof(ushort)), VkBufferUsageFlags.IndexBufferBit);
            stagedIndices = staging.AddStagingBuffer<ushort>(shortIndices);
        }
        else
        {
            indices = CreateDeviceBuffer(device, "mesh.indices", ByteSize.Of(primitive.Indices.Length * sizeof(uint)), VkBufferUsageFlags.IndexBufferBit);
            stagedIndices = staging.AddStagingBuffer<uint>(primitive.Indices);
        }
        var stagedVertices = staging.AddStagingBuffer<Vertex>(vertexData);
        cmd.CopyBuffer(stagedVertices.VkBuffer, vertices.VkBuffer, vertices.BufferSize);
        cmd.CopyBuffer(stagedIndices.VkBuffer, indices.VkBuffer, indices.BufferSize);
        return new GpuMesh(vertices, indices, primitive, indexType);
    }

    private static BufferEx CreateDeviceBuffer(DeviceEx device, string name, ByteSize size, VkBufferUsageFlags usage) =>
        device.CreateBuffer(new BufferEx.CreateInfo
        {
            Name = name,
            BufferSize = size,
            BufferUsage = usage | VkBufferUsageFlags.TransferDstBit,
            AllocPreference = MemoryPreference.PreferGpu,
            AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
        });

    public void Dispose()
    {
        _vertices.Dispose();
        _indices.Dispose();
    }
}
