using System.Text.Json;
using Brutal;
using Brutal.Vulkan.WebGpu;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace WebGpuIcd.Tests;

/// <summary>Edge cases: readback lifetimes, fence idioms, shader-written host memory, stand-in descriptors, copy rules.</summary>
public sealed unsafe class RobustnessTests
{
    private readonly RecordingBridge _bridge = TestIcd.Reset();

    private static (CommandBuffer Cmd, VkCommandPool Pool) Begin(TestDevice dev)
    {
        var cmd = dev.CommandBuffer(out var pool);
        cmd.Begin();
        return (cmd, pool);
    }

    [Fact]
    public void FreeingMemoryWhileAReadbackIsInFlightKeepsTheShadowUntilItLands()
    {
        using var dev = new TestDevice();
        using var src = dev.DeviceBuffer(64, VkBufferUsageFlags.TransferSrcBit);
        var dst = dev.HostBuffer(64, VkBufferUsageFlags.TransferDstBit);
        var (cmd, pool) = Begin(dev);
        cmd.CopyBuffer(src.VkBuffer, dst.VkBuffer, ByteSize.Of<byte>(64));
        cmd.End();
        dev.Queue.Submit([], [], [cmd], [], default);
        var memory = HandleTable.Get<DeviceMemoryObject>(dst.VkDeviceMemory.VkHandle)!;
        var ticket = Readbacks.Pending.Single();

        dst.Dispose(); // vkDestroyBuffer + vkFreeMemory before the data landed (vkDeviceWaitIdle cannot block)
        Assert.True(memory.Freed);
        Assert.True(memory.HostVisible); // JS will still write into the shadow: it must stay allocated

        *(int*)ticket.Status = 1;        // the mapAsync copy completed
        Readbacks.PollAll();
        Assert.False(memory.HostVisible);
        Assert.Empty(Readbacks.Pending);
        dev.Device.DestroyCommandPool(pool, null);
    }

    [Fact]
    public void AFenceOnlySubmitWaitsForEarlierReadbacksButFrameFencesDoNot()
    {
        using var dev = new TestDevice();
        using var src = dev.DeviceBuffer(64, VkBufferUsageFlags.TransferSrcBit);
        using var dst = dev.HostBuffer(64, VkBufferUsageFlags.TransferDstBit);
        var (cmd, pool) = Begin(dev);
        cmd.CopyBuffer(src.VkBuffer, dst.VkBuffer, ByteSize.Of<byte>(64));
        cmd.End();
        dev.Queue.Submit([], [], [cmd], [], default); // readback without a fence

        var other = dev.Device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo { CommandPool = pool, Level = VkCommandBufferLevel.Primary });
        other.Begin();
        other.Draw(3, 1, 0, 0);
        other.End();
        var frameFence = dev.Device.CreateFence(VkFenceCreateFlags.None, null);
        dev.Queue.Submit([], [], [other], [], frameFence);
        Assert.Equal(VkResult.Success, dev.Device.WaitForFence(frameFence, 0)); // no readback of its own: signalled at submit

