using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

internal static unsafe partial class Icd
{
    public static VkResult vkCreateImage(VkDevice device, VkImageCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator, VkImage* pImage)
    {
        const string fn = "vkCreateImage";
        ref readonly var ci = ref *pCreateInfo;
        var gpuFormat = Formats.ToGpuTextureFormat(ci.Format);
        if (gpuFormat is null)
        {
            IcdLog.Unsupported(fn, $"format {ci.Format}");
            return VkResult.ErrorFormatNotSupported;
        }
        if (Formats.OptimalTilingFeatures(ci.Format, Profile.GpuFeatures) == VkFormatFeatureFlags.None)
        {
            IcdLog.Unsupported(fn, $"format {ci.Format} on this WebGPU device (missing feature, e.g. texture-compression-*)");
            return VkResult.ErrorFormatNotSupported;
        }
        if (Formats.IsCompressed(ci.Format) && !Profile.GpuFeatures.Contains("texture-compression-unaligned"))
        {
            var (_, bw, bh) = Formats.BlockInfo(ci.Format);
            if (ci.Extent.Width % bw != 0 || ci.Extent.Height % bh != 0)
            {
                IcdLog.Unsupported(fn, $"{ci.Format} images whose size ({ci.Extent.Width}x{ci.Extent.Height}) is not a multiple of the block size");
                return VkResult.ErrorFormatNotSupported;
            }
        }
        if (ci.Tiling != VkImageTiling.Optimal)
        {
            IcdLog.Unsupported(fn, $"tiling {ci.Tiling} (only OPTIMAL images exist on WebGPU)");
            return VkResult.ErrorFormatNotSupported;
        }
        if ((ci.Flags & (VkImageCreateFlags.SparseBindingBit | VkImageCreateFlags.SparseResidencyBit | VkImageCreateFlags.SparseAliasedBit)) != 0)
        {
            IcdLog.Unsupported(fn, "sparse images");
            return VkResult.ErrorFeatureNotPresent;
        }
        var dimension = Mappings.TextureDimension(ci.ImageType);
        var samples = Mappings.SampleCount(ci.Samples);
        if (dimension is null || samples < 0)
        {
            IcdLog.Unsupported(fn, $"image type {ci.ImageType} with {ci.Samples} samples (sample counts are 1 or 4)");
            return VkResult.ErrorFeatureNotPresent;
        }
        var usage = Mappings.TextureUsage(ci.Usage);
        if (usage == 0)
            usage = GpuTextureUsage.RenderAttachment; // e.g. TRANSIENT_ATTACHMENT alone
        var depthOrLayers = ci.ImageType == VkImageType._3D ? ci.Extent.Depth : Math.Max(1, ci.ArrayLayers);

        string json;
        using (var j = new Json())
        {
            var w = j.W;
            w.WriteStartObject();
            w.WriteStartArray("size");
            w.WriteNumberValue(ci.Extent.Width);
            w.WriteNumberValue(Math.Max(1, ci.Extent.Height));
            w.WriteNumberValue(depthOrLayers);
            w.WriteEndArray();
            w.WriteNumber("mipLevelCount", Math.Max(1, ci.MipLevels));
            w.WriteNumber("sampleCount", samples);
            w.WriteString("dimension", dimension);
            w.WriteString("format", gpuFormat);
            w.WriteNumber("usage", usage);
            var twin = Formats.SrgbTwin(ci.Format);
            if (twin != VkFormat.Undefined && Formats.ToGpuTextureFormat(twin) is { } twinFormat)
            {
                w.WriteStartArray("viewFormats");
                w.WriteStringValue(twinFormat);
                w.WriteEndArray();
            }
            w.WriteEndObject();
            json = j.ToString();
        }
        var gpu = WebIcd.Bridge.CreateTexture(json);
        if (gpu == 0)
        {
            IcdLog.Error($"{fn}: createTexture failed for {json}");
            return VkResult.ErrorOutOfDeviceMemory;
        }
        var image = new ImageObject
        {
            Format = ci.Format, GpuFormat = gpuFormat, Type = ci.ImageType, Width = ci.Extent.Width, Height = Math.Max(1, ci.Extent.Height),
            Depth = ci.ImageType == VkImageType._3D ? ci.Extent.Depth : 1, MipLevels = Math.Max(1, ci.MipLevels),
            ArrayLayers = Math.Max(1, ci.ArrayLayers), Samples = samples, Usage = ci.Usage, Gpu = gpu,
        };
        HandleTable.Add(image);
        *pImage = new VkImage(image.Handle);
        return VkResult.Success;
    }

