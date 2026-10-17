using System.Runtime.InteropServices;

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// A VkDeviceMemory. Device-local memory is bookkeeping only (each WebGPU resource owns its storage); host-visible memory has
/// a zero-filled shadow in the wasm heap that vkMapMemory returns and that is uploaded with writeBuffer.
/// </summary>
internal sealed unsafe class DeviceMemoryObject : IcdObject
{
    public override string VkTypeName => "VkDeviceMemory";
    public ulong Size;
    public int TypeIndex;
    public byte* Shadow;
    public bool IsMapped, EverMapped;
    public ulong MappedOffset, MappedSize;
    /// <summary>Readbacks still writing into the shadow; vkFreeMemory defers freeing it until they are done.</summary>
    public int PendingReadbacks;
    public bool Freed;
    /// <summary>Buffers bound to this memory (BRUTAL's DeviceEx binds one per allocation at offset 0).</summary>
    public readonly List<BufferObject> Buffers = new(1);

    public bool HostVisible => Shadow != null;

    public void FreeShadow()
    {
        if (Shadow != null)
        {
            NativeMemory.AlignedFree(Shadow);
            Shadow = null;
        }
    }
}

internal sealed class BufferObject : IcdObject
{
    public override string VkTypeName => "VkBuffer";
    public ulong Size;
    public ulong GpuSize;
    public int Gpu;
    public int GpuUsage;
    public DeviceMemoryObject? Memory;
    public ulong MemoryOffset;
    /// <summary>GPU→CPU readbacks into this buffer in flight: uploads from the (stale) shadow are skipped meanwhile.</summary>
    public int PendingReadbacks;

    public override int GpuHandle => Gpu;

    /// <summary>The buffer is bound to host-visible memory (its bytes live in the shadow).</summary>
    public bool HostVisible => Memory is { HostVisible: true };

    public unsafe byte* ShadowAddress(ulong offset) => Memory!.Shadow + MemoryOffset + offset;
}
