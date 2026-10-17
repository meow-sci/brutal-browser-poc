using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

/// <summary>Vulkan enum → WebGPU translation tables (docs/research/webgpu/vulkan-to-webgpu-object-mapping.md).</summary>
internal static class Mappings
{
    public static int BufferUsage(VkBufferUsageFlags u)
    {
        // COPY_DST (shadow uploads) and COPY_SRC (readback) are always added; MAP_* never (they exclude other usages).
        var g = GpuBufferUsage.CopyDst | GpuBufferUsage.CopySrc;
        if ((u & VkBufferUsageFlags.UniformBufferBit) != 0) g |= GpuBufferUsage.Uniform;
        if ((u & (VkBufferUsageFlags.StorageBufferBit | VkBufferUsageFlags.UniformTexelBufferBit | VkBufferUsageFlags.StorageTexelBufferBit)) != 0)
            g |= GpuBufferUsage.Storage;
        if ((u & VkBufferUsageFlags.IndexBufferBit) != 0) g |= GpuBufferUsage.Index;
        if ((u & VkBufferUsageFlags.VertexBufferBit) != 0) g |= GpuBufferUsage.Vertex;
        if ((u & VkBufferUsageFlags.IndirectBufferBit) != 0) g |= GpuBufferUsage.Indirect;
        return g;
    }

    public static int TextureUsage(VkImageUsageFlags u)
    {
        var g = 0;
        if ((u & VkImageUsageFlags.TransferSrcBit) != 0) g |= GpuTextureUsage.CopySrc;
        if ((u & VkImageUsageFlags.TransferDstBit) != 0) g |= GpuTextureUsage.CopyDst;
        if ((u & (VkImageUsageFlags.SampledBit | VkImageUsageFlags.InputAttachmentBit)) != 0) g |= GpuTextureUsage.TextureBinding;
        if ((u & VkImageUsageFlags.StorageBit) != 0) g |= GpuTextureUsage.StorageBinding;
        if ((u & (VkImageUsageFlags.ColorAttachmentBit | VkImageUsageFlags.DepthStencilAttachmentBit)) != 0) g |= GpuTextureUsage.RenderAttachment;
        return g;
    }

    public static int Visibility(VkShaderStageFlags s)
    {
        var v = 0;
        if ((s & VkShaderStageFlags.VertexBit) != 0) v |= GpuShaderStage.Vertex;
        if ((s & VkShaderStageFlags.FragmentBit) != 0) v |= GpuShaderStage.Fragment;
        if ((s & VkShaderStageFlags.ComputeBit) != 0) v |= GpuShaderStage.Compute;
        return v;
    }

    public static string? Compare(VkCompareOp op) => op switch
    {
        VkCompareOp.Never => "never",
        VkCompareOp.Less => "less",
        VkCompareOp.Equal => "equal",
        VkCompareOp.LessOrEqual => "less-equal",
        VkCompareOp.Greater => "greater",
        VkCompareOp.NotEqual => "not-equal",
        VkCompareOp.GreaterOrEqual => "greater-equal",
        VkCompareOp.Always => "always",
        _ => null,
    };

    public static string? StencilOp(VkStencilOp op) => op switch
    {
        VkStencilOp.Keep => "keep",
        VkStencilOp.Zero => "zero",
        VkStencilOp.Replace => "replace",
        VkStencilOp.IncrementAndClamp => "increment-clamp",
        VkStencilOp.DecrementAndClamp => "decrement-clamp",
        VkStencilOp.Invert => "invert",
        VkStencilOp.IncrementAndWrap => "increment-wrap",
        VkStencilOp.DecrementAndWrap => "decrement-wrap",
        _ => null,
    };

