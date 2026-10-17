using System.Runtime.InteropServices;
using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// Memory model (docs/research/webgpu/memory-model-and-mapping-emulation.md): type 0 DEVICE_LOCAL is bookkeeping only; type 1
/// HOST_VISIBLE|HOST_COHERENT is a zero-filled shadow in the wasm heap. vkMapMemory returns a pointer into the shadow;
/// vkFlushMappedMemoryRanges and vkUnmapMemory upload the affected ranges of the bound buffers with writeBuffer at once.
/// Because coherent memory may be written without a flush, every vkQueueSubmit also uploads the currently mapped range of
/// every mapped allocation bound to a buffer (strategy S1 "whole mapped": its cost is proportional to the mapped bytes of
/// all persistently mapped allocations, every submit; buffers with GPU→CPU readbacks in flight are skipped).
/// </summary>
internal static unsafe partial class Icd
{
    /// <summary>Host-visible allocations that are currently mapped (uploaded at every submit).</summary>
    private static readonly List<DeviceMemoryObject> s_mappedMemories = new();

    public static VkResult vkAllocateMemory(VkDevice device, VkMemoryAllocateInfo* pAllocateInfo, VkAllocationCallbacks* pAllocator,
        VkDeviceMemory* pMemory)
    {
        const string fn = "vkAllocateMemory";
        var size = pAllocateInfo->AllocationSize.Value;
        var type = pAllocateInfo->MemoryTypeIndex;
        if (type is not (Profile.DeviceLocalType or Profile.HostVisibleType))
        {
            IcdLog.Error($"{fn}: memory type {type} does not exist (0 = DEVICE_LOCAL, 1 = HOST_VISIBLE|HOST_COHERENT)");
            return VkResult.ErrorOutOfDeviceMemory;
        }
        var flags = FindNext<VkMemoryAllocateFlagsInfo>(pAllocateInfo->Next, VkStructureType.MemoryAllocateFlagsInfo);
        if (flags != null && (flags->Flags & (VkMemoryAllocateFlags.DeviceAddressBit | VkMemoryAllocateFlags.DeviceAddressCaptureReplayBit)) != 0)
        {
            IcdLog.Unsupported(fn, "VK_MEMORY_ALLOCATE_DEVICE_ADDRESS_BIT (buffer device address)");
            return VkResult.ErrorFeatureNotPresent;
        }

        var memory = new DeviceMemoryObject { Size = size, TypeIndex = type };
        if (type == Profile.HostVisibleType)
        {
            if (size > int.MaxValue)
            {
                IcdLog.Error($"{fn}: host-visible allocation of {size} bytes does not fit the wasm heap");
                return VkResult.ErrorOutOfHostMemory;
            }
            // 64-byte aligned (minMemoryMapAlignment), + slack for 4-byte-widened uploads
            var bytes = (nuint)((size + 7) & ~3UL);
            memory.Shadow = (byte*)NativeMemory.AlignedAlloc(bytes, 64);
            new Span<byte>(memory.Shadow, (int)bytes).Clear();
        }
        HandleTable.Add(memory);
        *pMemory = new VkDeviceMemory(memory.Handle);
        return VkResult.Success;
    }

    public static void vkFreeMemory(VkDevice device, VkDeviceMemory memory, VkAllocationCallbacks* pAllocator)
    {
        var obj = HandleTable.Get<DeviceMemoryObject>(memory.VkHandle);
        if (obj is null)
            return;
        if (obj.IsMapped)
            s_mappedMemories.Remove(obj);
        foreach (var b in obj.Buffers)
            b.Memory = null;
        obj.Buffers.Clear();
        Readbacks.PollAll();
        obj.Freed = true;
        if (obj.PendingReadbacks == 0)
            obj.FreeShadow(); // otherwise freed when the last readback into it has landed
        HandleTable.Remove(obj.Handle);
    }

    public static VkResult vkMapMemory(VkDevice device, VkDeviceMemory memory, ByteSize64 offset, ByteSize64 size, VkMemoryMapFlags flags, void** ppData)
    {
        const string fn = "vkMapMemory";
        var obj = HandleTable.Lookup<DeviceMemoryObject>(memory.VkHandle, fn);
        if (obj is null || !obj.HostVisible)
        {
            *ppData = null;
            if (obj is not null)
                IcdLog.Error($"{fn}: {obj} is not HOST_VISIBLE (memory type {obj.TypeIndex})");
            return VkResult.ErrorMemoryMapFailed;
        }
        if (offset.Value >= obj.Size)
        {
            *ppData = null;
            IcdLog.Error($"{fn}: offset {offset.Value} is outside {obj} ({obj.Size} bytes)");
            return VkResult.ErrorMemoryMapFailed;
        }
        if (obj.IsMapped)
        {
            if (IcdLog.First(fn, "map-twice"))
                IcdLog.Warn($"{fn}: {obj} is already mapped (Vulkan requires vkUnmapMemory first); remapping");
        }
        else
            s_mappedMemories.Add(obj);
        obj.IsMapped = obj.EverMapped = true;
        obj.MappedOffset = offset.Value;
        obj.MappedSize = Mappings.IsWholeSize(size.Value) ? obj.Size - offset.Value : Math.Min(size.Value, obj.Size - offset.Value);
        *ppData = obj.Shadow + offset.Value;
        return VkResult.Success;
    }