        var idleFence = dev.Device.CreateFence(VkFenceCreateFlags.None, null);
        dev.Queue.Submit([], [], [], [], idleFence);                             // "fence after earlier work"
        Assert.Equal(VkResult.Timeout, dev.Device.WaitForFence(idleFence, 0));
        *(int*)Readbacks.Pending.Single().Status = 1;
        Assert.Equal(VkResult.Success, dev.Device.WaitForFence(idleFence, 0));
        dev.Device.DestroyFence(frameFence, null);
        dev.Device.DestroyFence(idleFence, null);
        dev.Device.DestroyCommandPool(pool, null);
    }

    [Fact]
    public void ShaderWrittenHostVisibleStorageBuffersAreReadBackAndNotOverwritten()
    {
        using var dev = new TestDevice();
        var binding = new VkDescriptorSetLayoutBinding { Binding = 0, DescriptorType = VkDescriptorType.StorageBuffer, DescriptorCount = 1, StageFlags = VkShaderStageFlags.ComputeBit };
        using var layout = dev.Device.CreateDescriptorSetLayout(new DescriptorSetLayoutEx.CreateInfo { Bindings = new Span<VkDescriptorSetLayoutBinding>(&binding, 1) }, null);
        var size = new VkDescriptorPoolSize { Type = VkDescriptorType.StorageBuffer, DescriptorCount = 1 };
        using var descriptorPool = dev.Device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo { MaxSets = 1, PoolSizes = new Span<VkDescriptorPoolSize>(&size, 1) }, null);
        var set = dev.Device.AllocateDescriptorSet(descriptorPool, layout);
        using var storage = dev.HostBuffer(256, VkBufferUsageFlags.StorageBufferBit);
        var map = storage.Map(); // persistently mapped: normally uploaded at every submit
        var info = new VkDescriptorBufferInfo { Buffer = storage.VkBuffer, Range = VK.WHOLE_SIZE };
        dev.Device.UpdateDescriptorSets([new VkWriteDescriptorSet { DstSet = set, DstBinding = 0, DescriptorCount = 1, DescriptorType = VkDescriptorType.StorageBuffer, BufferInfo = &info }], []);
        var pipelineLayout = dev.Device.CreatePipelineLayout([layout.Handle], ReadOnlySpan<VkPushConstantRange>.Empty, null);
        var (cmd, pool) = Begin(dev);
        cmd.BindDescriptorSets(VkPipelineBindPoint.Compute, pipelineLayout, 0, [set], []);
        cmd.Dispatch(4, 1, 1);
        cmd.End();
        dev.Queue.Submit([], [], [cmd], [], default);
        var first = Streams.Decode(_bridge.Streams[^1]);
        Assert.Equal(Op.WriteBuffer, first[0].Op);       // the CPU's data goes up first
        Assert.Equal(Op.Readback, first[^2].Op);         // the shader's results come back
        Assert.Equal(256u, first[^2].Payload[2]);

        dev.Queue.Submit([], [], [cmd], [], default);    // results not landed yet: the stale shadow must not be uploaded over them
        Assert.DoesNotContain(Streams.Decode(_bridge.Streams[^1]), r => r.Op == Op.WriteBuffer);
        map.Dispose();
        dev.Device.DestroyCommandPool(pool, null);
    }

    [Fact]
    public void UnwrittenDescriptorsGetStandInsInsteadOfFailingTheBindGroup()
    {
        using var dev = new TestDevice();
        Span<VkDescriptorSetLayoutBinding> bindings =
        [
            new() { Binding = 0, DescriptorType = VkDescriptorType.UniformBuffer, DescriptorCount = 1, StageFlags = VkShaderStageFlags.VertexBit },
            new() { Binding = 1, DescriptorType = VkDescriptorType.CombinedImageSampler, DescriptorCount = 1, StageFlags = VkShaderStageFlags.FragmentBit },
        ];
        using var layout = dev.Device.CreateDescriptorSetLayout(new DescriptorSetLayoutEx.CreateInfo { Bindings = bindings }, null);
        Span<VkDescriptorPoolSize> sizes = [new() { Type = VkDescriptorType.UniformBuffer, DescriptorCount = 1 }, new() { Type = VkDescriptorType.CombinedImageSampler, DescriptorCount = 1 }];
        using var descriptorPool = dev.Device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo { MaxSets = 1, PoolSizes = sizes }, null);
        var set = dev.Device.AllocateDescriptorSet(descriptorPool, layout);
        var pipelineLayout = dev.Device.CreatePipelineLayout([layout.Handle], ReadOnlySpan<VkPushConstantRange>.Empty, null);
        var (cmd, pool) = Begin(dev);
        cmd.BindDescriptorSets(VkPipelineBindPoint.Graphics, pipelineLayout, 0, [set], []);
        cmd.End();
        var group = JsonDocument.Parse(_bridge.Named("createBindGroup").Single().Json!).RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal([0, 1, 501], group.Select(e => e.GetProperty("binding").GetInt32()));
        Assert.Empty(TestIcd.Errors());
        Assert.Contains(TestIcd.Log, l => l.StartsWith("WARN") && l.Contains("never written"));
        dev.Device.DestroyCommandPool(pool, null);
    }

    [Fact]
    public void ReadOnlyDepthAttachmentsAreFlaggedAndTakeNoDepthOps()
    {
        using var dev = new TestDevice();
        using var depth = dev.Device.CreateImage(new ImageEx.CreateInfo
        {
            Name = "depth", ImageType = VkImageType._2D, ImageFormat = VkFormat.D32SFloat, ImageExtent = new VkExtent3D(8, 8, 1), ImageMipLevels = 1,
            ImageArrayLayers = 1, ImageSamples = VkSampleCountFlags._1Bit, ImageTiling = VkImageTiling.Optimal,
            ImageUsage = VkImageUsageFlags.DepthStencilAttachmentBit | VkImageUsageFlags.SampledBit,
            AllocPreference = MemoryPreference.PreferGpu, AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
        });
        using var view = depth.CreateImageView(VkImageViewType._2D, Subresources.Full(VkImageAspectFlags.DepthBit), null);
        var (cmd, pool) = Begin(dev);
        var attachment = new VkRenderingAttachmentInfo { ImageView = view.VkImageView, ImageLayout = VkImageLayout.DepthReadOnlyOptimal, LoadOp = VkAttachmentLoadOp.Load, StoreOp = VkAttachmentStoreOp.Store };
        cmd.BeginRendering(new VkRenderingInfo { RenderArea = new VkRect2D(new VkExtent2D(8, 8)), LayerCount = 1, DepthAttachment = &attachment });
        cmd.EndRendering();
        cmd.End();
        var pass = IcdTestHooks.Decode(HandleTable.Get<CommandBufferObject>(cmd.Handle.VkHandle)!.Stream.Words)[0].Payload;
        Assert.Equal(StreamCodes.LoadOmit, pass[9]);
        Assert.Equal(StreamCodes.StoreOmit, pass[10]);
        Assert.Equal(StreamCodes.DepthReadOnly, pass[15]);
        dev.Device.DestroyCommandPool(pool, null);
    }

    [Fact]
    public void UnalignedBufferCopiesArePaddedOnlyAtBufferTails()
    {
        using var dev = new TestDevice();
        using var src = dev.DeviceBuffer(10, VkBufferUsageFlags.TransferSrcBit);
        using var dst = dev.DeviceBuffer(10, VkBufferUsageFlags.TransferDstBit);
        using var big = dev.DeviceBuffer(64, VkBufferUsageFlags.TransferDstBit);
        var (cmd, pool) = Begin(dev);
        cmd.CopyBuffer(src.VkBuffer, dst.VkBuffer, ByteSize.Of<byte>(10));    // whole buffers: padded to 12
        cmd.CopyBuffer(src.VkBuffer, big.VkBuffer, ByteSize.Of<byte>(10));    // would clobber big[10..12): rejected
        cmd.End();
        var records = IcdTestHooks.Decode(HandleTable.Get<CommandBufferObject>(cmd.Handle.VkHandle)!.Stream.Words);
        Assert.Single(records);
        Assert.Equal(12u, records[0].Payload[4]);
        Assert.Contains("vkCmdCopyBuffer: buffer offsets and sizes are multiples of 4", IcdTestHooks.Violations);
        dev.Device.DestroyCommandPool(pool, null);
    }

    [Fact]
    public void ResumedRenderingLoadsAndSuspendedRenderingStores()
    {
        using var dev = new TestDevice();
        using var color = dev.Device.CreateImage(new ImageEx.CreateInfo
        {
            Name = "c", ImageType = VkImageType._2D, ImageFormat = VkFormat.R8G8B8A8UNorm, ImageExtent = new VkExtent3D(8, 8, 1), ImageMipLevels = 1,
            ImageArrayLayers = 1, ImageSamples = VkSampleCountFlags._1Bit, ImageTiling = VkImageTiling.Optimal, ImageUsage = VkImageUsageFlags.ColorAttachmentBit,
            AllocPreference = MemoryPreference.PreferGpu, AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
        });
        using var view = color.CreateImageView(VkImageViewType._2D, Subresources.Full(VkImageAspectFlags.ColorBit), null);
        var (cmd, pool) = Begin(dev);
        var attachment = new VkRenderingAttachmentInfo { ImageView = view.VkImageView, LoadOp = VkAttachmentLoadOp.Clear, StoreOp = VkAttachmentStoreOp.DontCare };
        cmd.BeginRendering(new VkRenderingInfo { Flags = VkRenderingFlags.ResumingBit | VkRenderingFlags.SuspendingBit, RenderArea = new VkRect2D(new VkExtent2D(8, 8)), LayerCount = 1, ColorAttachmentCount = 1, ColorAttachments = &attachment });
        cmd.EndRendering();
        cmd.End();
        var pass = IcdTestHooks.Decode(HandleTable.Get<CommandBufferObject>(cmd.Handle.VkHandle)!.Stream.Words)[0].Payload;
        Assert.Equal(StreamCodes.LoadLoad, pass[10]);
        Assert.Equal(StreamCodes.StoreStore, pass[11]);
        dev.Device.DestroyCommandPool(pool, null);
    }
}