    public static void vkDestroyImage(VkDevice device, VkImage image, VkAllocationCallbacks* pAllocator)
    {
        var obj = HandleTable.Get<ImageObject>(image.VkHandle);
        if (obj is null || obj.IsSwapchainImage)
            return; // swapchain images belong to the swapchain
        WebIcd.Bridge.Release(obj.Gpu);
        HandleTable.Remove(obj.Handle);
    }

    public static void vxDestroyImages(VkDevice device, VkImage* pImages, int count, VkAllocationCallbacks* pAllocator)
    {
        for (var i = 0; i < count; i++)
            vkDestroyImage(device, pImages[i], pAllocator);
    }

    public static void vkGetImageMemoryRequirements(VkDevice device, VkImage image, VkMemoryRequirements* pMemoryRequirements)
    {
        var obj = HandleTable.Lookup<ImageObject>(image.VkHandle, "vkGetImageMemoryRequirements");
        ImageRequirements(obj?.Format ?? VkFormat.R8G8B8A8UNorm, obj?.Width ?? 1, obj?.Height ?? 1, obj is null ? 1 : obj.Depth * obj.ArrayLayers,
            obj?.MipLevels ?? 1, obj?.Samples ?? 1, pMemoryRequirements);
    }

    /// <summary>Synthetic requirements: Σ mips of the texel footprint (self-consistent only; WebGPU owns the real storage).</summary>
    private static void ImageRequirements(VkFormat format, int width, int height, int depthOrLayers, int mips, int samples, VkMemoryRequirements* r)
    {
        var (bytes, bw, bh) = Formats.BlockInfo(format);
        if (bytes == 0) bytes = 4;
        ulong total = 0;
        for (var m = 0; m < mips; m++)
        {
            var w = (ulong)((Math.Max(1, width >> m) + bw - 1) / bw);
            var h = (ulong)((Math.Max(1, height >> m) + bh - 1) / bh);
            total += w * h * (ulong)depthOrLayers * (ulong)bytes * (ulong)Math.Max(1, samples);
        }
        r->Size = (ByteSize64)((total + 255) & ~255UL);
        r->Alignment = (ByteSize64)256UL;
        r->MemoryTypeBits = 1 << Profile.DeviceLocalType;
    }

    public static void vkGetImageMemoryRequirements2(VkDevice device, VkImageMemoryRequirementsInfo2* pInfo, VkMemoryRequirements2* pMemoryRequirements) =>
        vkGetImageMemoryRequirements(device, pInfo->Image, &pMemoryRequirements->MemoryRequirements);

    public static void vkGetDeviceImageMemoryRequirements(VkDevice device, VkDeviceImageMemoryRequirements* pInfo, VkMemoryRequirements2* pMemoryRequirements)
    {
        var ci = pInfo->CreateInfo;
        ImageRequirements(ci->Format, ci->Extent.Width, ci->Extent.Height, Math.Max(1, ci->Extent.Depth) * Math.Max(1, ci->ArrayLayers),
            Math.Max(1, ci->MipLevels), Math.Max(1, Mappings.SampleCount(ci->Samples)), &pMemoryRequirements->MemoryRequirements);
    }

    public static void vkGetImageSparseMemoryRequirements(VkDevice device, VkImage image, int* pCount, VkSparseImageMemoryRequirements* pRequirements) =>
        *pCount = 0;

    public static VkResult vkBindImageMemory(VkDevice device, VkImage image, VkDeviceMemory memory, ByteSize64 memoryOffset)
    {
        var m = HandleTable.Lookup<DeviceMemoryObject>(memory.VkHandle, "vkBindImageMemory");
        if (m is { HostVisible: true })
            IcdLog.WarnOnce("img-hostvisible", "vkBindImageMemory: an image bound to host-visible memory cannot be mapped on WebGPU (no linear images)");
        return VkResult.Success; // the GPUTexture owns its storage
    }

    public static VkResult vkBindImageMemory2(VkDevice device, int bindInfoCount, VkBindImageMemoryInfo* pBindInfos)
    {
        for (var i = 0; i < bindInfoCount; i++)
            vkBindImageMemory(device, pBindInfos[i].Image, pBindInfos[i].Memory, pBindInfos[i].MemoryOffset);
        return VkResult.Success;
    }

    // ---- image views ---------------------------------------------------------------------------------------------------

