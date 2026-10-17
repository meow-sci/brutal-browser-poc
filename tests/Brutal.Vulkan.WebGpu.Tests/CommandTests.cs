using Brutal;
using Brutal.Vulkan.WebGpu;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace WebGpuIcd.Tests;

/// <summary>Command encodings beyond the smoke scene: legacy render passes, secondaries, labels, strips, readback copies.</summary>
public sealed unsafe class CommandTests
{
    private readonly RecordingBridge _bridge = TestIcd.Reset();

    private static List<(Op Op, uint[] Payload)> Recorded(CommandBuffer cmd) =>
        IcdTestHooks.Decode(HandleTable.Get<CommandBufferObject>(cmd.Handle.VkHandle)!.Stream.Words);

    private static (ImageEx, ImageViewEx) Target(TestDevice dev, VkFormat format, VkImageUsageFlags usage)
    {
        var image = dev.Device.CreateImage(new ImageEx.CreateInfo
        {
            Name = "rt", ImageType = VkImageType._2D, ImageFormat = format, ImageExtent = new VkExtent3D(64, 32, 1), ImageMipLevels = 1,
            ImageArrayLayers = 1, ImageSamples = VkSampleCountFlags._1Bit, ImageTiling = VkImageTiling.Optimal, ImageUsage = usage,
            AllocPreference = MemoryPreference.PreferGpu, AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
        });
        var aspect = Formats.HasDepth(format) ? VkImageAspectFlags.DepthBit : VkImageAspectFlags.ColorBit;
        return (image, image.CreateImageView(VkImageViewType._2D, Subresources.Full(aspect), null));
    }

    [Fact]
    public void LegacyRenderPassesBecomeTheSameRecordAsDynamicRendering()
    {
        using var dev = new TestDevice();
        var (color, colorView) = Target(dev, VkFormat.R8G8B8A8UNorm, VkImageUsageFlags.ColorAttachmentBit | VkImageUsageFlags.SampledBit);
        var (depth, depthView) = Target(dev, VkFormat.D24UNormS8UInt, VkImageUsageFlags.DepthStencilAttachmentBit);
        // BRUTAL's helper: one subpass, colour finalLayout SHADER_READ_ONLY (storeOp DONT_CARE when not sampled later)
        var renderPass = dev.Device.CreateDefaultRenderPass(true, true, VkSampleCountFlags._1Bit, VkFormat.R8G8B8A8UNorm, VkFormat.D24UNormS8UInt, VkFormat.Undefined, null);
        var views = stackalloc VkImageView[2] { colorView.VkImageView, depthView.VkImageView };
        var framebuffer = dev.Device.CreateFramebuffer(new VkFramebufferCreateInfo { RenderPass = renderPass, AttachmentCount = 2, Attachments = views, Width = 64, Height = 32, Layers = 1 }, null);
        var clears = stackalloc VkClearValue[2];
        clears[0] = new VkClearColorValue { Float32 = new Brutal.Numerics.float4(1, 0, 0, 1) };
        clears[1] = new VkClearDepthStencilValue { Depth = 1, Stencil = 7 };
        var cmd = dev.CommandBuffer(out var pool);
        cmd.Begin();
        cmd.BeginRenderPass(new VkRenderPassBeginInfo { RenderPass = renderPass, Framebuffer = framebuffer, RenderArea = new VkRect2D(new VkExtent2D(64, 32)), ClearValueCount = 2, ClearValues = clears }, VkSubpassContents.Inline);
        cmd.EndRenderPass();
        cmd.End();

        var records = Recorded(cmd);
        Assert.Equal([Op.BeginRenderPass, Op.EndRenderPass], records.Select(r => r.Op));
        var p = records[0].Payload;
        Assert.Equal([1u, StreamCodes.RenderPassHasDepthStencil, 0u, 0u, 64u, 32u, 64u, 32u], p[..8]);
        Assert.Equal((uint)HandleTable.Get<ImageViewObject>(colorView.VkImageView.VkHandle)!.AttachmentGpu, p[8]);
        Assert.Equal(1f, Streams.F(p[13]));
        // the helper's colour storeOp is DONT_CARE (finalLayout SHADER_READ_ONLY): stored anyway, with a lint warning
        Assert.Equal(StreamCodes.StoreStore, p[11]);
        Assert.Contains("vkCmdBeginRenderPass: storeOp STORE for attachments read later", IcdTestHooks.Violations);
        // D24S8 viewed through the DEPTH aspect: the attachment uses an extra "all aspects" view, stencil cleared to 7
        var dsView = HandleTable.Get<ImageViewObject>(depthView.VkImageView.VkHandle)!;
        Assert.NotEqual(dsView.Gpu, dsView.AttachmentGpu);
        Assert.Equal((uint)dsView.AttachmentGpu, p[17]);
        Assert.Equal(7u, p[23]);
        Assert.NotEqual(StreamCodes.LoadOmit, p[21]);

        dev.Device.DestroyCommandPool(pool, null);
        dev.Device.DestroyFramebuffer(framebuffer, null);
        dev.Device.DestroyRenderPass(renderPass, null);
        colorView.Dispose(); color.Dispose(); depthView.Dispose(); depth.Dispose();
        Assert.Empty(TestIcd.Errors());
    }

