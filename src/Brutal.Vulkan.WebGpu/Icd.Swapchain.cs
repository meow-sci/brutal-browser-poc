using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// Surface and swapchain over the page's canvas: the swapchain configures the canvas context and hands out N virtual images;
/// vkAcquireNextImageKHR caches the frame's canvas texture (views of swapchain images resolve to it at replay) and returns
/// VK_ERROR_OUT_OF_DATE_KHR after a canvas resize; vkQueuePresentKHR is the end-of-frame marker (the browser presents when
/// the frame callback returns).
/// </summary>
internal static unsafe partial class Icd
{
    public const int CanvasSurfaceHandle = 1;

    public static VkResult vkCreateWebSurfaceBRUTAL(VkInstance instance, VkWebSurfaceCreateInfoBRUTAL* pCreateInfo, VkAllocationCallbacks* pAllocator,
        VkSurfaceKHR* pSurface)
    {
        const string fn = "vkCreateWebSurfaceBRUTAL";
        var inst = HandleTable.Lookup<InstanceObject>(instance.VkHandle, fn);
        if (inst is null)
            return VkResult.ErrorInitializationFailed;
        if (!inst.Extensions.Contains(Profile.WebSurfaceExtension))
            IcdLog.WarnOnce("websurface-ext", $"{fn}: {Profile.WebSurfaceExtension} was not enabled on the instance");
        if (pCreateInfo->CanvasHandle != CanvasSurfaceHandle)
        {
            IcdLog.Error($"{fn}: canvas handle {pCreateInfo->CanvasHandle} (the page has one canvas, handle {CanvasSurfaceHandle})");
            return VkResult.ErrorSurfaceLostKHR;
        }
        var surface = new SurfaceObject { CanvasHandle = pCreateInfo->CanvasHandle };
        HandleTable.Add(surface);
        *pSurface = new VkSurfaceKHR(surface.Handle);
        return VkResult.Success;
    }

    /// <summary>The <c>Brutal.Browser.BrowserSurfaces.CreateSurface</c> hook (used by the browser <c>Glfw.CreateWindowSurface</c>).</summary>
    internal static int CreateSurfaceForCanvas(nint instance, nint canvasHandle, out ulong surface)
    {
        var info = new VkWebSurfaceCreateInfoBRUTAL(canvasHandle);
        VkSurfaceKHR result;
        var r = vkCreateWebSurfaceBRUTAL(new VkInstance(instance), &info, null, &result);
        surface = (ulong)result.VkHandle;
        return (int)r;
    }

    public static void vkDestroySurfaceKHR(VkInstance instance, VkSurfaceKHR surface, VkAllocationCallbacks* pAllocator)
    {
        if (HandleTable.Get<SurfaceObject>(surface.VkHandle) is { } s)
            HandleTable.Remove(s.Handle);
    }

    public static VkResult vkGetPhysicalDeviceSurfaceSupportKHR(VkPhysicalDevice physicalDevice, int queueFamilyIndex, VkSurfaceKHR surface, Bool32* pSupported)
    {
        *pSupported = queueFamilyIndex == 0 && HandleTable.Get<SurfaceObject>(surface.VkHandle) is not null;
        return VkResult.Success;
    }

    private static (int Width, int Height) CanvasSize()
    {
        var packed = WebIcd.Bridge.GetCanvasSize();
        return (packed >>> 16, packed & 0xFFFF);
    }

    public static VkResult vkGetPhysicalDeviceSurfaceCapabilitiesKHR(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface, VkSurfaceCapabilitiesKHR* pCaps)
    {
        if (HandleTable.Lookup<SurfaceObject>(surface.VkHandle, "vkGetPhysicalDeviceSurfaceCapabilitiesKHR") is null)
            return VkResult.ErrorSurfaceLostKHR;
        var (w, h) = CanvasSize();
        pCaps->MinImageCount = 2;
        pCaps->MaxImageCount = Profile.SwapchainImageCount;
        pCaps->CurrentExtent = new VkExtent2D(w, h);
        // the canvas drawing buffer decides the size: like a fixed-size window surface, imageExtent must equal currentExtent
        pCaps->MinImageExtent = new VkExtent2D(w, h);
        pCaps->MaxImageExtent = new VkExtent2D(w, h);
        pCaps->MaxImageArrayLayers = 1;
        pCaps->SupportedTransforms = VkSurfaceTransformFlagsKHR.IdentityBitKHR;
        pCaps->CurrentTransform = VkSurfaceTransformFlagsKHR.IdentityBitKHR;
        pCaps->SupportedCompositeAlpha = VkCompositeAlphaFlagsKHR.OpaqueBitKHR;
        pCaps->SupportedUsageFlags = VkImageUsageFlags.ColorAttachmentBit | VkImageUsageFlags.TransferSrcBit | VkImageUsageFlags.TransferDstBit;
        return VkResult.Success;
    }

