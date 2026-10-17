using System.Text;
using System.Text.Json;
using Brutal.Numerics;
using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

/// <summary>WebGPU device limits the ICD reports through (clamped to the profile values).</summary>
internal struct GpuLimits
{
    public int MaxTextureDimension1D, MaxTextureDimension2D, MaxTextureDimension3D, MaxTextureArrayLayers;
    public int MaxBindGroups, MaxDynamicUniformBuffersPerPipelineLayout, MaxDynamicStorageBuffersPerPipelineLayout;
    public int MaxSampledTexturesPerShaderStage, MaxSamplersPerShaderStage, MaxStorageBuffersPerShaderStage;
    public int MaxStorageTexturesPerShaderStage, MaxUniformBuffersPerShaderStage;
    public long MaxUniformBufferBindingSize, MaxStorageBufferBindingSize, MaxBufferSize;
    public int MinUniformBufferOffsetAlignment, MinStorageBufferOffsetAlignment;
    public int MaxVertexBuffers, MaxVertexAttributes, MaxVertexBufferArrayStride, MaxInterStageShaderVariables;
    public int MaxColorAttachments, MaxColorAttachmentBytesPerSample;
    public int MaxComputeWorkgroupStorageSize, MaxComputeInvocationsPerWorkgroup;
    public int MaxComputeWorkgroupSizeX, MaxComputeWorkgroupSizeY, MaxComputeWorkgroupSizeZ, MaxComputeWorkgroupsPerDimension;

    /// <summary>The WebGPU core defaults (= the values of webgpu-limits-features-and-compat-mode.md).</summary>
    public static GpuLimits Defaults => new()
    {
        MaxTextureDimension1D = 8192, MaxTextureDimension2D = 8192, MaxTextureDimension3D = 2048, MaxTextureArrayLayers = 256,
        MaxBindGroups = 4, MaxDynamicUniformBuffersPerPipelineLayout = 8, MaxDynamicStorageBuffersPerPipelineLayout = 4,
        MaxSampledTexturesPerShaderStage = 16, MaxSamplersPerShaderStage = 16, MaxStorageBuffersPerShaderStage = 8,
        MaxStorageTexturesPerShaderStage = 4, MaxUniformBuffersPerShaderStage = 12,
        MaxUniformBufferBindingSize = 65536, MaxStorageBufferBindingSize = 134217728, MaxBufferSize = 268435456,
        MinUniformBufferOffsetAlignment = 256, MinStorageBufferOffsetAlignment = 256,
        MaxVertexBuffers = 8, MaxVertexAttributes = 16, MaxVertexBufferArrayStride = 2048, MaxInterStageShaderVariables = 16,
        MaxColorAttachments = 8, MaxColorAttachmentBytesPerSample = 32,
        MaxComputeWorkgroupStorageSize = 16384, MaxComputeInvocationsPerWorkgroup = 256,
        MaxComputeWorkgroupSizeX = 256, MaxComputeWorkgroupSizeY = 256, MaxComputeWorkgroupSizeZ = 64,
        MaxComputeWorkgroupsPerDimension = 65535,
    };
}

/// <summary>
/// The <c>VP_BRUTAL_web_poc_2026</c> profile: API version, extensions, features, limits, memory types and queue family the ICD
/// reports (docs/research/webgpu/web-vulkan-profile-proposal.md, webgpu-limits-features-and-compat-mode.md). Limits are the
/// WebGPU core defaults clamped by the real device limits read through the bridge.
/// </summary>
internal static unsafe class Profile
{
    public const string Name = "VP_BRUTAL_web_poc_2026";
    public const string WebSurfaceExtension = "VK_BRUTAL_web_surface";

    /// <summary>
    /// VK_MAKE_API_VERSION(0, 1, 3, 0): non-conformant 1.3 (no buffer device address, descriptor indexing, private data, ...). 1.3 is
    /// advertised because BRUTAL calls the 1.3 core entry points (vkCmdBeginRendering, vkCmdPipelineBarrier2, ...) and the Khronos
    /// profiles layer only resolves those names under a 1.3 profile.
    /// </summary>
    public const int ApiVersion = (1 << 22) | (3 << 12);