    public static VkResult vkCreateImageView(VkDevice device, VkImageViewCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator, VkImageView* pView)
    {
        const string fn = "vkCreateImageView";
        ref readonly var ci = ref *pCreateInfo;
        var image = HandleTable.Lookup<ImageObject>(ci.Image.VkHandle, fn);
        if (image is null)
            return VkResult.ErrorInitializationFailed;

        var format = ci.Format == VkFormat.Undefined ? image.Format : ci.Format;
        if (format != image.Format && format != Formats.SrgbTwin(image.Format))
        {
            IcdLog.Unsupported(fn, $"view format {format} on a {image.Format} image (views may only add or drop sRGB)");
            return VkResult.ErrorFormatNotSupported;
        }
        if (!IsIdentity(ci.Components.R, VkComponentSwizzle.R) || !IsIdentity(ci.Components.G, VkComponentSwizzle.G)
            || !IsIdentity(ci.Components.B, VkComponentSwizzle.B) || !IsIdentity(ci.Components.A, VkComponentSwizzle.A))
        {
            IcdLog.Unsupported(fn, "non-identity component swizzles");
            return VkResult.ErrorFeatureNotPresent;
        }
        var dimension = Mappings.ViewDimension(ci.ViewType);
        if (dimension is null)
        {
            IcdLog.Unsupported(fn, $"view type {ci.ViewType}");
            return VkResult.ErrorFeatureNotPresent;
        }

        ref readonly var range = ref ci.SubresourceRange;
        var baseMip = range.BaseMipLevel;
        var mipCount = range.LevelCount == Mappings.Remaining ? image.MipLevels - baseMip : range.LevelCount;
        var baseLayer = range.BaseArrayLayer;
        var layerCount = range.LayerCount == Mappings.Remaining ? image.ArrayLayers - baseLayer : range.LayerCount;
        var aspect = AspectName(range.AspectMask, image.Format);

        var view = new ImageViewObject
        {
            Image = image, Format = format, ViewType = ci.ViewType, Aspect = range.AspectMask, BaseMip = baseMip, MipCount = mipCount,
            BaseLayer = baseLayer, LayerCount = layerCount,
            Width = Math.Max(1, image.Width >> baseMip), Height = Math.Max(1, image.Height >> baseMip),
        };

        if (image.IsSwapchainImage)
        {
            // the canvas is configured with the linear format; sRGB views come from its viewFormats
            view.Gpu = view.AttachmentGpu = Formats.IsSrgb(format) ? ReservedHandles.CanvasViewSrgb : ReservedHandles.CanvasView;
        }
        else
        {
            var gpuFormat = Formats.ToGpuTextureFormat(format)!;
            view.Gpu = WebIcd.Bridge.CreateTextureView(image.Gpu, ViewJson(gpuFormat, dimension, aspect, baseMip, mipCount, baseLayer, layerCount));
            if (view.Gpu == 0)
            {
                IcdLog.Error($"{fn}: createView failed for {image}");
                return VkResult.ErrorOutOfDeviceMemory;
            }
            var isAttachment = (image.Usage & (VkImageUsageFlags.ColorAttachmentBit | VkImageUsageFlags.DepthStencilAttachmentBit)) != 0;
            var attachmentCompatible = aspect == "all" && mipCount == 1 && (layerCount == 1 || dimension == "3d");
            view.AttachmentGpu = !isAttachment || attachmentCompatible
                ? view.Gpu
                : WebIcd.Bridge.CreateTextureView(image.Gpu, ViewJson(gpuFormat, image.Type == VkImageType._3D ? "3d" : "2d", "all", baseMip, 1, baseLayer, 1));
        }

        HandleTable.Add(view);
        *pView = new VkImageView(view.Handle);
        return VkResult.Success;
    }

    private static bool IsIdentity(VkComponentSwizzle s, VkComponentSwizzle own) => s == VkComponentSwizzle.Identity || s == own;

    private static string AspectName(VkImageAspectFlags aspect, VkFormat format)
    {
        var depth = (aspect & VkImageAspectFlags.DepthBit) != 0;
        var stencil = (aspect & VkImageAspectFlags.StencilBit) != 0;
        if (depth && !stencil && Formats.HasStencil(format))
            return "depth-only";
        if (stencil && !depth && Formats.HasDepth(format))
            return "stencil-only";
        return "all";
    }

    private static string ViewJson(string format, string dimension, string aspect, int baseMip, int mipCount, int baseLayer, int layerCount)
    {
        using var j = new Json();
        var w = j.W;
        w.WriteStartObject();
        w.WriteString("format", format);
        w.WriteString("dimension", dimension);
        w.WriteString("aspect", aspect);
        w.WriteNumber("baseMipLevel", baseMip);
        w.WriteNumber("mipLevelCount", mipCount);
        w.WriteNumber("baseArrayLayer", baseLayer);
        w.WriteNumber("arrayLayerCount", layerCount);
        w.WriteEndObject();
        return j.ToString();
    }