    public static VkResult vkMapMemory2(VkDevice device, VkMemoryMapInfo* pMemoryMapInfo, void** ppData) =>
        vkMapMemory(device, pMemoryMapInfo->Memory, pMemoryMapInfo->Offset, pMemoryMapInfo->Size, pMemoryMapInfo->Flags, ppData);

    public static void vkUnmapMemory(VkDevice device, VkDeviceMemory memory)
    {
        var obj = HandleTable.Lookup<DeviceMemoryObject>(memory.VkHandle, "vkUnmapMemory");
        if (obj is null || !obj.IsMapped)
            return;
        UploadRange(obj, obj.MappedOffset, obj.MappedSize);
        obj.IsMapped = false;
        s_mappedMemories.Remove(obj);
    }

    public static VkResult vkUnmapMemory2(VkDevice device, VkMemoryUnmapInfo* pMemoryUnmapInfo)
    {
        vkUnmapMemory(device, pMemoryUnmapInfo->Memory);
        return VkResult.Success;
    }

    public static VkResult vkFlushMappedMemoryRanges(VkDevice device, int memoryRangeCount, VkMappedMemoryRange* pMemoryRanges)
    {
        for (var i = 0; i < memoryRangeCount; i++)
        {
            ref var r = ref pMemoryRanges[i];
            var obj = HandleTable.Lookup<DeviceMemoryObject>(r.Memory.VkHandle, "vkFlushMappedMemoryRanges");
            if (obj is null || !obj.HostVisible)
                continue;
            var size = Mappings.IsWholeSize(r.Size.Value) ? obj.Size - Math.Min(r.Offset.Value, obj.Size) : r.Size.Value;
            UploadRange(obj, r.Offset.Value, size);
        }
        return VkResult.Success;
    }

    public static VkResult vkInvalidateMappedMemoryRanges(VkDevice device, int memoryRangeCount, VkMappedMemoryRange* pMemoryRanges) =>
        VkResult.Success; // readbacks write the shadow when they complete (see vkGetFenceStatus)

    public static void vkGetDeviceMemoryCommitment(VkDevice device, VkDeviceMemory memory, ByteSize64* pCommittedMemoryInBytes) =>
        *pCommittedMemoryInBytes = (ByteSize64)(HandleTable.Get<DeviceMemoryObject>(memory.VkHandle)?.Size ?? 0UL);

    /// <summary>writeBuffer of [offset, offset+size) of the memory into every bound buffer it overlaps (4-byte padded).</summary>
    private static void UploadRange(DeviceMemoryObject memory, ulong offset, ulong size)
    {
        foreach (var b in memory.Buffers)
        {
            if (b.PendingReadbacks > 0 || !Overlap(memory, b, offset, size, out var bufferOffset, out var length))
                continue;
            WebIcd.Bridge.WriteBuffer(b.Gpu, bufferOffset, (nint)(memory.Shadow + b.MemoryOffset + bufferOffset), (int)length);
        }
    }

    /// <summary>
    /// The part of buffer <paramref name="b"/> covered by memory range [offset, offset+size), widened to 4-byte boundaries
    /// (writeBuffer needs multiples of 4; shadows carry 4 bytes of slack, so the widened read stays inside the allocation).
    /// </summary>
    private static bool Overlap(DeviceMemoryObject memory, BufferObject b, ulong offset, ulong size, out ulong bufferOffset, out ulong length)
    {
        var start = Math.Max(offset, b.MemoryOffset);
        var end = Math.Min(offset + size, b.MemoryOffset + b.Size);
        bufferOffset = length = 0;
        if (end <= start || b.Gpu == 0)
            return false;
        bufferOffset = (start - b.MemoryOffset) & ~3UL;
        var bufferEnd = Math.Min((end - b.MemoryOffset + 3) & ~3UL, b.GpuSize);
        length = bufferEnd - bufferOffset;
        return length > 0;
    }

    /// <summary>The coherency flush of vkQueueSubmit: WriteBuffer records for every mapped, buffer-bound, host-visible range.</summary>
    private static void AppendCoherentUploads(CommandStream stream)
    {
        foreach (var memory in s_mappedMemories)
        {
            foreach (var b in memory.Buffers)
            {
                if (b.PendingReadbacks > 0 || !Overlap(memory, b, memory.MappedOffset, memory.MappedSize, out var bufferOffset, out var length))
                    continue;
                var p = stream.Record(Op.WriteBuffer, 4);
                p[0] = (uint)b.Gpu;
                p[1] = (uint)bufferOffset;
                p[2] = (uint)(nint)(memory.Shadow + b.MemoryOffset + bufferOffset);
                p[3] = (uint)length;
            }
        }
    }
}