    public static string? BlendFactor(VkBlendFactor f) => f switch
    {
        VkBlendFactor.Zero => "zero",
        VkBlendFactor.One => "one",
        VkBlendFactor.SrcColor => "src",
        VkBlendFactor.OneMinusSrcColor => "one-minus-src",
        VkBlendFactor.DstColor => "dst",
        VkBlendFactor.OneMinusDstColor => "one-minus-dst",
        VkBlendFactor.SrcAlpha => "src-alpha",
        VkBlendFactor.OneMinusSrcAlpha => "one-minus-src-alpha",
        VkBlendFactor.DstAlpha => "dst-alpha",
        VkBlendFactor.OneMinusDstAlpha => "one-minus-dst-alpha",
        VkBlendFactor.ConstantColor => "constant",
        VkBlendFactor.OneMinusConstantColor => "one-minus-constant",
        VkBlendFactor.SrcAlphaSaturate => "src-alpha-saturated",
        VkBlendFactor.Src1Color => "src1",
        VkBlendFactor.OneMinusSrc1Color => "one-minus-src1",
        VkBlendFactor.Src1Alpha => "src1-alpha",
        VkBlendFactor.OneMinusSrc1Alpha => "one-minus-src1-alpha",
        _ => null, // CONSTANT_ALPHA / ONE_MINUS_CONSTANT_ALPHA: no WebGPU equivalent
    };

    public static string? BlendOp(VkBlendOp op) => op switch
    {
        VkBlendOp.Add => "add",
        VkBlendOp.Subtract => "subtract",
        VkBlendOp.ReverseSubtract => "reverse-subtract",
        VkBlendOp.Min => "min",
        VkBlendOp.Max => "max",
        _ => null,
    };

    public static string? Topology(VkPrimitiveTopology t) => t switch
    {
        VkPrimitiveTopology.PointList => "point-list",
        VkPrimitiveTopology.LineList => "line-list",
        VkPrimitiveTopology.LineStrip => "line-strip",
        VkPrimitiveTopology.TriangleList => "triangle-list",
        VkPrimitiveTopology.TriangleStrip => "triangle-strip",
        _ => null,
    };

    public static string CullMode(VkCullModeFlags c) => c switch
    {
        VkCullModeFlags.FrontBit => "front",
        VkCullModeFlags.BackBit => "back",
        _ => "none",
    };

    public static string FrontFace(VkFrontFace f) => f == VkFrontFace.Clockwise ? "cw" : "ccw";

    public static string Filter(VkFilter f) => f == VkFilter.Linear ? "linear" : "nearest";

    public static string MipmapFilter(VkSamplerMipmapMode m) => m == VkSamplerMipmapMode.Linear ? "linear" : "nearest";

    public static string? AddressMode(VkSamplerAddressMode m) => m switch
    {
        VkSamplerAddressMode.Repeat => "repeat",
        VkSamplerAddressMode.MirroredRepeat => "mirror-repeat",
        VkSamplerAddressMode.ClampToEdge => "clamp-to-edge",
        _ => null,
    };

    public static uint LoadOp(VkAttachmentLoadOp op) => op switch
    {
        VkAttachmentLoadOp.Load => StreamCodes.LoadLoad,
        VkAttachmentLoadOp.None => StreamCodes.LoadLoad,
        _ => StreamCodes.LoadClear, // CLEAR, and DONT_CARE (cheapest defined choice)
    };

    public static uint StoreOp(VkAttachmentStoreOp op) => op == VkAttachmentStoreOp.DontCare ? StreamCodes.StoreDiscard : StreamCodes.StoreStore;

    public static string? TextureDimension(VkImageType t) => t switch
    {
        VkImageType._1D => "1d",
        VkImageType._2D => "2d",
        VkImageType._3D => "3d",
        _ => null,
    };

    public static string? ViewDimension(VkImageViewType t) => t switch
    {
        VkImageViewType._1D => "1d",
        VkImageViewType._2D => "2d",
        VkImageViewType._2DArray => "2d-array",
        VkImageViewType.Cube => "cube",
        VkImageViewType.CubeArray => "cube-array",
        VkImageViewType._3D => "3d",
        _ => null, // 1D_ARRAY
    };

    public static int SampleCount(VkSampleCountFlags s) => s switch
    {
        VkSampleCountFlags._1Bit or VkSampleCountFlags.None => 1,
        VkSampleCountFlags._4Bit => 4,
        _ => -1,
    };

    /// <summary>VK_REMAINING_MIP_LEVELS / VK_REMAINING_ARRAY_LAYERS (~0U) as BRUTAL's int.</summary>
    public const int Remaining = -1;

    /// <summary>VK_WHOLE_SIZE: ~0UL, or 0xFFFFFFFF when widened from BRUTAL's 32-bit ByteSize on wasm32.</summary>
    public static bool IsWholeSize(ulong size) => size == ulong.MaxValue || size == uint.MaxValue;
}
