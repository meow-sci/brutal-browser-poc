using Brutal;
using Brutal.Vulkan.WebGpu;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace WebGpuIcd.Tests;

/// <summary>The WebGPU-only rules of the web profile (WebProfileLint).</summary>
public sealed unsafe class LintTests
{
    private readonly RecordingBridge _bridge = TestIcd.Reset();

    private static void Record(TestDevice dev, Action<CommandBuffer> record)
    {
        var cmd = dev.CommandBuffer(out var pool);
        cmd.Begin();
        record(cmd);
        cmd.End();
        dev.Device.DestroyCommandPool(pool, null);
    }

    [Fact]
    public void DynamicOffsetsMustBeMultiplesOf256()
    {
        using var dev = new TestDevice();
        var binding = new VkDescriptorSetLayoutBinding { Binding = 0, DescriptorType = VkDescriptorType.UniformBufferDynamic, DescriptorCount = 1, StageFlags = VkShaderStageFlags.VertexBit };
        using var layout = dev.Device.CreateDescriptorSetLayout(new DescriptorSetLayoutEx.CreateInfo { Bindings = new Span<VkDescriptorSetLayoutBinding>(&binding, 1) }, null);
        var size = new VkDescriptorPoolSize { Type = VkDescriptorType.UniformBufferDynamic, DescriptorCount = 1 };
        using var pool = dev.Device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo { MaxSets = 1, PoolSizes = new Span<VkDescriptorPoolSize>(&size, 1) }, null);
        var set = dev.Device.AllocateDescriptorSet(pool, layout);
        using var ubo = dev.HostBuffer(1024, VkBufferUsageFlags.UniformBufferBit);
        var info = new VkDescriptorBufferInfo { Buffer = ubo.VkBuffer, Range = ByteSize.Of<byte>(64) };
        dev.Device.UpdateDescriptorSets([new VkWriteDescriptorSet { DstSet = set, DstBinding = 0, DescriptorCount = 1, DescriptorType = VkDescriptorType.UniformBufferDynamic, BufferInfo = &info }], []);
        var pipelineLayout = dev.Device.CreatePipelineLayout([layout.Handle], ReadOnlySpan<VkPushConstantRange>.Empty, null);
        Record(dev, cmd => cmd.BindDescriptorSets(VkPipelineBindPoint.Graphics, pipelineLayout, 0, [set], [new ByteSize32(100)]));
        Assert.Contains("vkCmdBindDescriptorSets: dynamic offsets are multiples of 256", IcdTestHooks.Violations);
    }

    [Fact]
    public void VertexBufferOffsetsMustBeMultiplesOf4()
    {
        using var dev = new TestDevice();
        using var vb = dev.DeviceBuffer(64, VkBufferUsageFlags.VertexBufferBit);
        Record(dev, cmd =>
        {
            var buffer = vb.VkBuffer;
            var offset = (ByteSize64)2UL;
            Icd.vkCmdBindVertexBuffers(cmd.Handle, 0, 1, &buffer, &offset);
        });
        Assert.Contains("vkCmdBindVertexBuffers: buffer offsets and sizes are multiples of 4", IcdTestHooks.Violations);
    }

    [Fact]
    public void AtMostEightVertexBuffers()
    {
        using var dev = new TestDevice();
        using var vb = dev.DeviceBuffer(64, VkBufferUsageFlags.VertexBufferBit);
        Record(dev, cmd =>
        {
            var buffers = stackalloc VkBuffer[9];
            var offsets = stackalloc ByteSize64[9];
            for (var i = 0; i < 9; i++)
                buffers[i] = vb.VkBuffer;
            Icd.vkCmdBindVertexBuffers(cmd.Handle, 0, 9, buffers, offsets);
        });
        Assert.Contains("vkCmdBindVertexBuffers: at most 8 vertex buffers", IcdTestHooks.Violations);
    }

    [Fact]
    public void NegativeViewportHeightsAreDroppedWithAnError()
    {
        using var dev = new TestDevice();
        var cmd = dev.CommandBuffer(out var pool);
        cmd.Begin();
        cmd.SetViewport(0, [new VkViewport { Y = 480, Width = 640, Height = -480, MaxDepth = 1 }]);
        cmd.End();
        var cb = HandleTable.Get<CommandBufferObject>(cmd.Handle.VkHandle)!;
        Assert.Equal(0, cb.Stream.WordLength);
        Assert.Contains("vkCmdSetViewport: positive viewport height", IcdTestHooks.Violations);
        Assert.Contains(TestIcd.Errors(), l => l.Contains("positive viewport height"));
        dev.Device.DestroyCommandPool(pool, null);
    }

    [Fact]
    public void DontCareStoreOfAnImageThatCanBeReadLaterIsStoredWithAWarning()
    {
        using var dev = new TestDevice();
        using var image = dev.Device.CreateImage(new ImageEx.CreateInfo
        {
            Name = "rt", ImageType = VkImageType._2D, ImageFormat = VkFormat.R8G8B8A8UNorm, ImageExtent = new VkExtent3D(16, 16, 1), ImageMipLevels = 1,
            ImageArrayLayers = 1, ImageSamples = VkSampleCountFlags._1Bit, ImageTiling = VkImageTiling.Optimal,
            ImageUsage = VkImageUsageFlags.ColorAttachmentBit | VkImageUsageFlags.SampledBit,
            AllocPreference = MemoryPreference.PreferGpu, AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
        });
        using var view = image.CreateImageView(VkImageViewType._2D, Subresources.Full(VkImageAspectFlags.ColorBit), null);
        var cmd = dev.CommandBuffer(out var pool);
        cmd.Begin();
        var color = new VkRenderingAttachmentInfo { ImageView = view.VkImageView, LoadOp = VkAttachmentLoadOp.Clear, StoreOp = VkAttachmentStoreOp.DontCare };
        cmd.BeginRendering(new VkRenderingInfo { RenderArea = new VkRect2D(new VkExtent2D(16, 16)), LayerCount = 1, ColorAttachmentCount = 1, ColorAttachments = &color });
        cmd.EndRendering();
        cmd.End();
        var record = IcdTestHooks.Decode(HandleTable.Get<CommandBufferObject>(cmd.Handle.VkHandle)!.Stream.Words)[0];
        Assert.Equal(Op.BeginRenderPass, record.Op);
        Assert.Equal(StreamCodes.StoreStore, record.Payload[11]);
        Assert.Contains("vkCmdBeginRendering: storeOp STORE for attachments read later", IcdTestHooks.Violations);
        Assert.Empty(TestIcd.Errors()); // a warning, not an error
        dev.Device.DestroyCommandPool(pool, null);
    }

    [Fact]
    public void OverlappingBufferBindsAreReportedAsAliasing()
    {
        using var dev = new TestDevice();
        var info = new VkBufferCreateInfo { Size = (ByteSize64)64UL, Usage = VkBufferUsageFlags.UniformBufferBit };
        VkBuffer a, b;
        Icd.vkCreateBuffer(dev.Device.Handle, &info, null, &a);
        Icd.vkCreateBuffer(dev.Device.Handle, &info, null, &b);
        var alloc = new VkMemoryAllocateInfo { AllocationSize = (ByteSize64)128UL, MemoryTypeIndex = 0 };
        VkDeviceMemory memory;
        Icd.vkAllocateMemory(dev.Device.Handle, &alloc, null, &memory);
        Assert.Equal(VkResult.Success, Icd.vkBindBufferMemory(dev.Device.Handle, a, memory, (ByteSize64)0UL));
        Assert.Equal(VkResult.Success, Icd.vkBindBufferMemory(dev.Device.Handle, b, memory, (ByteSize64)32UL));
        Assert.Contains("vkBindBufferMemory: memory aliasing", IcdTestHooks.Violations);
        Icd.vkDestroyBuffer(dev.Device.Handle, a, null);
        Icd.vkDestroyBuffer(dev.Device.Handle, b, null);
        Icd.vkFreeMemory(dev.Device.Handle, memory, null);
    }

    [Fact]
    public void PushConstantsAndBindGroupsHaveWebGpuLimits()
    {
        using var dev = new TestDevice();
        var big = new VkPushConstantRange { StageFlags = VkShaderStageFlags.VertexBit, Offset = new ByteSize32(0), Size = new ByteSize32(256) };
        dev.Device.DestroyPipelineLayout(dev.Device.CreatePipelineLayout(ReadOnlySpan<VkDescriptorSetLayout>.Empty, [big], null), null);
        Assert.Contains("vkCreatePipelineLayout: push constants are at most 128 bytes", IcdTestHooks.Violations);

        var binding = new VkDescriptorSetLayoutBinding { Binding = 0, DescriptorType = VkDescriptorType.UniformBuffer, DescriptorCount = 1, StageFlags = VkShaderStageFlags.VertexBit };
        using var layout = dev.Device.CreateDescriptorSetLayout(new DescriptorSetLayoutEx.CreateInfo { Bindings = new Span<VkDescriptorSetLayoutBinding>(&binding, 1) }, null);
        var small = new VkPushConstantRange { StageFlags = VkShaderStageFlags.VertexBit, Offset = new ByteSize32(0), Size = new ByteSize32(16) };
        dev.Device.DestroyPipelineLayout(dev.Device.CreatePipelineLayout([layout.Handle, layout.Handle, layout.Handle, layout.Handle], [small], null), null);
        Assert.Contains(IcdTestHooks.Violations, v => v.StartsWith("vkCreatePipelineLayout: at most 4 bind groups", StringComparison.Ordinal));
    }

    [Fact]
    public void Copies256ByteRowsFromDeviceLocalBuffers()
    {
        using var dev = new TestDevice();
        using var src = dev.DeviceBuffer(4096, VkBufferUsageFlags.TransferSrcBit);
        using var image = dev.Device.CreateImage(new ImageEx.CreateInfo
        {
            Name = "t", ImageType = VkImageType._2D, ImageFormat = VkFormat.R8G8B8A8UNorm, ImageExtent = new VkExtent3D(10, 10, 1), ImageMipLevels = 1,
            ImageArrayLayers = 1, ImageSamples = VkSampleCountFlags._1Bit, ImageTiling = VkImageTiling.Optimal, ImageUsage = VkImageUsageFlags.TransferDstBit,
            AllocPreference = MemoryPreference.PreferGpu, AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
        });
        Record(dev, cmd => cmd.CopyBufferToImage(src.VkBuffer, image.VkImage, VkImageLayout.TransferDstOptimal,
            [new VkBufferImageCopy { ImageSubresource = new VkImageSubresourceLayers { AspectMask = VkImageAspectFlags.ColorBit, LayerCount = 1 }, ImageExtent = new VkExtent3D(10, 10, 1) }]));
        Assert.Contains("vkCmdCopyBufferToImage: buffer-to-image copies from device-local buffers use 256-byte rows", IcdTestHooks.Violations);
    }
}
