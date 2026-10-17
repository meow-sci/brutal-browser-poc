using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

internal sealed class ImageObject : IcdObject
{
    public override string VkTypeName => "VkImage";
    public VkFormat Format;
    public string GpuFormat = "";
    public VkImageType Type;
    public int Width, Height, Depth, MipLevels, ArrayLayers, Samples;
    public VkImageUsageFlags Usage;
    public int Gpu;
    /// <summary>Virtual swapchain image: resolves to the canvas texture of the frame being replayed.</summary>
    public SwapchainObject? Swapchain;
    public int SwapchainIndex;

    public override int GpuHandle => Gpu;
    public bool IsSwapchainImage => Swapchain is not null;

    /// <summary>The texture id used in copy records (the reserved canvas-texture id for swapchain images).</summary>
    public int CopyHandle => IsSwapchainImage ? ReservedHandles.CanvasTexture : Gpu;
}

internal sealed class ImageViewObject : IcdObject
{
    public override string VkTypeName => "VkImageView";
    public ImageObject Image = null!;
    public VkFormat Format;
    public VkImageViewType ViewType;
    public VkImageAspectFlags Aspect;
    public int BaseMip, MipCount, BaseLayer, LayerCount;
    /// <summary>View used when bound as a texture (aspect as requested).</summary>
    public int Gpu;
    /// <summary>View used as a render attachment (aspect "all"; equals <see cref="Gpu"/> unless a depth/stencil aspect was selected).</summary>
    public int AttachmentGpu;
    /// <summary>Extent of the base mip level (render-pass scissor clamping).</summary>
    public int Width, Height;

    public override int GpuHandle => Gpu;
}

internal sealed class SamplerObject : IcdObject
{
    public override string VkTypeName => "VkSampler";
    public int Gpu;
    public bool Comparison;
    public bool Filtering;
    public override int GpuHandle => Gpu;
}