    public static void vkDestroyImageView(VkDevice device, VkImageView imageView, VkAllocationCallbacks* pAllocator)
    {
        var obj = HandleTable.Get<ImageViewObject>(imageView.VkHandle);
        if (obj is null)
            return;
        if (!obj.Image.IsSwapchainImage)
        {
            WebIcd.Bridge.Release(obj.Gpu);
            if (obj.AttachmentGpu != obj.Gpu)
                WebIcd.Bridge.Release(obj.AttachmentGpu);
        }
        HandleTable.Remove(obj.Handle);
    }

    public static void vxDestroyImageViews(VkDevice device, VkImageView* pViews, int count, VkAllocationCallbacks* pAllocator)
    {
        for (var i = 0; i < count; i++)
            vkDestroyImageView(device, pViews[i], pAllocator);
    }

    // ---- samplers ------------------------------------------------------------------------------------------------------

    public static VkResult vkCreateSampler(VkDevice device, VkSamplerCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator, VkSampler* pSampler)
    {
        const string fn = "vkCreateSampler";
        ref readonly var ci = ref *pCreateInfo;
        var u = Mappings.AddressMode(ci.AddressModeU);
        var v = Mappings.AddressMode(ci.AddressModeV);
        var wMode = Mappings.AddressMode(ci.AddressModeW);
        if (u is null || v is null || wMode is null)
        {
            IcdLog.Unsupported(fn, "CLAMP_TO_BORDER / MIRROR_CLAMP_TO_EDGE address modes (border colours)");
            return VkResult.ErrorFeatureNotPresent;
        }
        if (ci.UnnormalizedCoordinates)
        {
            IcdLog.Unsupported(fn, "unnormalizedCoordinates");
            return VkResult.ErrorFeatureNotPresent;
        }
        if (ci.MipLodBias != 0f)
            IcdLog.Ignored(fn, "mipLodBias");

        var mag = Mappings.Filter(ci.MagFilter);
        var min = Mappings.Filter(ci.MinFilter);
        var mip = Mappings.MipmapFilter(ci.MipmapMode);
        var anisotropy = ci.AnisotropyEnable ? (int)Math.Clamp(MathF.Round(ci.MaxAnisotropy), 1f, 16f) : 1;
        if (anisotropy > 1 && (mag != "linear" || min != "linear" || mip != "linear"))
        {
            IcdLog.Ignored(fn, "anisotropy with nearest filters (WebGPU requires linear min/mag/mipmap filters; forcing linear)");
            mag = min = mip = "linear";
        }

        string json;
        using (var j = new Json())
        {
            var w = j.W;
            w.WriteStartObject();
            w.WriteString("addressModeU", u);
            w.WriteString("addressModeV", v);
            w.WriteString("addressModeW", wMode);
            w.WriteString("magFilter", mag);
            w.WriteString("minFilter", min);
            w.WriteString("mipmapFilter", mip);
            w.WriteNumber("lodMinClamp", Math.Max(0f, ci.MinLod));
            w.WriteNumber("lodMaxClamp", Math.Clamp(ci.MaxLod, Math.Max(0f, ci.MinLod), 32f));
            if (ci.CompareEnable)
                w.WriteString("compare", Mappings.Compare(ci.CompareOp) ?? "always");
            if (anisotropy > 1)
                w.WriteNumber("maxAnisotropy", anisotropy);
            w.WriteEndObject();
            json = j.ToString();
        }
        var gpu = WebIcd.Bridge.CreateSampler(json);
        if (gpu == 0)
        {
            IcdLog.Error($"{fn}: createSampler failed for {json}");
            return VkResult.ErrorOutOfDeviceMemory;
        }
        var sampler = new SamplerObject { Gpu = gpu, Comparison = ci.CompareEnable, Filtering = mag == "linear" || min == "linear" || mip == "linear" };
        HandleTable.Add(sampler);
        *pSampler = new VkSampler(sampler.Handle);
        return VkResult.Success;
    }

    public static void vkDestroySampler(VkDevice device, VkSampler sampler, VkAllocationCallbacks* pAllocator)
    {
        var obj = HandleTable.Get<SamplerObject>(sampler.VkHandle);
        if (obj is null)
            return;
        WebIcd.Bridge.Release(obj.Gpu);
        HandleTable.Remove(obj.Handle);
    }
}
