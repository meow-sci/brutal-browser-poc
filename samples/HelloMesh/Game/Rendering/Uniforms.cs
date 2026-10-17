using Brutal;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace HelloMesh.Rendering;

/// <summary>Per-draw uniform block; mirrors <c>PerDraw</c> in mesh.vert/mesh.frag (std140) and is padded to 256 bytes.</summary>
[StructLayout(LayoutKind.Sequential, Size = 256)]
public struct PerDraw
{
    public float4x4 Model;
    public float4x4 View;
    public float4x4 Proj;
    public float4 BaseColor;
    public float4 LightDir;
}

/// <summary>A persistently mapped uniform buffer holding one <see cref="PerDraw"/> per draw, addressed with dynamic offsets.</summary>
public sealed class DynamicUniformBuffer : IDisposable
{
    private readonly BufferEx _buffer;
    private readonly MappedMemory _mapped;

    /// <summary>Distance in bytes between two draws: the block size rounded up to the device's offset alignment.</summary>
    public int Stride { get; }

    public VkBuffer Buffer => _buffer.VkBuffer;

    public DynamicUniformBuffer(DeviceEx device, int drawCount, int offsetAlignment)
    {
        Stride = (Unsafe.SizeOf<PerDraw>() + offsetAlignment - 1) / offsetAlignment * offsetAlignment;
        _buffer = device.CreateBuffer(new BufferEx.CreateInfo
        {
            Name = "uniforms",
            BufferSize = ByteSize.Of(Stride * drawCount),
            BufferUsage = VkBufferUsageFlags.UniformBufferBit,
            AllocPreference = MemoryPreference.PreferCpu,
            AllocRequiredProperties = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,
        });
        _mapped = _buffer.Map();
    }

    /// <summary>Writes the block for draw <paramref name="drawIndex"/> and returns its dynamic offset.</summary>
    public int Write(int drawIndex, in PerDraw value)
    {
        var offset = drawIndex * Stride;
        MemoryMarshal.Write(_mapped.AsSpan().Slice(offset, Unsafe.SizeOf<PerDraw>()), in value);
        return offset;
    }

    public void Dispose()
    {
        _mapped.Unmap();
        _buffer.Dispose();
    }
}