    public const int DriverVersion = (2026 << 16) | 10;
    public const int MaxPushConstantsSize = 128;
    public const int MaxPerSetDescriptors = 1000;
    public const string DriverName = "BRUTAL web ICD (WebGPU)";
    public const int PushConstantSlotSize = 256;
    public const int SwapchainImageCount = 3;

    public static readonly (string Name, int SpecVersion)[] InstanceExtensions =
    [
        ("VK_KHR_surface", 25),
        ("VK_KHR_get_physical_device_properties2", 2),
        ("VK_KHR_portability_enumeration", 1),
        ("VK_EXT_debug_utils", 2),
        (WebSurfaceExtension, 1),
    ];

    public static readonly (string Name, int SpecVersion)[] DeviceExtensions =
    [
        ("VK_KHR_swapchain", 70),
        ("VK_KHR_portability_subset", 1),
        ("VK_KHR_dynamic_rendering", 1),
        ("VK_KHR_synchronization2", 1),
        ("VK_KHR_maintenance1", 2),
        ("VK_KHR_maintenance2", 1),
        ("VK_KHR_maintenance3", 1),
        ("VK_KHR_maintenance4", 2),
    ];

    private static bool s_initialized;
    private static GpuLimits s_limits = GpuLimits.Defaults;
    private static HashSet<string> s_features = new(StringComparer.Ordinal);
    private static string s_vendor = "", s_architecture = "", s_description = "";
    private static bool s_fallbackAdapter;
    private static string? s_preferredCanvasFormat;

    public static ref readonly GpuLimits Limits
    {
        get
        {
            EnsureInitialized();
            return ref s_limits;
        }
    }

    public static IReadOnlySet<string> GpuFeatures
    {
        get
        {
            EnsureInitialized();
            return s_features;
        }
    }

    public static string PreferredCanvasFormat
    {
        get
        {
            EnsureInitialized();
            return s_preferredCanvasFormat ??= WebIcd.Bridge.GetPreferredCanvasFormat();
        }
    }

    public static bool HasExtension((string Name, int SpecVersion)[] list, string name)
    {
        foreach (var e in list)
            if (e.Name == name)
                return true;
        return false;
    }

    /// <summary>Reads limits, features and adapter info; on failure (JS module not registered yet) retries on first use.</summary>
    public static void TryInitialize(WebGpuBridge bridge)
    {
        try
        {
            Initialize(bridge);
        }
        catch (Exception e)
        {
            IcdLog.Warn($"reading the WebGPU adapter limits failed ({e.GetType().Name}: {e.Message}); retrying at vkCreateInstance");
        }
    }

    public static void EnsureInitialized()
    {
        if (!s_initialized && WebIcd.HasBridge)
            Initialize(WebIcd.Bridge);
    }

    /// <summary>Forgets the adapter state (tests swap bridges).</summary>
    internal static void Reset()
    {
        s_initialized = false;
        s_limits = GpuLimits.Defaults;
        s_features = new HashSet<string>(StringComparer.Ordinal);
        s_preferredCanvasFormat = null;
    }

