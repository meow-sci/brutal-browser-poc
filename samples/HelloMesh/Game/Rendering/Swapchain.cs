using Brutal.GlfwApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace HelloMesh.Rendering;

/// <summary>FIFO swapchain with image views, per-image render-finished semaphores and a D32_SFLOAT depth buffer; can be rebuilt after a resize.</summary>
public sealed class Swapchain : IDisposable
{
    public const VkFormat DepthFormat = VkFormat.D32SFloat;

    private readonly VulkanContext _context;
    private readonly GlfwWindow _window;
    private ImageEx _depth;
    private ImageViewEx _depthView;

    public VkSwapchainKHR Handle { get; private set; }
    public VkFormat Format { get; private set; }
    public VkExtent2D Extent { get; private set; }
    public VkImage[] Images { get; private set; } = [];
    public VkImageView[] ImageViews { get; private set; } = [];

    /// <summary>One semaphore per swapchain image, signalled by the frame's submit and waited on by its present.</summary>
    public VkSemaphore[] RenderFinished { get; private set; } = [];

    /// <summary>True when the images can be copied out (needed for screenshots).</summary>
    public bool SupportsReadback { get; private set; }

    public VkImage DepthImage => _depth.VkImage;
    public VkImageView DepthView => _depthView.VkImageView;

    public Swapchain(VulkanContext context, GlfwWindow window)
    {
        _context = context;
        _window = window;
        Create(default);
    }

    /// <summary>Rebuilds the swapchain for the current window size. All frames in flight must have finished.</summary>
    public void Recreate()
    {
        _context.Device.WaitIdle(); // returns at once in the browser (the web ICD has no blocking waits)
        var old = Handle;
        DestroyDependents();
        Create(old);
        _context.Device.DestroySwapchainKHR(old, null);
    }

    private unsafe void Create(VkSwapchainKHR old)
    {
        var device = _context.Device;
        var capabilities = _context.PhysicalDevice.GetSurfaceCapabilitiesKHR(_context.Surface);
        var surfaceFormat = ChooseSurfaceFormat();
        var framebuffer = _window.FramebufferSize;
        var extent = capabilities.CurrentExtent.Width == -1
            ? new VkExtent2D(
                Math.Clamp(framebuffer.X, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
                Math.Clamp(framebuffer.Y, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height))
            : capabilities.CurrentExtent;
        var imageCount = capabilities.MinImageCount + 1;
        if (capabilities.MaxImageCount > 0)
            imageCount = Math.Min(imageCount, capabilities.MaxImageCount);
        SupportsReadback = capabilities.SupportedUsageFlags.HasFlag(VkImageUsageFlags.TransferSrcBit);

        Handle = device.CreateSwapchainKHR(new VkSwapchainCreateInfoKHR
        {
            Surface = _context.Surface,
            MinImageCount = imageCount,
            ImageFormat = surfaceFormat.Format,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = VkImageUsageFlags.ColorAttachmentBit | (SupportsReadback ? VkImageUsageFlags.TransferSrcBit : VkImageUsageFlags.None),
            ImageSharingMode = VkSharingMode.Exclusive,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = VkCompositeAlphaFlagsKHR.OpaqueBitKHR,
            PresentMode = VkPresentModeKHR.FifoKHR,
            Clipped = true,
            OldSwapchain = old,
        }, null);
        Format = surfaceFormat.Format;
        Extent = extent;

        Images = new VkImage[device.GetSwapchainImagesKHR_Count(Handle)];
        device.GetSwapchainImagesKHR_Write(Handle, Images);
        ImageViews = new VkImageView[Images.Length];
        RenderFinished = new VkSemaphore[Images.Length];
        for (var i = 0; i < Images.Length; i++)
        {
            ImageViews[i] = device.CreateImageView(new VkImageViewCreateInfo
            {
                Image = Images[i],
                ViewType = VkImageViewType._2D,
                Format = Format,
                SubresourceRange = Subresources.Single(VkImageAspectFlags.ColorBit),
            }, null);
            RenderFinished[i] = device.CreateSemaphore(new VkSemaphoreCreateInfo(), null);
        }

        _depth = device.CreateImage(new ImageEx.CreateInfo
        {
            Name = "depth",
            ImageType = VkImageType._2D,
            ImageFormat = DepthFormat,
            ImageExtent = new VkExtent3D(extent.Width, extent.Height, 1),
            ImageMipLevels = 1,
            ImageArrayLayers = 1,
            ImageSamples = VkSampleCountFlags._1Bit,
            ImageTiling = VkImageTiling.Optimal,
            ImageUsage = VkImageUsageFlags.DepthStencilAttachmentBit,
            ImageSharingMode = VkSharingMode.Exclusive,
            AllocPreference = MemoryPreference.PreferGpu,
            AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
        });
        _depthView = _depth.CreateImageView(VkImageViewType._2D, Subresources.Single(VkImageAspectFlags.DepthBit), null);
    }

    private VkSurfaceFormatKHR ChooseSurfaceFormat()
    {
        var formats = new VkSurfaceFormatKHR[_context.PhysicalDevice.GetSurfaceFormatsKHR_Count(_context.Surface)];
        _context.PhysicalDevice.GetSurfaceFormatsKHR_Write(_context.Surface, formats);
        foreach (var preferred in new[] { VkFormat.B8G8R8A8UNorm, VkFormat.R8G8B8A8UNorm })
            foreach (var format in formats)
                if (format.Format == preferred && format.ColorSpace == VkColorSpaceKHR.SRGBNonlinearKHR)
                    return format;
        return formats.Length > 0 ? formats[0] : throw new InvalidOperationException("The surface reports no formats.");
    }

    private void DestroyDependents()
    {
        var device = _context.Device;
        _depthView.Dispose();
        _depth.Dispose();
        foreach (var view in ImageViews)
            device.DestroyImageView(view, null);
        foreach (var semaphore in RenderFinished)
            device.DestroySemaphore(semaphore, null);
    }

    public void Dispose()
    {
        DestroyDependents();
        _context.Device.DestroySwapchainKHR(Handle, null);
    }
}