    [Fact]
    public void SecondaryCommandBuffersAreInlinedAndLabelsBecomeDebugGroups()
    {
        using var dev = new TestDevice();
        var pool = dev.Device.CreateCommandPool(new VkCommandPoolCreateInfo { QueueFamilyIndex = 0 }, null);
        var secondary = dev.Device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo { CommandPool = pool, Level = VkCommandBufferLevel.Secondary });
        var primary = dev.Device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo { CommandPool = pool, Level = VkCommandBufferLevel.Primary });
        secondary.Begin();
        secondary.Draw(6, 2, 0, 1);
        secondary.End();
        primary.Begin();
        using (primary.DebugScope("scene"))
            primary.ExecuteCommands([secondary]);
        primary.End();

        var records = Recorded(primary);
        Assert.Equal([Op.PushDebugGroup, Op.Draw, Op.PopDebugGroup], records.Select(r => r.Op));
        Assert.Equal("scene"u8.ToArray(), System.Runtime.InteropServices.MemoryMarshal.AsBytes(records[0].Payload.AsSpan(1)).Slice(0, (int)records[0].Payload[0]).ToArray());
        Assert.Equal([6u, 2u, 0u, 1u], records[1].Payload);
        dev.Device.DestroyCommandPool(pool, null);
    }

    [Fact]
    public void ImageToHostBufferCopiesAreReadBack()
    {
        using var dev = new TestDevice();
        var (image, view) = Target(dev, VkFormat.R8G8B8A8UNorm, VkImageUsageFlags.TransferSrcBit | VkImageUsageFlags.ColorAttachmentBit);
        using var readback = dev.HostBuffer(64 * 4 * 32, VkBufferUsageFlags.TransferDstBit);
        var cmd = dev.CommandBuffer(out var pool);
        cmd.Begin();
        cmd.CopyImageToBuffer(image.VkImage, VkImageLayout.TransferSrcOptimal, readback.VkBuffer,
            [new VkBufferImageCopy { ImageSubresource = new VkImageSubresourceLayers { AspectMask = VkImageAspectFlags.ColorBit, LayerCount = 1 }, ImageExtent = new VkExtent3D(64, 32, 1) }]);
        cmd.End();
        var fence = dev.Device.CreateFence(VkFenceCreateFlags.None, null);
        dev.Queue.Submit([], [], [cmd], [], fence);
        var records = Streams.Decode(_bridge.Streams.Single());
        Assert.Equal([Op.CopyTextureToBuffer, Op.Readback, Op.End], records.Select(r => r.Op));
        Assert.Equal(256u, records[0].Payload[8]);                    // 64 texels * 4 bytes: a 256-byte row
        Assert.Equal(64u * 4 * 32, records[1].Payload[2]);
        Assert.Equal(VkResult.Timeout, dev.Device.WaitForFence(fence, 0));
        dev.Device.DestroyFence(fence, null);
        dev.Device.DestroyCommandPool(pool, null);
        view.Dispose();
        image.Dispose();
    }

    [Fact]
    public void FillAndUpdateBufferAreEncodedInline()
    {
        using var dev = new TestDevice();
        using var buffer = dev.DeviceBuffer(256, VkBufferUsageFlags.StorageBufferBit | VkBufferUsageFlags.TransferDstBit);
        var cmd = dev.CommandBuffer(out var pool);
        cmd.Begin();
        cmd.FillBuffer(buffer.VkBuffer, ByteSize.Zero, VK.WHOLE_SIZE, 0);
        cmd.FillBuffer(buffer.VkBuffer, ByteSize.Of<byte>(16), ByteSize.Of<byte>(8), 0x01020304);
        var data = new uint[] { 1, 2, 3, 4 };
        fixed (uint* p = data)
            Icd.vkCmdUpdateBuffer(cmd.Handle, buffer.VkBuffer, (ByteSize64)32UL, (ByteSize64)16UL, p);
        cmd.End();
        var records = Recorded(cmd);
        Assert.Equal([Op.ClearBuffer, Op.UpdateBuffer, Op.UpdateBuffer], records.Select(r => r.Op));
        Assert.Equal(256u, records[0].Payload[2]);
        Assert.Equal([16u, 8u, 0x01020304u, 0x01020304u], records[1].Payload[1..]);
        Assert.Equal([32u, 16u, 1u, 2u, 3u, 4u], records[2].Payload[1..]);
        dev.Device.DestroyCommandPool(pool, null);
    }

    [Fact]
    public void BarriersAndEventsAreNoOps()
    {
        using var dev = new TestDevice();
        var cmd = dev.CommandBuffer(out var pool);
        cmd.Begin();
        var dependency = new VkDependencyInfo();
        cmd.PipelineBarrier2(dependency);
        cmd.End();
        Assert.Equal([Op.Barrier], Recorded(cmd).Select(r => r.Op));
        dev.Device.DestroyCommandPool(pool, null);
    }
}
