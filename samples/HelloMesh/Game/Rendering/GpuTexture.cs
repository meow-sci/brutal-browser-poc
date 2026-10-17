using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace HelloMesh.Rendering;

/// <summary>An RGBA8 2D texture with its view and a linear/repeat sampler, uploaded through a staging buffer.</summary>
public sealed class GpuTexture : IDisposable
{
    private const int RowAlignment = 256;
    private const VkFormat Format = VkFormat.R8G8B8A8UNorm;

    private readonly Device _device;
    private readonly ImageEx _image;
    private readonly ImageViewEx _view;

    public VkImageView View => _view.VkImageView;
    public VkSampler Sampler { get; }

    private GpuTexture(Device device, ImageEx image, ImageViewEx view, VkSampler sampler)
    {
        _device = device;
        _image = image;
        _view = view;
        Sampler = sampler;
    }

    /// <summary>A 1x1 opaque white texture for untextured materials.</summary>
    public static GpuTexture UploadWhite(DeviceEx device, StagingPool staging, CommandBuffer cmd) =>
        Upload(device, staging, cmd, 1, 1, [255, 255, 255, 255]);

    /// <summary>
    /// Creates the texture and records the copy into <paramref name="cmd"/>; the caller submits it and waits. Staging rows are
    /// padded to 256 bytes (the browser backend requires it; <c>bufferRowLength</c> carries the padded pitch in texels).
    /// </summary>
    public static GpuTexture Upload(DeviceEx device, StagingPool staging, CommandBuffer cmd, int width, int height, ReadOnlySpan<byte> rgba)
    {
        var rowBytes = width * 4;
        var pitch = height > 1 ? (rowBytes + RowAlignment - 1) / RowAlignment * RowAlignment : rowBytes;
        var stagingBuffer = staging.AddStagingBuffer(ByteSize.Of(pitch * height));
        using (var mapped = stagingBuffer.Map())
        {
            var destination = mapped.AsSpan();
            for (var y = 0; y < height; y++)
                rgba.Slice(y * rowBytes, rowBytes).CopyTo(destination[(y * pitch)..]);
        }

        var image = device.CreateImage(new ImageEx.CreateInfo
        {
            Name = "texture",
            ImageType = VkImageType._2D,
            ImageFormat = Format,
            ImageExtent = new VkExtent3D(width, height, 1),
            ImageMipLevels = 1,
            ImageArrayLayers = 1,
            ImageSamples = VkSampleCountFlags._1Bit,
            ImageTiling = VkImageTiling.Optimal,
            ImageUsage = VkImageUsageFlags.SampledBit | VkImageUsageFlags.TransferDstBit,
            ImageSharingMode = VkSharingMode.Exclusive,
            AllocPreference = MemoryPreference.PreferGpu,
            AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
        });
        var view = image.CreateImageView(VkImageViewType._2D, Subresources.Single(VkImageAspectFlags.ColorBit), null);
        var sampler = device.CreateSampler(new VkSamplerCreateInfo
        {
            MagFilter = VkFilter.Linear,
            MinFilter = VkFilter.Linear,
            MipmapMode = VkSamplerMipmapMode.Nearest,
            AddressModeU = VkSamplerAddressMode.Repeat,
            AddressModeV = VkSamplerAddressMode.Repeat,
            AddressModeW = VkSamplerAddressMode.Repeat,
            CompareOp = VkCompareOp.Never,
            MinLod = 0f,
            MaxLod = 0f,
            BorderColor = VkBorderColor.FloatOpaqueBlack,
        }, null);

        Barriers.Image(cmd, image.VkImage, VkImageAspectFlags.ColorBit, VkImageLayout.Undefined, VkImageLayout.TransferDstOptimal,
            VkPipelineStageFlags2.None, VkAccessFlags2.None, VkPipelineStageFlags2.AllTransferBit, VkAccessFlags2.TransferWriteBit);
        cmd.CopyBufferToImage(stagingBuffer.VkBuffer, image.VkImage, VkImageLayout.TransferDstOptimal,
        [
            new VkBufferImageCopy
            {
                BufferRowLength = pitch == rowBytes ? 0 : pitch / 4,
                ImageSubresource = new VkImageSubresourceLayers { AspectMask = VkImageAspectFlags.ColorBit, LayerCount = 1 },
                ImageExtent = new VkExtent3D(width, height, 1),
            },
        ]);
        Barriers.Image(cmd, image.VkImage, VkImageAspectFlags.ColorBit, VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal,
            VkPipelineStageFlags2.AllTransferBit, VkAccessFlags2.TransferWriteBit, VkPipelineStageFlags2.FragmentShaderBit, VkAccessFlags2.ShaderSampledReadBit);
        return new GpuTexture(device, image, view, sampler);
    }

    public void Dispose()
    {
        _device.DestroySampler(Sampler, null);
        _view.Dispose();
        _image.Dispose();
    }
}
