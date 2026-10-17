using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace HelloMesh.Rendering;

/// <summary>A captured frame as tightly packed RGB8, top row first.</summary>
public sealed record Screenshot(int Width, int Height, byte[] Rgb);

/// <summary>
/// Desktop-only screenshot of one frame (<c>--screenshot</c> of HelloMesh.Desktop, through <see cref="HelloMeshApp.RequestScreenshot"/>):
/// copies the swapchain image into a host-visible buffer with <c>vkCmdCopyImageToBuffer</c> and reads it once the frame's fence
/// signalled. The browser host never arms it: its swapchain is not copyable and a readback lands a frame later there (the e2e test
/// reads pixels with <c>window.__brutal.readPixel</c> instead).
/// </summary>
public sealed class ScreenshotCapture(DeviceEx device) : IFrameCapture
{
    private BufferEx _buffer;
    private bool _hasBuffer;
    private VkExtent2D _extent;
    private VkFormat _format;

    /// <summary>The captured frame, once <see cref="IFrameCapture.Complete"/> ran.</summary>
    public Screenshot? Result { get; private set; }

    /// <inheritdoc />
    public void Record(CommandBuffer cmd, in RenderTarget target)
    {
        _extent = target.Extent;
        _format = target.ColorFormat;
        Dispose();
        _buffer = device.CreateBuffer(new BufferEx.CreateInfo
        {
            Name = "screenshot",
            BufferSize = ByteSize.Of(_extent.Width * _extent.Height * 4),
            BufferUsage = VkBufferUsageFlags.TransferDstBit,
            AllocPreference = MemoryPreference.PreferCpu,
            AllocRequiredProperties = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,
        });
        _hasBuffer = true;
        var image = target.ColorImage;
        Barriers.Image(cmd, image, VkImageAspectFlags.ColorBit, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.TransferSrcOptimal,
            VkPipelineStageFlags2.ColorAttachmentOutputBit, VkAccessFlags2.ColorAttachmentWriteBit, VkPipelineStageFlags2.AllTransferBit, VkAccessFlags2.TransferReadBit);
        cmd.CopyImageToBuffer(image, VkImageLayout.TransferSrcOptimal, _buffer.VkBuffer,
        [
            new VkBufferImageCopy
            {
                ImageSubresource = new VkImageSubresourceLayers { AspectMask = VkImageAspectFlags.ColorBit, LayerCount = 1 },
                ImageExtent = new VkExtent3D(_extent.Width, _extent.Height, 1),
            },
        ]);
        Barriers.Image(cmd, image, VkImageAspectFlags.ColorBit, VkImageLayout.TransferSrcOptimal, VkImageLayout.PresentSrcKHR,
            VkPipelineStageFlags2.AllTransferBit, VkAccessFlags2.TransferReadBit, VkPipelineStageFlags2.None, VkAccessFlags2.None);
    }

    /// <inheritdoc />
    public void Complete()
    {
        using (var mapped = _buffer.Map())
        {
            var source = mapped.AsSpan();
            var swapRedBlue = _format is VkFormat.B8G8R8A8UNorm or VkFormat.B8G8R8A8SRGB;
            var rgb = new byte[_extent.Width * _extent.Height * 3];
            for (var pixel = 0; pixel < _extent.Width * _extent.Height; pixel++)
            {
                rgb[pixel * 3] = source[pixel * 4 + (swapRedBlue ? 2 : 0)];
                rgb[pixel * 3 + 1] = source[pixel * 4 + 1];
                rgb[pixel * 3 + 2] = source[pixel * 4 + (swapRedBlue ? 0 : 2)];
            }
            Result = new Screenshot(_extent.Width, _extent.Height, rgb);
        }
        Dispose();
    }

    /// <summary>Releases the readback buffer.</summary>
    public void Dispose()
    {
        if (_hasBuffer)
            _buffer.Dispose();
        _hasBuffer = false;
    }
}