    public static VkResult vkGetPhysicalDeviceSurfaceFormatsKHR(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface, int* pCount, VkSurfaceFormatKHR* pFormats)
    {
        var (unorm, srgb) = Formats.SurfaceFormats(Profile.PreferredCanvasFormat);
        if (pFormats == null)
        {
            *pCount = 2;
            return VkResult.Success;
        }
        var n = Math.Min(*pCount, 2);
        if (n > 0) pFormats[0] = new VkSurfaceFormatKHR { Format = unorm, ColorSpace = VkColorSpaceKHR.SRGBNonlinearKHR };
        if (n > 1) pFormats[1] = new VkSurfaceFormatKHR { Format = srgb, ColorSpace = VkColorSpaceKHR.SRGBNonlinearKHR };
        *pCount = n;
        return n < 2 ? VkResult.Incomplete : VkResult.Success;
    }

    public static VkResult vkGetPhysicalDeviceSurfacePresentModesKHR(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface, int* pCount, VkPresentModeKHR* pModes)
    {
        if (pModes == null)
        {
            *pCount = 1;
            return VkResult.Success;
        }
        if (*pCount < 1)
            return VkResult.Incomplete;
        pModes[0] = VkPresentModeKHR.FifoKHR;
        *pCount = 1;
        return VkResult.Success;
    }

    // ---- swapchain -------------------------------------------------------------------------------------------------------

    public static VkResult vkCreateSwapchainKHR(VkDevice device, VkSwapchainCreateInfoKHR* pCreateInfo, VkAllocationCallbacks* pAllocator, VkSwapchainKHR* pSwapchain)
    {
        const string fn = "vkCreateSwapchainKHR";
        ref readonly var ci = ref *pCreateInfo;
        var dev = HandleTable.Lookup<DeviceObject>(device.VkHandle, fn);
        var surface = HandleTable.Lookup<SurfaceObject>(ci.Surface.VkHandle, fn);
        if (dev is null || surface is null)
            return VkResult.ErrorSurfaceLostKHR;

        var srgbView = Formats.IsSrgb(ci.ImageFormat);
        var linear = srgbView ? Formats.SrgbTwin(ci.ImageFormat) : ci.ImageFormat;
        var canvasFormat = Formats.ToGpuTextureFormat(linear);
        if (canvasFormat is not ("bgra8unorm" or "rgba8unorm" or "rgba16float"))
        {
            IcdLog.Unsupported(fn, $"swapchain format {ci.ImageFormat} (canvases support B8G8R8A8/R8G8B8A8 ± sRGB and R16G16B16A16_SFLOAT)");
            return VkResult.ErrorFormatNotSupported;
        }
        if (ci.PresentMode != VkPresentModeKHR.FifoKHR)
            IcdLog.Ignored(fn, $"present mode {ci.PresentMode} (the browser presents once per animation frame, like FIFO)");
        var (w, h) = CanvasSize();
        if (ci.ImageExtent.Width != w || ci.ImageExtent.Height != h)
            IcdLog.Warn($"{fn}: imageExtent {ci.ImageExtent.Width}x{ci.ImageExtent.Height} differs from the canvas size {w}x{h} (currentExtent); " +
                "the canvas size is used, so attachments sized from imageExtent will not match");

        var usage = GpuTextureUsage.RenderAttachment | GpuTextureUsage.CopySrc; // COPY_SRC: pixel readback (window.__brutal.readPixel)
        if ((ci.ImageUsage & VkImageUsageFlags.TransferDstBit) != 0) usage |= GpuTextureUsage.CopyDst;
        if ((ci.ImageUsage & (VkImageUsageFlags.SampledBit | VkImageUsageFlags.InputAttachmentBit)) != 0) usage |= GpuTextureUsage.TextureBinding;
        if ((ci.ImageUsage & VkImageUsageFlags.StorageBit) != 0) usage |= GpuTextureUsage.StorageBinding;
        var twin = Formats.SrgbTwin(linear);
        var viewFormats = twin != VkFormat.Undefined && Formats.ToGpuTextureFormat(twin) is { } t ? $"[\"{t}\"]" : "[]";
        var alpha = ci.CompositeAlpha == VkCompositeAlphaFlagsKHR.PreMultipliedBitKHR ? "premultiplied" : "opaque";
        WebIcd.Bridge.ConfigureCanvas(canvasFormat, usage, viewFormats, alpha);

        if (HandleTable.Get<SwapchainObject>(ci.OldSwapchain.VkHandle) is { } old)
            old.Retired = true;
        var swapchain = new SwapchainObject
        {
            Surface = surface, Device = dev, Format = ci.ImageFormat, CanvasFormat = canvasFormat, SrgbView = srgbView, Width = w, Height = h,
            Images = new ImageObject[Profile.SwapchainImageCount],
        };
        for (var i = 0; i < swapchain.Images.Length; i++)
        {
            var image = new ImageObject
            {
                Format = ci.ImageFormat, GpuFormat = canvasFormat, Type = VkImageType._2D, Width = w, Height = h, Depth = 1, MipLevels = 1, ArrayLayers = 1,
                Samples = 1, Usage = ci.ImageUsage | VkImageUsageFlags.ColorAttachmentBit, Swapchain = swapchain, SwapchainIndex = i,
            };
            HandleTable.Add(image);
            swapchain.Images[i] = image;
        }
        HandleTable.Add(swapchain);
        surface.Swapchain = swapchain;
        *pSwapchain = new VkSwapchainKHR(swapchain.Handle);
        return VkResult.Success;
    }

