using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace WebGpuIcdSmoke;

/// <summary>
/// One compute dispatch through BRUTAL's API: a storage buffer filled by <c>fill.comp</c> (push-constant bias), copied into
/// host-visible memory and read back once its fence signals (polled with a zero timeout, a frame or more later on the web ICD).
/// </summary>
public sealed unsafe class ComputeCheck : IDisposable
{
    public const int Count = 64;
    public const uint Bias = 7;

    private readonly DeviceEx _device;
    private readonly BufferEx _storage;
    private readonly BufferEx _readback;
    private readonly DescriptorSetLayoutEx _setLayout;
    private readonly DescriptorPoolEx _pool;
    private readonly VkPipelineLayout _pipelineLayout;
    private readonly VkPipeline _pipeline;
    private readonly VkCommandPool _commandPool;
    private readonly VkFence _fence;

    public uint[]? Result { get; private set; }

    public ComputeCheck(DeviceEx device, Queue queue, ReadOnlySpan<byte> spv)
    {
        _device = device;
        _storage = device.CreateBuffer(new BufferEx.CreateInfo
        {
            Name = "compute.out", BufferSize = ByteSize.Of<uint>(Count),
            BufferUsage = VkBufferUsageFlags.StorageBufferBit | VkBufferUsageFlags.TransferSrcBit,
            AllocPreference = MemoryPreference.PreferGpu, AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
        });
        _readback = device.CreateBuffer(new BufferEx.CreateInfo
        {
            Name = "compute.readback", BufferSize = ByteSize.Of<uint>(Count), BufferUsage = VkBufferUsageFlags.TransferDstBit,
            AllocPreference = MemoryPreference.PreferCpu,
            AllocRequiredProperties = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,
        });
        var binding = new VkDescriptorSetLayoutBinding { Binding = 0, DescriptorType = VkDescriptorType.StorageBuffer, DescriptorCount = 1, StageFlags = VkShaderStageFlags.ComputeBit };
        _setLayout = device.CreateDescriptorSetLayout(new DescriptorSetLayoutEx.CreateInfo { Bindings = new Span<VkDescriptorSetLayoutBinding>(&binding, 1) }, null);
        var size = new VkDescriptorPoolSize { Type = VkDescriptorType.StorageBuffer, DescriptorCount = 1 };
        _pool = device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo { MaxSets = 1, PoolSizes = new Span<VkDescriptorPoolSize>(&size, 1) }, null);
        var set = device.AllocateDescriptorSet(_pool, _setLayout);
        var info = new VkDescriptorBufferInfo { Buffer = _storage.VkBuffer, Range = VK.WHOLE_SIZE };
        device.UpdateDescriptorSets([new VkWriteDescriptorSet { DstSet = set, DstBinding = 0, DescriptorCount = 1, DescriptorType = VkDescriptorType.StorageBuffer, BufferInfo = &info }], []);

        _pipelineLayout = device.CreatePipelineLayout([_setLayout.Handle],
            [new VkPushConstantRange { StageFlags = VkShaderStageFlags.ComputeBit, Offset = new ByteSize32(0), Size = new ByteSize32(4) }], null);
        var module = ShaderModuleExtensions.FromSpirv(device, spv, null);
        _pipeline = device.CreateComputePipeline(default, new VkComputePipelineCreateInfo
        {
            Stage = Presets.ShaderStage.Compute(module),
            Layout = _pipelineLayout,
        }, null);
        device.DestroyShaderModule(module, null);

        _commandPool = device.CreateCommandPool(new VkCommandPoolCreateInfo { QueueFamilyIndex = queue.Family }, null);
        var cmd = device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo { CommandPool = _commandPool, Level = VkCommandBufferLevel.Primary });
        cmd.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
        cmd.BindPipeline(VkPipelineBindPoint.Compute, _pipeline);
        cmd.BindDescriptorSets(VkPipelineBindPoint.Compute, _pipelineLayout, 0, [set], []);
        cmd.PushConstants(_pipelineLayout, VkShaderStageFlags.ComputeBit, ByteSize.Zero, Bias);
        cmd.Dispatch(1, 1, 1);
        cmd.PipelineBarrier(VkPipelineStageFlags.ComputeShaderBit, VkPipelineStageFlags.TransferBit, VkDependencyFlags.None, [], [], []);
        cmd.CopyBuffer(_storage.VkBuffer, _readback.VkBuffer, _storage.BufferSize);
        cmd.End();
        _fence = device.CreateFence(VkFenceCreateFlags.None, null);
        queue.Submit([], [], [cmd], [], _fence);
    }

    /// <summary>Polls the fence (never blocks); true once <see cref="Result"/> holds the read-back values.</summary>
    public bool Poll()
    {
        if (Result is not null)
            return true;
        if (_device.WaitForFence(_fence, 0) != VkResult.Success)
            return false;
        using var map = _readback.Map();
        Result = map.AsSpan<uint>()[..Count].ToArray();
        return true;
    }

    public bool IsCorrect => Result is { } r && Enumerable.Range(0, Count).All(i => r[i] == (uint)i * 3 + Bias);

    public void Dispose()
    {
        _device.DestroyFence(_fence, null);
        _device.DestroyCommandPool(_commandPool, null);
        _device.DestroyPipeline(_pipeline, null);
        _device.DestroyPipelineLayout(_pipelineLayout, null);
        _pool.Dispose();
        _setLayout.Dispose();
        _readback.Dispose();
        _storage.Dispose();
    }
}
