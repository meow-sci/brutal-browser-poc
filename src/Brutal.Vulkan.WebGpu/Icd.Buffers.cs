using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

internal static unsafe partial class Icd
{
    public static VkResult vkCreateBuffer(VkDevice device, VkBufferCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator, VkBuffer* pBuffer)
    {
        const string fn = "vkCreateBuffer";
        var size = pCreateInfo->Size.Value;
        if ((pCreateInfo->Flags & (VkBufferCreateFlags.SparseBindingBit | VkBufferCreateFlags.SparseResidencyBit | VkBufferCreateFlags.SparseAliasedBit)) != 0)
        {
            IcdLog.Unsupported(fn, "sparse buffers");
            return VkResult.ErrorFeatureNotPresent;
        }
        if ((pCreateInfo->Usage & VkBufferUsageFlags.ShaderDeviceAddressBit) != 0)
            IcdLog.Ignored(fn, "VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT (buffer device address)");
        if ((pCreateInfo->Usage & (VkBufferUsageFlags.UniformTexelBufferBit | VkBufferUsageFlags.StorageTexelBufferBit)) != 0)
            IcdLog.Ignored(fn, "texel buffer usage (no texel buffers in WebGPU; the buffer gets STORAGE usage)");

        var gpuSize = Math.Max(4UL, (size + 3) & ~3UL);
        if (gpuSize > (ulong)Profile.Limits.MaxBufferSize)
        {
            IcdLog.Error($"{fn}: {size} bytes exceeds the WebGPU maxBufferSize ({Profile.Limits.MaxBufferSize})");
            return VkResult.ErrorOutOfDeviceMemory;
        }
        var usage = Mappings.BufferUsage(pCreateInfo->Usage);
        var gpu = WebIcd.Bridge.CreateBuffer(gpuSize, usage, "");
        if (gpu == 0)
        {
            IcdLog.Error($"{fn}: createBuffer({gpuSize}) failed");
            return VkResult.ErrorOutOfDeviceMemory;
        }
        var buffer = new BufferObject { Size = size, GpuSize = gpuSize, Gpu = gpu, GpuUsage = usage };
        HandleTable.Add(buffer);
        *pBuffer = new VkBuffer(buffer.Handle);
        return VkResult.Success;
    }

    public static void vkDestroyBuffer(VkDevice device, VkBuffer buffer, VkAllocationCallbacks* pAllocator)
    {
        var obj = HandleTable.Get<BufferObject>(buffer.VkHandle);
        if (obj is null)
            return;
        obj.Memory?.Buffers.Remove(obj);
        obj.Memory = null;
        WebIcd.Bridge.Release(obj.Gpu);
        HandleTable.Remove(obj.Handle);
    }

    public static void vxDestroyBuffers(VkDevice device, VkBuffer* pBuffers, int count, VkAllocationCallbacks* pAllocator)
    {
        for (var i = 0; i < count; i++)
            vkDestroyBuffer(device, pBuffers[i], pAllocator);
    }

    public static void vxDestroyBufferViews(VkDevice device, VkBufferView* pBufferViews, int count, VkAllocationCallbacks* pAllocator)
    {
        // texel buffer views are never created (vkCreateBufferView is unsupported); nothing to destroy
    }

    public static void vkGetBufferMemoryRequirements(VkDevice device, VkBuffer buffer, VkMemoryRequirements* pMemoryRequirements)
    {
        var obj = HandleTable.Lookup<BufferObject>(buffer.VkHandle, "vkGetBufferMemoryRequirements");
        BufferRequirements(obj?.GpuSize ?? 4, pMemoryRequirements);
    }

    private static void BufferRequirements(ulong gpuSize, VkMemoryRequirements* r)
    {
        r->Size = (ByteSize64)gpuSize;
        r->Alignment = (ByteSize64)256UL;
        r->MemoryTypeBits = (1 << Profile.DeviceLocalType) | (1 << Profile.HostVisibleType);
    }

    public static void vkGetBufferMemoryRequirements2(VkDevice device, VkBufferMemoryRequirementsInfo2* pInfo, VkMemoryRequirements2* pMemoryRequirements) =>
        vkGetBufferMemoryRequirements(device, pInfo->Buffer, &pMemoryRequirements->MemoryRequirements);

    public static void vkGetDeviceBufferMemoryRequirements(VkDevice device, VkDeviceBufferMemoryRequirements* pInfo, VkMemoryRequirements2* pMemoryRequirements) =>
        BufferRequirements(Math.Max(4UL, (pInfo->CreateInfo->Size.Value + 3) & ~3UL), &pMemoryRequirements->MemoryRequirements);

    public static VkResult vkBindBufferMemory(VkDevice device, VkBuffer buffer, VkDeviceMemory memory, ByteSize64 memoryOffset)
    {
        const string fn = "vkBindBufferMemory";
        var b = HandleTable.Lookup<BufferObject>(buffer.VkHandle, fn);
        var m = HandleTable.Lookup<DeviceMemoryObject>(memory.VkHandle, fn);
        if (b is null || m is null)
            return VkResult.ErrorOutOfDeviceMemory;
        if (memoryOffset.Value + b.Size > m.Size)
        {
            IcdLog.Error($"{fn}: {b} ({b.Size} bytes) at offset {memoryOffset.Value} does not fit {m} ({m.Size} bytes)");
            return VkResult.ErrorOutOfDeviceMemory;
        }
        if (WebIcd.Options.ValidateProfile)
            foreach (var other in m.Buffers)
                if (memoryOffset.Value < other.MemoryOffset + other.Size && other.MemoryOffset < memoryOffset.Value + b.Size)
                    WebProfileLint.Report(fn, "memory aliasing", $"{b} overlaps {other} in {m}; WebGPU resources cannot alias (each keeps its own contents)");
        b.Memory?.Buffers.Remove(b);
        b.Memory = m;
        b.MemoryOffset = memoryOffset.Value;
        m.Buffers.Add(b);
        if (m.EverMapped)
            UploadRange(m, memoryOffset.Value, b.Size); // the memory already holds host-written data
        return VkResult.Success;
    }

    public static VkResult vkBindBufferMemory2(VkDevice device, int bindInfoCount, VkBindBufferMemoryInfo* pBindInfos)
    {
        for (var i = 0; i < bindInfoCount; i++)
        {
            var r = vkBindBufferMemory(device, pBindInfos[i].Buffer, pBindInfos[i].Memory, pBindInfos[i].MemoryOffset);
            if (r != VkResult.Success)
                return r;
        }
        return VkResult.Success;
    }

    public static VkDeviceAddress vkGetBufferDeviceAddress(VkDevice device, VkBufferDeviceAddressInfo* pInfo)
    {
        IcdLog.Unsupported("vkGetBufferDeviceAddress", "buffer device address (WGSL has no pointers into buffers)");
        throw new VulkanUnsupportedException("vkGetBufferDeviceAddress", "buffer device address is not available on WebGPU (profile " + Profile.Name + ")");
    }
}