    private static void Initialize(WebGpuBridge bridge)
    {
        var limits = GpuLimits.Defaults;
        using (var doc = JsonDocument.Parse(bridge.GetLimitsJson()))
        {
            var root = doc.RootElement;
            int Clamp(string name, int profileValue) =>
                root.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) ? (int)Math.Min(profileValue, d) : profileValue;
            long ClampL(string name, long profileValue) =>
                root.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) ? (long)Math.Min(profileValue, d) : profileValue;
            int Align(string name, int profileValue) =>
                root.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) ? (int)Math.Max(profileValue, d) : profileValue;

            limits.MaxTextureDimension1D = Clamp("maxTextureDimension1D", limits.MaxTextureDimension1D);
            limits.MaxTextureDimension2D = Clamp("maxTextureDimension2D", limits.MaxTextureDimension2D);
            limits.MaxTextureDimension3D = Clamp("maxTextureDimension3D", limits.MaxTextureDimension3D);
            limits.MaxTextureArrayLayers = Clamp("maxTextureArrayLayers", limits.MaxTextureArrayLayers);
            limits.MaxBindGroups = Clamp("maxBindGroups", limits.MaxBindGroups);
            limits.MaxDynamicUniformBuffersPerPipelineLayout = Clamp("maxDynamicUniformBuffersPerPipelineLayout", limits.MaxDynamicUniformBuffersPerPipelineLayout);
            limits.MaxDynamicStorageBuffersPerPipelineLayout = Clamp("maxDynamicStorageBuffersPerPipelineLayout", limits.MaxDynamicStorageBuffersPerPipelineLayout);
            limits.MaxSampledTexturesPerShaderStage = Clamp("maxSampledTexturesPerShaderStage", limits.MaxSampledTexturesPerShaderStage);
            limits.MaxSamplersPerShaderStage = Clamp("maxSamplersPerShaderStage", limits.MaxSamplersPerShaderStage);
            limits.MaxStorageBuffersPerShaderStage = Clamp("maxStorageBuffersPerShaderStage", limits.MaxStorageBuffersPerShaderStage);
            limits.MaxStorageTexturesPerShaderStage = Clamp("maxStorageTexturesPerShaderStage", limits.MaxStorageTexturesPerShaderStage);
            limits.MaxUniformBuffersPerShaderStage = Clamp("maxUniformBuffersPerShaderStage", limits.MaxUniformBuffersPerShaderStage);
            limits.MaxUniformBufferBindingSize = ClampL("maxUniformBufferBindingSize", limits.MaxUniformBufferBindingSize);
            limits.MaxStorageBufferBindingSize = ClampL("maxStorageBufferBindingSize", limits.MaxStorageBufferBindingSize);
            limits.MaxBufferSize = ClampL("maxBufferSize", limits.MaxBufferSize);
            limits.MinUniformBufferOffsetAlignment = Align("minUniformBufferOffsetAlignment", limits.MinUniformBufferOffsetAlignment);
            limits.MinStorageBufferOffsetAlignment = Align("minStorageBufferOffsetAlignment", limits.MinStorageBufferOffsetAlignment);
            limits.MaxVertexBuffers = Clamp("maxVertexBuffers", limits.MaxVertexBuffers);
            limits.MaxVertexAttributes = Clamp("maxVertexAttributes", limits.MaxVertexAttributes);
            limits.MaxVertexBufferArrayStride = Clamp("maxVertexBufferArrayStride", limits.MaxVertexBufferArrayStride);
            limits.MaxInterStageShaderVariables = Clamp("maxInterStageShaderVariables", limits.MaxInterStageShaderVariables);
            limits.MaxColorAttachments = Clamp("maxColorAttachments", limits.MaxColorAttachments);
            limits.MaxColorAttachmentBytesPerSample = Clamp("maxColorAttachmentBytesPerSample", limits.MaxColorAttachmentBytesPerSample);
            limits.MaxComputeWorkgroupStorageSize = Clamp("maxComputeWorkgroupStorageSize", limits.MaxComputeWorkgroupStorageSize);
            limits.MaxComputeInvocationsPerWorkgroup = Clamp("maxComputeInvocationsPerWorkgroup", limits.MaxComputeInvocationsPerWorkgroup);
            limits.MaxComputeWorkgroupSizeX = Clamp("maxComputeWorkgroupSizeX", limits.MaxComputeWorkgroupSizeX);
            limits.MaxComputeWorkgroupSizeY = Clamp("maxComputeWorkgroupSizeY", limits.MaxComputeWorkgroupSizeY);
            limits.MaxComputeWorkgroupSizeZ = Clamp("maxComputeWorkgroupSizeZ", limits.MaxComputeWorkgroupSizeZ);
            limits.MaxComputeWorkgroupsPerDimension = Clamp("maxComputeWorkgroupsPerDimension", limits.MaxComputeWorkgroupsPerDimension);
        }

        var features = new HashSet<string>(StringComparer.Ordinal);
        using (var doc = JsonDocument.Parse(bridge.GetFeaturesJson()))
            foreach (var f in doc.RootElement.EnumerateArray())
                if (f.GetString() is { } s)
                    features.Add(s);

        using (var doc = JsonDocument.Parse(bridge.GetAdapterInfoJson()))
        {
            var root = doc.RootElement;
            string Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            s_vendor = Str("vendor");
            s_architecture = Str("architecture");
            s_description = Str("description");
            s_fallbackAdapter = root.TryGetProperty("isFallbackAdapter", out var fb) && fb.ValueKind == JsonValueKind.True;
        }

        s_limits = limits;
        s_features = features;
        s_initialized = true;
    }

    // ---- physical device ---------------------------------------------------------------------------------------------

    public static int VendorId => s_vendor.ToLowerInvariant() switch
    {
        "nvidia" => 0x10DE,
        "amd" or "ati" => 0x1002,
        "intel" => 0x8086,
        "apple" => 0x106B,
        "qualcomm" => 0x5143,
        "arm" => 0x13B5,
        "google" => 0x1AE0,
        _ => 0,
    };

    public static string DeviceName
    {
        get
        {
            var detail = string.Join(' ', new[] { s_vendor, s_architecture, s_description }.Where(s => s.Length > 0));
            return detail.Length == 0 ? "WebGPU (BRUTAL web ICD)" : $"WebGPU {detail} (BRUTAL web ICD)";
        }
    }

    public static void FillProperties(VkPhysicalDeviceProperties* p)
    {
        EnsureInitialized();
        ref readonly var g = ref s_limits;
        p->ApiVersion = ApiVersion;
        p->DriverVersion = DriverVersion;
        p->VendorID = VendorId;
        p->DeviceID = 0x0E06;
        p->DeviceType = s_fallbackAdapter ? VkPhysicalDeviceType.Cpu : VkPhysicalDeviceType.IntegratedGpu;
        WriteUtf8(p->DeviceName, 256, DeviceName);
        var uuid = "BRUTALwebICD2026"u8;
        for (var i = 0; i < 16; i++)
            p->PipelineCacheUUID[i] = uuid[i];

        ref var l = ref p->Limits;
        l = default;
        l.MaxImageDimension1D = g.MaxTextureDimension1D;
        l.MaxImageDimension2D = g.MaxTextureDimension2D;
        l.MaxImageDimension3D = g.MaxTextureDimension3D;
        l.MaxImageDimensionCube = g.MaxTextureDimension2D;
        l.MaxImageArrayLayers = g.MaxTextureArrayLayers;
        l.MaxTexelBufferElements = 0;
        l.MaxUniformBufferRange = (int)Math.Min(int.MaxValue, g.MaxUniformBufferBindingSize);
        l.MaxStorageBufferRange = (int)Math.Min(int.MaxValue, g.MaxStorageBufferBindingSize);
        l.MaxPushConstantsSize = MaxPushConstantsSize;
        l.MaxMemoryAllocationCount = 4096;
        l.MaxSamplerAllocationCount = 4000;
        l.BufferImageGranularity = (ByteSize64)1UL;
        l.SparseAddressSpaceSize = (ByteSize64)0UL;
        // one bind group is reserved for emulated push constants (wgsl-conventions C8)
        l.MaxBoundDescriptorSets = Math.Max(1, g.MaxBindGroups - 1);
        l.MaxPerStageDescriptorSamplers = g.MaxSamplersPerShaderStage;
        l.MaxPerStageDescriptorUniformBuffers = g.MaxUniformBuffersPerShaderStage;
        l.MaxPerStageDescriptorStorageBuffers = g.MaxStorageBuffersPerShaderStage;
        l.MaxPerStageDescriptorSampledImages = g.MaxSampledTexturesPerShaderStage;
        l.MaxPerStageDescriptorStorageImages = g.MaxStorageTexturesPerShaderStage;
        l.MaxPerStageDescriptorInputAttachments = 0;
        l.MaxPerStageResources = g.MaxSamplersPerShaderStage + g.MaxUniformBuffersPerShaderStage + g.MaxStorageBuffersPerShaderStage
            + g.MaxSampledTexturesPerShaderStage + g.MaxStorageTexturesPerShaderStage + g.MaxColorAttachments;
        l.MaxDescriptorSetSamplers = g.MaxSamplersPerShaderStage;
        l.MaxDescriptorSetUniformBuffers = g.MaxUniformBuffersPerShaderStage;
        l.MaxDescriptorSetUniformBuffersDynamic = g.MaxDynamicUniformBuffersPerPipelineLayout;
        l.MaxDescriptorSetStorageBuffers = g.MaxStorageBuffersPerShaderStage;
        l.MaxDescriptorSetStorageBuffersDynamic = g.MaxDynamicStorageBuffersPerPipelineLayout;
        l.MaxDescriptorSetSampledImages = g.MaxSampledTexturesPerShaderStage;
        l.MaxDescriptorSetStorageImages = g.MaxStorageTexturesPerShaderStage;
        l.MaxDescriptorSetInputAttachments = 0;
        l.MaxVertexInputAttributes = g.MaxVertexAttributes;
        l.MaxVertexInputBindings = g.MaxVertexBuffers;
        l.MaxVertexInputAttributeOffset = g.MaxVertexBufferArrayStride - 1;
        l.MaxVertexInputBindingStride = g.MaxVertexBufferArrayStride;
        l.MaxVertexOutputComponents = 4 * g.MaxInterStageShaderVariables;
        l.MaxFragmentInputComponents = 4 * g.MaxInterStageShaderVariables;
        l.MaxFragmentOutputAttachments = g.MaxColorAttachments;
        l.MaxFragmentDualSrcAttachments = s_features.Contains("dual-source-blending") ? 1 : 0;
        l.MaxFragmentCombinedOutputResources = g.MaxColorAttachments + g.MaxStorageBuffersPerShaderStage + g.MaxStorageTexturesPerShaderStage;
        l.MaxComputeSharedMemorySize = g.MaxComputeWorkgroupStorageSize;
        l.MaxComputeWorkGroupCount = new uint3((uint)g.MaxComputeWorkgroupsPerDimension, (uint)g.MaxComputeWorkgroupsPerDimension, (uint)g.MaxComputeWorkgroupsPerDimension);
        l.MaxComputeWorkGroupInvocations = g.MaxComputeInvocationsPerWorkgroup;
        l.MaxComputeWorkGroupSize = new uint3((uint)g.MaxComputeWorkgroupSizeX, (uint)g.MaxComputeWorkgroupSizeY, (uint)g.MaxComputeWorkgroupSizeZ);
        l.SubPixelPrecisionBits = 8;
        l.SubTexelPrecisionBits = 8;
        l.MipmapPrecisionBits = 4;
        l.MaxDrawIndexedIndexValue = unchecked((int)0xFFFFFFFF);
        l.MaxDrawIndirectCount = 1;
        l.MaxSamplerLodBias = 0f;
        l.MaxSamplerAnisotropy = 16f;
        l.MaxViewports = 1;
        l.MaxViewportDimensions = new uint2((uint)g.MaxTextureDimension2D, (uint)g.MaxTextureDimension2D);
        l.ViewportBoundsRange = new float2(-2f * g.MaxTextureDimension2D, 2f * g.MaxTextureDimension2D);
        l.ViewportSubPixelBits = 0;
        l.MinMemoryMapAlignment = 64;
        l.MinTexelBufferOffsetAlignment = (ByteSize64)256UL;
        l.MinUniformBufferOffsetAlignment = (ByteSize64)(ulong)g.MinUniformBufferOffsetAlignment;
        l.MinStorageBufferOffsetAlignment = (ByteSize64)(ulong)g.MinStorageBufferOffsetAlignment;
        l.MinTexelOffset = -8;
        l.MaxTexelOffset = 7;
        l.MinTexelGatherOffset = -8;
        l.MaxTexelGatherOffset = 7;
        l.MinInterpolationOffset = -0.5f;
        l.MaxInterpolationOffset = 0.4375f;
        l.SubPixelInterpolationOffsetBits = 4;
        l.MaxFramebufferWidth = g.MaxTextureDimension2D;
        l.MaxFramebufferHeight = g.MaxTextureDimension2D;
        l.MaxFramebufferLayers = 1;
        const VkSampleCountFlags counts = VkSampleCountFlags._1Bit | VkSampleCountFlags._4Bit;
        l.FramebufferColorSampleCounts = counts;
        l.FramebufferDepthSampleCounts = counts;
        l.FramebufferStencilSampleCounts = counts;
        l.FramebufferNoAttachmentsSampleCounts = counts;
        l.MaxColorAttachments = g.MaxColorAttachments;
        l.SampledImageColorSampleCounts = counts;
        l.SampledImageIntegerSampleCounts = counts;
        l.SampledImageDepthSampleCounts = counts;
        l.SampledImageStencilSampleCounts = counts;
        l.StorageImageSampleCounts = VkSampleCountFlags._1Bit;
        l.MaxSampleMaskWords = 1;
        l.TimestampComputeAndGraphics = false;
        l.TimestampPeriod = 1f;
        l.MaxClipDistances = 0;
        l.MaxCullDistances = 0;
        l.MaxCombinedClipAndCullDistances = 0;
        l.DiscreteQueuePriorities = 2;
        l.PointSizeRange = new float2(1f, 1f);
        l.LineWidthRange = new float2(1f, 1f);
        l.PointSizeGranularity = 0f;
        l.LineWidthGranularity = 0f;
        l.StrictLines = false;
        l.StandardSampleLocations = true;
        l.OptimalBufferCopyOffsetAlignment = (ByteSize64)256UL;
        l.OptimalBufferCopyRowPitchAlignment = (ByteSize64)256UL;
        l.NonCoherentAtomSize = (ByteSize64)4UL;
        p->SparseProperties = default;
    }

    public static void FillFeatures(VkPhysicalDeviceFeatures* f)
    {
        EnsureInitialized();
        *f = default;
        f->RobustBufferAccess = true;
        f->FullDrawIndexUint32 = true;
        f->ImageCubeArray = true;
        f->IndependentBlend = true;
        f->DepthBiasClamp = true;
        f->SamplerAnisotropy = true;
        f->VertexPipelineStoresAndAtomics = true;
        f->FragmentStoresAndAtomics = true;
        // optional WebGPU features (present only when the device was created with them)
        f->DepthClamp = s_features.Contains("depth-clip-control");
        f->DualSrcBlend = s_features.Contains("dual-source-blending");
        f->ShaderClipDistance = s_features.Contains("clip-distances");
        f->TextureCompressionBC = s_features.Contains("texture-compression-bc");
        f->TextureCompressionETC2 = s_features.Contains("texture-compression-etc2");
        f->TextureCompressionASTCLDR = s_features.Contains("texture-compression-astc");
    }

    public static void FillVulkan11Features(VkPhysicalDeviceVulkan11Features* f) => ClearBools(f, sizeof(VkPhysicalDeviceVulkan11Features));

    public static void FillVulkan12Features(VkPhysicalDeviceVulkan12Features* f)
    {
        ClearBools(f, sizeof(VkPhysicalDeviceVulkan12Features));
        f->ImagelessFramebuffer = true;
        f->SeparateDepthStencilLayouts = true;
        f->HostQueryReset = true;
    }

    // Non-conformant 1.3 device: bufferDeviceAddress, descriptorIndexing, privateData, inlineUniformBlock, subgroupSizeControl, ... stay false.
    public static void FillVulkan13Features(VkPhysicalDeviceVulkan13Features* f)
    {
        ClearBools(f, sizeof(VkPhysicalDeviceVulkan13Features));
        f->RobustImageAccess = true;
        f->ShaderDemoteToHelperInvocation = true;
        f->ShaderTerminateInvocation = true;
        f->Synchronization2 = true;
        f->ShaderZeroInitializeWorkgroupMemory = true;
        f->DynamicRendering = true;
        f->Maintenance4 = true;
    }

    public static void FillPortabilitySubsetFeatures(VkPhysicalDevicePortabilitySubsetFeaturesKHR* f) =>
        ClearBools(f, sizeof(VkPhysicalDevicePortabilitySubsetFeaturesKHR));

    // ---- Vulkan 1.1 / 1.2 / 1.3 property structs (everything not set here is zero: no subgroups, no inline uniform blocks, no update-after-bind) ----

    public static void FillVulkan11Properties(VkPhysicalDeviceVulkan11Properties* p)
    {
        ClearBools(p, sizeof(VkPhysicalDeviceVulkan11Properties));
        p->MaxPerSetDescriptors = MaxPerSetDescriptors;
        p->MaxMemoryAllocationSize = (ByteSize64)(ulong)Limits.MaxBufferSize;
    }

    public static void FillVulkan12Properties(VkPhysicalDeviceVulkan12Properties* p)
    {
        ClearBools(p, sizeof(VkPhysicalDeviceVulkan12Properties));
        WriteUtf8(p->DriverName, 256, DriverName);
        WriteUtf8(p->DriverInfo, 256, Name);
        p->FramebufferIntegerColorSampleCounts = VkSampleCountFlags._1Bit; // conformanceVersion stays 0.0.0.0: not conformant
    }

    public static void FillVulkan13Properties(VkPhysicalDeviceVulkan13Properties* p)
    {
        ClearBools(p, sizeof(VkPhysicalDeviceVulkan13Properties));
        p->MaxBufferSize = (ByteSize64)(ulong)Limits.MaxBufferSize;
    }

    /// <summary>Zeroes the members of a pNext feature/property struct (everything after sType/pNext at offset 16).</summary>
    public static void ClearBools(void* structure, int size) => new Span<byte>((byte*)structure + 16, size - 16).Clear();

    public static void FillMemoryProperties(VkPhysicalDeviceMemoryProperties* m)
    {
        *m = default;
        m->MemoryHeapCount = 2;
        m->MemoryHeaps[0] = new VkMemoryHeap { Size = (ByteSize64)(2UL << 30), Flags = VkMemoryHeapFlags.DeviceLocalBit };
        m->MemoryHeaps[1] = new VkMemoryHeap { Size = (ByteSize64)(512UL << 20), Flags = VkMemoryHeapFlags.None };
        m->MemoryTypeCount = 2;
        m->MemoryTypes[0] = new VkMemoryType { PropertyFlags = VkMemoryPropertyFlags.DeviceLocalBit, HeapIndex = 0 };
        m->MemoryTypes[1] = new VkMemoryType
        {
            PropertyFlags = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit,
            HeapIndex = 1,
        };
    }

    public const int DeviceLocalType = 0;
    public const int HostVisibleType = 1;

    public static void FillQueueFamily(VkQueueFamilyProperties* q)
    {
        q->QueueFlags = VkQueueFlags.GraphicsBit | VkQueueFlags.ComputeBit | VkQueueFlags.TransferBit;
        q->QueueCount = 1;
        q->TimestampValidBits = 0;
        q->MinImageTransferGranularity = new VkExtent3D(1, 1, 1);
    }

    public static void WriteUtf8(byte* destination, int capacity, string value)
    {
        var span = new Span<byte>(destination, capacity);
        span.Clear();
        var n = Encoding.UTF8.GetBytes(value.AsSpan(), span[..(capacity - 1)]);
        span[n] = 0;
    }
}
