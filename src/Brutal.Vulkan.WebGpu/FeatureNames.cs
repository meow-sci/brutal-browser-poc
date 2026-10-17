// Bool32 member names of the feature structs (by offset), for vkCreateDevice diagnostics. Generated from BRUTAL's struct layouts.
namespace Brutal.Vulkan.WebGpu;

internal static class FeatureNames
{
    /// <summary>VkPhysicalDeviceFeatures: first Bool32 at offset 0.</summary>
    public static readonly string[] Features =
    [
        "RobustBufferAccess", "FullDrawIndexUint32", "ImageCubeArray", "IndependentBlend", "GeometryShader", "TessellationShader",
        "SampleRateShading", "DualSrcBlend", "LogicOp", "MultiDrawIndirect", "DrawIndirectFirstInstance", "DepthClamp",
        "DepthBiasClamp", "FillModeNonSolid", "DepthBounds", "WideLines", "LargePoints", "AlphaToOne",
        "MultiViewport", "SamplerAnisotropy", "TextureCompressionETC2", "TextureCompressionASTCLDR", "TextureCompressionBC", "OcclusionQueryPrecise",
        "PipelineStatisticsQuery", "VertexPipelineStoresAndAtomics", "FragmentStoresAndAtomics", "ShaderTessellationAndGeometryPointSize", "ShaderImageGatherExtended", "ShaderStorageImageExtendedFormats",
        "ShaderStorageImageMultisample", "ShaderStorageImageReadWithoutFormat", "ShaderStorageImageWriteWithoutFormat", "ShaderUniformBufferArrayDynamicIndexing", "ShaderSampledImageArrayDynamicIndexing", "ShaderStorageBufferArrayDynamicIndexing",
        "ShaderStorageImageArrayDynamicIndexing", "ShaderClipDistance", "ShaderCullDistance", "ShaderFloat64", "ShaderInt64", "ShaderInt16",
        "ShaderResourceResidency", "ShaderResourceMinLod", "SparseBinding", "SparseResidencyBuffer", "SparseResidencyImage2D", "SparseResidencyImage3D",
        "SparseResidency2Samples", "SparseResidency4Samples", "SparseResidency8Samples", "SparseResidency16Samples", "SparseResidencyAliased", "VariableMultisampleRate",
        "InheritedQueries",
    ];
    /// <summary>VkPhysicalDeviceVulkan11Features: first Bool32 at offset 16.</summary>
    public static readonly string[] Vulkan11Features =
    [
        "StorageBuffer16BitAccess", "UniformAndStorageBuffer16BitAccess", "StoragePushConstant16", "StorageInputOutput16", "Multiview", "MultiviewGeometryShader",
        "MultiviewTessellationShader", "VariablePointersStorageBuffer", "VariablePointers", "ProtectedMemory", "SamplerYcbcrConversion", "ShaderDrawParameters",
    ];
    /// <summary>VkPhysicalDeviceVulkan12Features: first Bool32 at offset 16.</summary>
    public static readonly string[] Vulkan12Features =
    [
        "SamplerMirrorClampToEdge", "DrawIndirectCount", "StorageBuffer8BitAccess", "UniformAndStorageBuffer8BitAccess", "StoragePushConstant8", "ShaderBufferInt64Atomics",
        "ShaderSharedInt64Atomics", "ShaderFloat16", "ShaderInt8", "DescriptorIndexing", "ShaderInputAttachmentArrayDynamicIndexing", "ShaderUniformTexelBufferArrayDynamicIndexing",
        "ShaderStorageTexelBufferArrayDynamicIndexing", "ShaderUniformBufferArrayNonUniformIndexing", "ShaderSampledImageArrayNonUniformIndexing", "ShaderStorageBufferArrayNonUniformIndexing", "ShaderStorageImageArrayNonUniformIndexing", "ShaderInputAttachmentArrayNonUniformIndexing",
        "ShaderUniformTexelBufferArrayNonUniformIndexing", "ShaderStorageTexelBufferArrayNonUniformIndexing", "DescriptorBindingUniformBufferUpdateAfterBind", "DescriptorBindingSampledImageUpdateAfterBind", "DescriptorBindingStorageImageUpdateAfterBind", "DescriptorBindingStorageBufferUpdateAfterBind",
        "DescriptorBindingUniformTexelBufferUpdateAfterBind", "DescriptorBindingStorageTexelBufferUpdateAfterBind", "DescriptorBindingUpdateUnusedWhilePending", "DescriptorBindingPartiallyBound", "DescriptorBindingVariableDescriptorCount", "RuntimeDescriptorArray",
        "SamplerFilterMinmax", "ScalarBlockLayout", "ImagelessFramebuffer", "UniformBufferStandardLayout", "ShaderSubgroupExtendedTypes", "SeparateDepthStencilLayouts",
        "HostQueryReset", "TimelineSemaphore", "BufferDeviceAddress", "BufferDeviceAddressCaptureReplay", "BufferDeviceAddressMultiDevice", "VulkanMemoryModel",
        "VulkanMemoryModelDeviceScope", "VulkanMemoryModelAvailabilityVisibilityChains", "ShaderOutputViewportIndex", "ShaderOutputLayer", "SubgroupBroadcastDynamicId",
    ];
    /// <summary>VkPhysicalDeviceVulkan13Features: first Bool32 at offset 16.</summary>
    public static readonly string[] Vulkan13Features =
    [
        "RobustImageAccess", "InlineUniformBlock", "DescriptorBindingInlineUniformBlockUpdateAfterBind", "PipelineCreationCacheControl", "PrivateData", "ShaderDemoteToHelperInvocation",
        "ShaderTerminateInvocation", "SubgroupSizeControl", "ComputeFullSubgroups", "Synchronization2", "TextureCompressionASTCHdr", "ShaderZeroInitializeWorkgroupMemory",
        "DynamicRendering", "ShaderIntegerDotProduct", "Maintenance4",
    ];
    /// <summary>VkPhysicalDevicePortabilitySubsetFeaturesKHR: first Bool32 at offset 16.</summary>
    public static readonly string[] PortabilitySubsetFeatures =
    [
        "ConstantAlphaColorBlendFactors", "Events", "ImageViewFormatReinterpretation", "ImageViewFormatSwizzle", "ImageView2DOn3DImage", "MultisampleArrayImage",
        "MutableComparisonSamplers", "PointPolygons", "SamplerMipLodBias", "SeparateStencilMaskRef", "ShaderSampleRateInterpolationFunctions", "TessellationIsolines",
        "TessellationPointMode", "TriangleFans", "VertexAttributeAccessBeyondStride",
    ];
}