    public static void vkDestroySwapchainKHR(VkDevice device, VkSwapchainKHR swapchain, VkAllocationCallbacks* pAllocator)
    {
        var s = HandleTable.Get<SwapchainObject>(swapchain.VkHandle);
        if (s is null)
            return;
        foreach (var image in s.Images)
            HandleTable.Remove(image.Handle);
        if (s.Surface.Swapchain == s)
            s.Surface.Swapchain = null;
        HandleTable.Remove(s.Handle);
    }

    public static VkResult vkGetSwapchainImagesKHR(VkDevice device, VkSwapchainKHR swapchain, int* pCount, VkImage* pImages)
    {
        var s = HandleTable.Lookup<SwapchainObject>(swapchain.VkHandle, "vkGetSwapchainImagesKHR");
        if (s is null)
            return VkResult.ErrorSurfaceLostKHR;
        if (pImages == null)
        {
            *pCount = s.Images.Length;
            return VkResult.Success;
        }
        var n = Math.Min(*pCount, s.Images.Length);
        for (var i = 0; i < n; i++)
            pImages[i] = new VkImage(s.Images[i].Handle);
        *pCount = n;
        return n < s.Images.Length ? VkResult.Incomplete : VkResult.Success;
    }

    public static VkResult vkAcquireNextImageKHR(VkDevice device, VkSwapchainKHR swapchain, nint timeout, VkSemaphore semaphore, VkFence fence, int* pImageIndex)
    {
        const string fn = "vkAcquireNextImageKHR";
        var s = HandleTable.Lookup<SwapchainObject>(swapchain.VkHandle, fn);
        if (s is null)
            return VkResult.ErrorSurfaceLostKHR;
        if (s.Retired)
            return VkResult.ErrorOutOfDateKHR;
        var packed = WebIcd.Bridge.AcquireCanvasTexture();
        if (packed == BridgeStatus.CanvasDeviceLost)
            return DeviceLost(fn);
        if (packed >>> 16 != s.Width || (packed & 0xFFFF) != s.Height)
            return VkResult.ErrorOutOfDateKHR;
        var index = s.NextIndex;
        s.NextIndex = (index + 1) % s.Images.Length;
        s.CurrentIndex = index;
        *pImageIndex = index;
        if (fence.VkHandle != 0 && HandleTable.Lookup<FenceObject>(fence.VkHandle, fn) is { } f)
            f.Signaled = true;
        return VkResult.Success;
    }

    public static VkResult vkQueuePresentKHR(VkQueue queue, VkPresentInfoKHR* pPresentInfo)
    {
        const string fn = "vkQueuePresentKHR";
        var packed = WebIcd.Bridge.Present();
        if (packed == BridgeStatus.CanvasDeviceLost)
            return DeviceLost(fn);
        var result = VkResult.Success;
        for (var i = 0; i < pPresentInfo->SwapchainCount; i++)
        {
            var s = HandleTable.Lookup<SwapchainObject>(pPresentInfo->Swapchains[i].VkHandle, fn);
            var r = VkResult.Success;
            if (s is null || s.Retired)
                r = VkResult.ErrorOutOfDateKHR;
            else
            {
                if (s.CurrentIndex != pPresentInfo->ImageIndices[i] && IcdLog.First(fn, "present-index"))
                    IcdLog.Warn($"{fn}: presenting image {pPresentInfo->ImageIndices[i]} but image {s.CurrentIndex} was acquired");
                s.CurrentIndex = -1;
                if (packed >>> 16 != s.Width || (packed & 0xFFFF) != s.Height)
                    r = VkResult.SuboptimalKHR;
            }
            if (pPresentInfo->Results != null)
                pPresentInfo->Results[i] = r;
            if (r < 0 && result >= 0 || result == VkResult.Success)
                result = r; // the first error wins; otherwise SUBOPTIMAL over SUCCESS
        }
        return result;
    }

    public static VkResult vkGetSwapchainStatusKHR(VkDevice device, VkSwapchainKHR swapchain)
    {
        var s = HandleTable.Lookup<SwapchainObject>(swapchain.VkHandle, "vkGetSwapchainStatusKHR");
        if (s is null || s.Retired)
            return VkResult.ErrorOutOfDateKHR;
        var (w, h) = CanvasSize();
        return w == s.Width && h == s.Height ? VkResult.Success : VkResult.SuboptimalKHR;
    }
}
