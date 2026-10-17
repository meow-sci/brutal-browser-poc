using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// Translation between <see cref="VkFormat"/> and the WebGPU texture, vertex and canvas formats, plus the
/// <see cref="VkFormatFeatureFlags"/> the ICD reports for each format. Source of truth:
/// <c>docs/research/webgpu/format-tables.md</c>. All lookups are allocation-free (switch expressions on enum or string).
/// </summary>
internal static class Formats
{
    // ----- WebGPU device feature names consulted by OptimalTilingFeatures -----
    private const string FeatureTier1 = "texture-formats-tier1";
    private const string FeatureTier2 = "texture-formats-tier2";
    private const string FeatureBc = "texture-compression-bc";
    private const string FeatureEtc2 = "texture-compression-etc2";
    private const string FeatureAstc = "texture-compression-astc";
    private const string FeatureFloat32Filterable = "float32-filterable";
    private const string FeatureFloat32Blendable = "float32-blendable";
    private const string FeatureRg11B10Renderable = "rg11b10ufloat-renderable";
    private const string FeatureBgra8Storage = "bgra8unorm-storage";
    private const string FeatureDepth32Stencil8 = "depth32float-stencil8";

    // ----- Feature letters of format-tables.md (S L C B W D T X) -----
    private const VkFormatFeatureFlags None = VkFormatFeatureFlags.None;
    private const VkFormatFeatureFlags Sampled = VkFormatFeatureFlags.SampledImageBit;                 // S
    private const VkFormatFeatureFlags Linear = VkFormatFeatureFlags.SampledImageFilterLinearBit;      // L
    private const VkFormatFeatureFlags Color = VkFormatFeatureFlags.ColorAttachmentBit;                // C
    private const VkFormatFeatureFlags Blend = VkFormatFeatureFlags.ColorAttachmentBlendBit;           // B
    private const VkFormatFeatureFlags Storage = VkFormatFeatureFlags.StorageImageBit;                 // W
    private const VkFormatFeatureFlags DepthStencil = VkFormatFeatureFlags.DepthStencilAttachmentBit;  // D
    private const VkFormatFeatureFlags CopySrc = VkFormatFeatureFlags.TransferSrcBit;
    private const VkFormatFeatureFlags Copy = VkFormatFeatureFlags.TransferSrcBit | VkFormatFeatureFlags.TransferDstBit; // T
    private const VkFormatFeatureFlags Blit = VkFormatFeatureFlags.BlitSrcBit | VkFormatFeatureFlags.BlitDstBit; // X
    private const VkFormatFeatureFlags BlitSrcOnly = VkFormatFeatureFlags.BlitSrcBit;

    /// <summary>S L C B T X: filterable, renderable, blendable colour format (the "workhorse" row).</summary>
    private const VkFormatFeatureFlags FilterableRenderable = Sampled | Linear | Color | Blend | Copy | Blit;

    /// <summary>S C T: integer colour format (no filtering, no blending).</summary>
    private const VkFormatFeatureFlags IntegerRenderable = Sampled | Color | Copy;

    /// <summary>S L T: filterable, not renderable.</summary>
    private const VkFormatFeatureFlags FilterableOnly = Sampled | Linear | Copy;

    /// <summary>S L T + BlitSrc: block-compressed formats when the family feature is present.</summary>
    private const VkFormatFeatureFlags CompressedSampled = Sampled | Linear | Copy | BlitSrcOnly;

    // ======================================================================================================
    // Texture formats
    // ======================================================================================================

    /// <summary>
    /// WebGPU texture format for <paramref name="format"/>, or <c>null</c> when WebGPU has no equivalent
    /// (3-component, packed 16-bit, scaled, 64-bit, D16S8, YCbCr, BC1 RGB, PVRTC, ASTC HDR ...).
    /// </summary>
    public static string? ToGpuTextureFormat(VkFormat format) => format switch
    {
        // 8-bit
        VkFormat.R8UNorm => "r8unorm",
        VkFormat.R8SNorm => "r8snorm",
        VkFormat.R8UInt => "r8uint",
        VkFormat.R8SInt => "r8sint",
        VkFormat.R8G8UNorm => "rg8unorm",
        VkFormat.R8G8SNorm => "rg8snorm",
        VkFormat.R8G8UInt => "rg8uint",
        VkFormat.R8G8SInt => "rg8sint",
        VkFormat.R8G8B8A8UNorm => "rgba8unorm",
        VkFormat.R8G8B8A8SRGB => "rgba8unorm-srgb",
        VkFormat.R8G8B8A8SNorm => "rgba8snorm",
        VkFormat.R8G8B8A8UInt => "rgba8uint",
        VkFormat.R8G8B8A8SInt => "rgba8sint",
        VkFormat.B8G8R8A8UNorm => "bgra8unorm",
        VkFormat.B8G8R8A8SRGB => "bgra8unorm-srgb",

        // 16-bit (unorm / snorm need texture-formats-tier1)
        VkFormat.R16UNorm => "r16unorm",
        VkFormat.R16SNorm => "r16snorm",
        VkFormat.R16UInt => "r16uint",
        VkFormat.R16SInt => "r16sint",
        VkFormat.R16SFloat => "r16float",
        VkFormat.R16G16UNorm => "rg16unorm",
        VkFormat.R16G16SNorm => "rg16snorm",
        VkFormat.R16G16UInt => "rg16uint",
        VkFormat.R16G16SInt => "rg16sint",
        VkFormat.R16G16SFloat => "rg16float",
        VkFormat.R16G16B16A16UNorm => "rgba16unorm",
        VkFormat.R16G16B16A16SNorm => "rgba16snorm",
        VkFormat.R16G16B16A16UInt => "rgba16uint",
        VkFormat.R16G16B16A16SInt => "rgba16sint",
        VkFormat.R16G16B16A16SFloat => "rgba16float",

        // 32-bit
        VkFormat.R32UInt => "r32uint",
        VkFormat.R32SInt => "r32sint",
        VkFormat.R32SFloat => "r32float",
        VkFormat.R32G32UInt => "rg32uint",
        VkFormat.R32G32SInt => "rg32sint",
        VkFormat.R32G32SFloat => "rg32float",
        VkFormat.R32G32B32A32UInt => "rgba32uint",
        VkFormat.R32G32B32A32SInt => "rgba32sint",
        VkFormat.R32G32B32A32SFloat => "rgba32float",

        // packed
        VkFormat.A2B10G10R10UNormPack32 => "rgb10a2unorm",
        VkFormat.A2B10G10R10UIntPack32 => "rgb10a2uint",
        VkFormat.B10G11R11UfloatPack32 => "rg11b10ufloat",
        VkFormat.E5B9G9R9UfloatPack32 => "rgb9e5ufloat",

        // depth / stencil
        VkFormat.D16UNorm => "depth16unorm",
        VkFormat.X8D24UNormPack32 => "depth24plus",
        VkFormat.D24UNormS8UInt => "depth24plus-stencil8",
        VkFormat.D32SFloat => "depth32float",
        VkFormat.D32SFloatS8UInt => "depth32float-stencil8",
        VkFormat.S8UInt => "stencil8",

        // BC (texture-compression-bc); BC1 RGB has no WebGPU equivalent
        VkFormat.Bc1RGBAUNormBlock => "bc1-rgba-unorm",
        VkFormat.Bc1RGBASRGBBlock => "bc1-rgba-unorm-srgb",
        VkFormat.Bc2UNormBlock => "bc2-rgba-unorm",
        VkFormat.Bc2SRGBBlock => "bc2-rgba-unorm-srgb",
        VkFormat.Bc3UNormBlock => "bc3-rgba-unorm",
        VkFormat.Bc3SRGBBlock => "bc3-rgba-unorm-srgb",
        VkFormat.Bc4UNormBlock => "bc4-r-unorm",
        VkFormat.Bc4SNormBlock => "bc4-r-snorm",
        VkFormat.Bc5UNormBlock => "bc5-rg-unorm",
        VkFormat.Bc5SNormBlock => "bc5-rg-snorm",
        VkFormat.Bc6HUfloatBlock => "bc6h-rgb-ufloat",
        VkFormat.Bc6HSFloatBlock => "bc6h-rgb-float",
        VkFormat.Bc7UNormBlock => "bc7-rgba-unorm",
        VkFormat.Bc7SRGBBlock => "bc7-rgba-unorm-srgb",

        // ETC2 / EAC (texture-compression-etc2)
        VkFormat.Etc2R8G8B8UNormBlock => "etc2-rgb8unorm",
        VkFormat.Etc2R8G8B8SRGBBlock => "etc2-rgb8unorm-srgb",
        VkFormat.Etc2R8G8B8A1UNormBlock => "etc2-rgb8a1unorm",
        VkFormat.Etc2R8G8B8A1SRGBBlock => "etc2-rgb8a1unorm-srgb",
        VkFormat.Etc2R8G8B8A8UNormBlock => "etc2-rgba8unorm",
        VkFormat.Etc2R8G8B8A8SRGBBlock => "etc2-rgba8unorm-srgb",
        VkFormat.EacR11UNormBlock => "eac-r11unorm",
        VkFormat.EacR11SNormBlock => "eac-r11snorm",
        VkFormat.EacR11G11UNormBlock => "eac-rg11unorm",
        VkFormat.EacR11G11SNormBlock => "eac-rg11snorm",

        // ASTC LDR (texture-compression-astc)
        VkFormat.Astc4X4UNormBlock => "astc-4x4-unorm",
        VkFormat.Astc4X4SRGBBlock => "astc-4x4-unorm-srgb",
        VkFormat.Astc5X4UNormBlock => "astc-5x4-unorm",
        VkFormat.Astc5X4SRGBBlock => "astc-5x4-unorm-srgb",
        VkFormat.Astc5X5UNormBlock => "astc-5x5-unorm",
        VkFormat.Astc5X5SRGBBlock => "astc-5x5-unorm-srgb",
        VkFormat.Astc6X5UNormBlock => "astc-6x5-unorm",
        VkFormat.Astc6X5SRGBBlock => "astc-6x5-unorm-srgb",
        VkFormat.Astc6X6UNormBlock => "astc-6x6-unorm",
        VkFormat.Astc6X6SRGBBlock => "astc-6x6-unorm-srgb",
        VkFormat.Astc8X5UNormBlock => "astc-8x5-unorm",
        VkFormat.Astc8X5SRGBBlock => "astc-8x5-unorm-srgb",
        VkFormat.Astc8X6UNormBlock => "astc-8x6-unorm",
        VkFormat.Astc8X6SRGBBlock => "astc-8x6-unorm-srgb",
        VkFormat.Astc8X8UNormBlock => "astc-8x8-unorm",
        VkFormat.Astc8X8SRGBBlock => "astc-8x8-unorm-srgb",
        VkFormat.Astc10X5UNormBlock => "astc-10x5-unorm",
        VkFormat.Astc10X5SRGBBlock => "astc-10x5-unorm-srgb",
        VkFormat.Astc10X6UNormBlock => "astc-10x6-unorm",
        VkFormat.Astc10X6SRGBBlock => "astc-10x6-unorm-srgb",
        VkFormat.Astc10X8UNormBlock => "astc-10x8-unorm",
        VkFormat.Astc10X8SRGBBlock => "astc-10x8-unorm-srgb",
        VkFormat.Astc10X10UNormBlock => "astc-10x10-unorm",
        VkFormat.Astc10X10SRGBBlock => "astc-10x10-unorm-srgb",
        VkFormat.Astc12X10UNormBlock => "astc-12x10-unorm",
        VkFormat.Astc12X10SRGBBlock => "astc-12x10-unorm-srgb",
        VkFormat.Astc12X12UNormBlock => "astc-12x12-unorm",
        VkFormat.Astc12X12SRGBBlock => "astc-12x12-unorm-srgb",

        _ => null,
    };

    /// <summary>
    /// Inverse of <see cref="ToGpuTextureFormat"/>: the VkFormat for a WebGPU texture format string, or
    /// <see cref="VkFormat.Undefined"/> for unknown strings (e.g. the canvas format <c>"bgra8unorm"</c> gives
    /// <see cref="VkFormat.B8G8R8A8UNorm"/>).
    /// </summary>
    public static VkFormat FromGpuTextureFormat(string gpuFormat) => gpuFormat switch
    {
        "r8unorm" => VkFormat.R8UNorm,
        "r8snorm" => VkFormat.R8SNorm,
        "r8uint" => VkFormat.R8UInt,
        "r8sint" => VkFormat.R8SInt,
        "rg8unorm" => VkFormat.R8G8UNorm,
        "rg8snorm" => VkFormat.R8G8SNorm,
        "rg8uint" => VkFormat.R8G8UInt,
        "rg8sint" => VkFormat.R8G8SInt,
        "rgba8unorm" => VkFormat.R8G8B8A8UNorm,
        "rgba8unorm-srgb" => VkFormat.R8G8B8A8SRGB,
        "rgba8snorm" => VkFormat.R8G8B8A8SNorm,
        "rgba8uint" => VkFormat.R8G8B8A8UInt,
        "rgba8sint" => VkFormat.R8G8B8A8SInt,
        "bgra8unorm" => VkFormat.B8G8R8A8UNorm,
        "bgra8unorm-srgb" => VkFormat.B8G8R8A8SRGB,

        "r16unorm" => VkFormat.R16UNorm,
        "r16snorm" => VkFormat.R16SNorm,
        "r16uint" => VkFormat.R16UInt,
        "r16sint" => VkFormat.R16SInt,
        "r16float" => VkFormat.R16SFloat,
        "rg16unorm" => VkFormat.R16G16UNorm,
        "rg16snorm" => VkFormat.R16G16SNorm,
        "rg16uint" => VkFormat.R16G16UInt,
        "rg16sint" => VkFormat.R16G16SInt,
        "rg16float" => VkFormat.R16G16SFloat,
        "rgba16unorm" => VkFormat.R16G16B16A16UNorm,
        "rgba16snorm" => VkFormat.R16G16B16A16SNorm,
        "rgba16uint" => VkFormat.R16G16B16A16UInt,
        "rgba16sint" => VkFormat.R16G16B16A16SInt,
        "rgba16float" => VkFormat.R16G16B16A16SFloat,

        "r32uint" => VkFormat.R32UInt,
        "r32sint" => VkFormat.R32SInt,
        "r32float" => VkFormat.R32SFloat,
        "rg32uint" => VkFormat.R32G32UInt,
        "rg32sint" => VkFormat.R32G32SInt,
        "rg32float" => VkFormat.R32G32SFloat,
        "rgba32uint" => VkFormat.R32G32B32A32UInt,
        "rgba32sint" => VkFormat.R32G32B32A32SInt,
        "rgba32float" => VkFormat.R32G32B32A32SFloat,

        "rgb10a2unorm" => VkFormat.A2B10G10R10UNormPack32,
        "rgb10a2uint" => VkFormat.A2B10G10R10UIntPack32,
        "rg11b10ufloat" => VkFormat.B10G11R11UfloatPack32,
        "rgb9e5ufloat" => VkFormat.E5B9G9R9UfloatPack32,

        "depth16unorm" => VkFormat.D16UNorm,
        "depth24plus" => VkFormat.X8D24UNormPack32,
        "depth24plus-stencil8" => VkFormat.D24UNormS8UInt,
        "depth32float" => VkFormat.D32SFloat,
        "depth32float-stencil8" => VkFormat.D32SFloatS8UInt,
        "stencil8" => VkFormat.S8UInt,

        "bc1-rgba-unorm" => VkFormat.Bc1RGBAUNormBlock,
        "bc1-rgba-unorm-srgb" => VkFormat.Bc1RGBASRGBBlock,
        "bc2-rgba-unorm" => VkFormat.Bc2UNormBlock,
        "bc2-rgba-unorm-srgb" => VkFormat.Bc2SRGBBlock,
        "bc3-rgba-unorm" => VkFormat.Bc3UNormBlock,
        "bc3-rgba-unorm-srgb" => VkFormat.Bc3SRGBBlock,
        "bc4-r-unorm" => VkFormat.Bc4UNormBlock,
        "bc4-r-snorm" => VkFormat.Bc4SNormBlock,
        "bc5-rg-unorm" => VkFormat.Bc5UNormBlock,
        "bc5-rg-snorm" => VkFormat.Bc5SNormBlock,
        "bc6h-rgb-ufloat" => VkFormat.Bc6HUfloatBlock,
        "bc6h-rgb-float" => VkFormat.Bc6HSFloatBlock,
        "bc7-rgba-unorm" => VkFormat.Bc7UNormBlock,
        "bc7-rgba-unorm-srgb" => VkFormat.Bc7SRGBBlock,

        "etc2-rgb8unorm" => VkFormat.Etc2R8G8B8UNormBlock,
        "etc2-rgb8unorm-srgb" => VkFormat.Etc2R8G8B8SRGBBlock,
        "etc2-rgb8a1unorm" => VkFormat.Etc2R8G8B8A1UNormBlock,
        "etc2-rgb8a1unorm-srgb" => VkFormat.Etc2R8G8B8A1SRGBBlock,
        "etc2-rgba8unorm" => VkFormat.Etc2R8G8B8A8UNormBlock,
        "etc2-rgba8unorm-srgb" => VkFormat.Etc2R8G8B8A8SRGBBlock,
        "eac-r11unorm" => VkFormat.EacR11UNormBlock,
        "eac-r11snorm" => VkFormat.EacR11SNormBlock,
        "eac-rg11unorm" => VkFormat.EacR11G11UNormBlock,
        "eac-rg11snorm" => VkFormat.EacR11G11SNormBlock,

        "astc-4x4-unorm" => VkFormat.Astc4X4UNormBlock,
        "astc-4x4-unorm-srgb" => VkFormat.Astc4X4SRGBBlock,
        "astc-5x4-unorm" => VkFormat.Astc5X4UNormBlock,
        "astc-5x4-unorm-srgb" => VkFormat.Astc5X4SRGBBlock,
        "astc-5x5-unorm" => VkFormat.Astc5X5UNormBlock,
        "astc-5x5-unorm-srgb" => VkFormat.Astc5X5SRGBBlock,
        "astc-6x5-unorm" => VkFormat.Astc6X5UNormBlock,
        "astc-6x5-unorm-srgb" => VkFormat.Astc6X5SRGBBlock,
        "astc-6x6-unorm" => VkFormat.Astc6X6UNormBlock,
        "astc-6x6-unorm-srgb" => VkFormat.Astc6X6SRGBBlock,
        "astc-8x5-unorm" => VkFormat.Astc8X5UNormBlock,
        "astc-8x5-unorm-srgb" => VkFormat.Astc8X5SRGBBlock,
        "astc-8x6-unorm" => VkFormat.Astc8X6UNormBlock,
        "astc-8x6-unorm-srgb" => VkFormat.Astc8X6SRGBBlock,
        "astc-8x8-unorm" => VkFormat.Astc8X8UNormBlock,
        "astc-8x8-unorm-srgb" => VkFormat.Astc8X8SRGBBlock,
        "astc-10x5-unorm" => VkFormat.Astc10X5UNormBlock,
        "astc-10x5-unorm-srgb" => VkFormat.Astc10X5SRGBBlock,
        "astc-10x6-unorm" => VkFormat.Astc10X6UNormBlock,
        "astc-10x6-unorm-srgb" => VkFormat.Astc10X6SRGBBlock,
        "astc-10x8-unorm" => VkFormat.Astc10X8UNormBlock,
        "astc-10x8-unorm-srgb" => VkFormat.Astc10X8SRGBBlock,
        "astc-10x10-unorm" => VkFormat.Astc10X10UNormBlock,
        "astc-10x10-unorm-srgb" => VkFormat.Astc10X10SRGBBlock,
        "astc-12x10-unorm" => VkFormat.Astc12X10UNormBlock,
        "astc-12x10-unorm-srgb" => VkFormat.Astc12X10SRGBBlock,
        "astc-12x12-unorm" => VkFormat.Astc12X12UNormBlock,
        "astc-12x12-unorm-srgb" => VkFormat.Astc12X12SRGBBlock,

        _ => VkFormat.Undefined,
    };

    /// <summary>
    /// The sRGB / non-sRGB sibling of <paramref name="format"/> (UNORM &lt;-&gt; SRGB), or <see cref="VkFormat.Undefined"/>
    /// when the format has none. Only pairs where both sides map to a WebGPU texture format are returned (so a mapped
    /// format always has a mapped twin); e.g. R8_UNORM / R8_SRGB and BC1 RGB have no twin here.
    /// </summary>
    public static VkFormat SrgbTwin(VkFormat format) => format switch
    {
        VkFormat.R8G8B8A8UNorm => VkFormat.R8G8B8A8SRGB,
        VkFormat.R8G8B8A8SRGB => VkFormat.R8G8B8A8UNorm,
        VkFormat.B8G8R8A8UNorm => VkFormat.B8G8R8A8SRGB,
        VkFormat.B8G8R8A8SRGB => VkFormat.B8G8R8A8UNorm,

        VkFormat.Bc1RGBAUNormBlock => VkFormat.Bc1RGBASRGBBlock,
        VkFormat.Bc1RGBASRGBBlock => VkFormat.Bc1RGBAUNormBlock,
        VkFormat.Bc2UNormBlock => VkFormat.Bc2SRGBBlock,
        VkFormat.Bc2SRGBBlock => VkFormat.Bc2UNormBlock,
        VkFormat.Bc3UNormBlock => VkFormat.Bc3SRGBBlock,
        VkFormat.Bc3SRGBBlock => VkFormat.Bc3UNormBlock,
        VkFormat.Bc7UNormBlock => VkFormat.Bc7SRGBBlock,
        VkFormat.Bc7SRGBBlock => VkFormat.Bc7UNormBlock,

        VkFormat.Etc2R8G8B8UNormBlock => VkFormat.Etc2R8G8B8SRGBBlock,
        VkFormat.Etc2R8G8B8SRGBBlock => VkFormat.Etc2R8G8B8UNormBlock,
        VkFormat.Etc2R8G8B8A1UNormBlock => VkFormat.Etc2R8G8B8A1SRGBBlock,
        VkFormat.Etc2R8G8B8A1SRGBBlock => VkFormat.Etc2R8G8B8A1UNormBlock,
        VkFormat.Etc2R8G8B8A8UNormBlock => VkFormat.Etc2R8G8B8A8SRGBBlock,
        VkFormat.Etc2R8G8B8A8SRGBBlock => VkFormat.Etc2R8G8B8A8UNormBlock,

        VkFormat.Astc4X4UNormBlock => VkFormat.Astc4X4SRGBBlock,
        VkFormat.Astc4X4SRGBBlock => VkFormat.Astc4X4UNormBlock,
        VkFormat.Astc5X4UNormBlock => VkFormat.Astc5X4SRGBBlock,
        VkFormat.Astc5X4SRGBBlock => VkFormat.Astc5X4UNormBlock,
        VkFormat.Astc5X5UNormBlock => VkFormat.Astc5X5SRGBBlock,
        VkFormat.Astc5X5SRGBBlock => VkFormat.Astc5X5UNormBlock,
        VkFormat.Astc6X5UNormBlock => VkFormat.Astc6X5SRGBBlock,
        VkFormat.Astc6X5SRGBBlock => VkFormat.Astc6X5UNormBlock,
        VkFormat.Astc6X6UNormBlock => VkFormat.Astc6X6SRGBBlock,
        VkFormat.Astc6X6SRGBBlock => VkFormat.Astc6X6UNormBlock,
        VkFormat.Astc8X5UNormBlock => VkFormat.Astc8X5SRGBBlock,
        VkFormat.Astc8X5SRGBBlock => VkFormat.Astc8X5UNormBlock,
        VkFormat.Astc8X6UNormBlock => VkFormat.Astc8X6SRGBBlock,
        VkFormat.Astc8X6SRGBBlock => VkFormat.Astc8X6UNormBlock,
        VkFormat.Astc8X8UNormBlock => VkFormat.Astc8X8SRGBBlock,
        VkFormat.Astc8X8SRGBBlock => VkFormat.Astc8X8UNormBlock,
        VkFormat.Astc10X5UNormBlock => VkFormat.Astc10X5SRGBBlock,
        VkFormat.Astc10X5SRGBBlock => VkFormat.Astc10X5UNormBlock,
        VkFormat.Astc10X6UNormBlock => VkFormat.Astc10X6SRGBBlock,
        VkFormat.Astc10X6SRGBBlock => VkFormat.Astc10X6UNormBlock,
        VkFormat.Astc10X8UNormBlock => VkFormat.Astc10X8SRGBBlock,
        VkFormat.Astc10X8SRGBBlock => VkFormat.Astc10X8UNormBlock,
        VkFormat.Astc10X10UNormBlock => VkFormat.Astc10X10SRGBBlock,
        VkFormat.Astc10X10SRGBBlock => VkFormat.Astc10X10UNormBlock,
        VkFormat.Astc12X10UNormBlock => VkFormat.Astc12X10SRGBBlock,
        VkFormat.Astc12X10SRGBBlock => VkFormat.Astc12X10UNormBlock,
        VkFormat.Astc12X12UNormBlock => VkFormat.Astc12X12SRGBBlock,
        VkFormat.Astc12X12SRGBBlock => VkFormat.Astc12X12UNormBlock,

        _ => VkFormat.Undefined,
    };

    /// <summary>True for every sRGB-encoded colour format (all <c>*_SRGB</c> members, including ones WebGPU cannot represent).</summary>
    public static bool IsSrgb(VkFormat format) => format is
        VkFormat.R8SRGB or VkFormat.R8G8SRGB or VkFormat.R8G8B8SRGB or VkFormat.B8G8R8SRGB
        or VkFormat.R8G8B8A8SRGB or VkFormat.B8G8R8A8SRGB or VkFormat.A8B8G8R8SRGBPack32
        or VkFormat.Bc1RGBSRGBBlock or VkFormat.Bc1RGBASRGBBlock or VkFormat.Bc2SRGBBlock
        or VkFormat.Bc3SRGBBlock or VkFormat.Bc7SRGBBlock
        or VkFormat.Etc2R8G8B8SRGBBlock or VkFormat.Etc2R8G8B8A1SRGBBlock or VkFormat.Etc2R8G8B8A8SRGBBlock
        or VkFormat.Astc4X4SRGBBlock
        or VkFormat.Astc5X4SRGBBlock
        or VkFormat.Astc5X5SRGBBlock
        or VkFormat.Astc6X5SRGBBlock
        or VkFormat.Astc6X6SRGBBlock
        or VkFormat.Astc8X5SRGBBlock
        or VkFormat.Astc8X6SRGBBlock
        or VkFormat.Astc8X8SRGBBlock
        or VkFormat.Astc10X5SRGBBlock
        or VkFormat.Astc10X6SRGBBlock
        or VkFormat.Astc10X8SRGBBlock
        or VkFormat.Astc10X10SRGBBlock
        or VkFormat.Astc12X10SRGBBlock
        or VkFormat.Astc12X12SRGBBlock;

    // ======================================================================================================
    // Format class predicates
    // ======================================================================================================

    /// <summary>True when the format has a depth aspect (D16, X8D24, D24S8, D32, D32S8, and D16S8 although unmapped).</summary>
    public static bool HasDepth(VkFormat format) => format is
        VkFormat.D16UNorm or VkFormat.X8D24UNormPack32 or VkFormat.D32SFloat
        or VkFormat.D16UNormS8UInt or VkFormat.D24UNormS8UInt or VkFormat.D32SFloatS8UInt;

    /// <summary>True when the format has a stencil aspect (S8, D16S8, D24S8, D32S8).</summary>
    public static bool HasStencil(VkFormat format) => format is
        VkFormat.S8UInt or VkFormat.D16UNormS8UInt or VkFormat.D24UNormS8UInt or VkFormat.D32SFloatS8UInt;

    /// <summary>True for any depth and/or stencil format.</summary>
    public static bool IsDepthOrStencil(VkFormat format) => HasDepth(format) || HasStencil(format);

    /// <summary>True for block-compressed formats (BC, ETC2/EAC, ASTC LDR/HDR, PVRTC).</summary>
    public static bool IsCompressed(VkFormat format) => format is
        (>= VkFormat.Bc1RGBUNormBlock and <= VkFormat.Astc12X12SRGBBlock)
        or (>= VkFormat.Astc4X4SFloatBlock and <= VkFormat.Astc12X12SFloatBlock)
        or (>= VkFormat.Pvrtc12BppUNormBlockIMG and <= VkFormat.Pvrtc24BppSRGBBlockIMG);

    /// <summary>True for <c>*_UINT</c> / <c>*_SINT</c> colour formats (not depth/stencil; includes unmapped ones).</summary>
    public static bool IsInteger(VkFormat format) => IntegerKind(format) != 0;

    /// <summary>1 for <c>*_UINT</c> colour formats, 2 for <c>*_SINT</c> colour formats, 0 otherwise.</summary>
    private static int IntegerKind(VkFormat format) => format switch
    {
        VkFormat.R8UInt or VkFormat.R8G8UInt or VkFormat.R8G8B8UInt or VkFormat.B8G8R8UInt
            or VkFormat.R8G8B8A8UInt or VkFormat.B8G8R8A8UInt or VkFormat.A8B8G8R8UIntPack32
            or VkFormat.A2R10G10B10UIntPack32 or VkFormat.A2B10G10R10UIntPack32
            or VkFormat.R16UInt or VkFormat.R16G16UInt or VkFormat.R16G16B16UInt or VkFormat.R16G16B16A16UInt
            or VkFormat.R32UInt or VkFormat.R32G32UInt or VkFormat.R32G32B32UInt or VkFormat.R32G32B32A32UInt
            or VkFormat.R64UInt or VkFormat.R64G64UInt or VkFormat.R64G64B64UInt or VkFormat.R64G64B64A64UInt => 1,
        VkFormat.R8SInt or VkFormat.R8G8SInt or VkFormat.R8G8B8SInt or VkFormat.B8G8R8SInt
            or VkFormat.R8G8B8A8SInt or VkFormat.B8G8R8A8SInt or VkFormat.A8B8G8R8SIntPack32
            or VkFormat.A2R10G10B10SIntPack32 or VkFormat.A2B10G10R10SIntPack32
            or VkFormat.R16SInt or VkFormat.R16G16SInt or VkFormat.R16G16B16SInt or VkFormat.R16G16B16A16SInt
            or VkFormat.R32SInt or VkFormat.R32G32SInt or VkFormat.R32G32B32SInt or VkFormat.R32G32B32A32SInt
            or VkFormat.R64SInt or VkFormat.R64G64SInt or VkFormat.R64G64B64SInt or VkFormat.R64G64B64A64SInt => 2,
        _ => 0,
    };

    /// <summary>
    /// Texel block size: bytes per block and block width/height in texels (1x1 for uncompressed formats).
    /// Depth/stencil formats report the byte size WebGPU copies use for the copyable aspect (D16: 2, D32: 4, S8: 1,
    /// X8D24 and D24S8 depth: 4, D16S8 depth: 2, D32S8 depth: 4). Undefined and unknown formats (YCbCr, 3D ASTC ...) give (0, 1, 1).
    /// </summary>
    public static (int BlockBytes, int BlockWidth, int BlockHeight) BlockInfo(VkFormat format) => format switch
    {
        // 1 byte
        VkFormat.R4G4UNormPack8
            or VkFormat.R8UNorm or VkFormat.R8SNorm or VkFormat.R8UScaled or VkFormat.R8SScaled
            or VkFormat.R8UInt or VkFormat.R8SInt or VkFormat.R8SRGB or VkFormat.A8UNorm
            or VkFormat.S8UInt => (1, 1, 1),

        // 2 bytes
        VkFormat.R4G4B4A4UNormPack16 or VkFormat.B4G4R4A4UNormPack16 or VkFormat.R5G6B5UNormPack16
            or VkFormat.B5G6R5UNormPack16 or VkFormat.R5G5B5A1UNormPack16 or VkFormat.B5G5R5A1UNormPack16
            or VkFormat.A1R5G5B5UNormPack16 or VkFormat.A4R4G4B4UNormPack16 or VkFormat.A4B4G4R4UNormPack16
            or VkFormat.A1B5G5R5UNormPack16
            or VkFormat.R8G8UNorm or VkFormat.R8G8SNorm or VkFormat.R8G8UScaled or VkFormat.R8G8SScaled
            or VkFormat.R8G8UInt or VkFormat.R8G8SInt or VkFormat.R8G8SRGB
            or VkFormat.R16UNorm or VkFormat.R16SNorm or VkFormat.R16UScaled or VkFormat.R16SScaled
            or VkFormat.R16UInt or VkFormat.R16SInt or VkFormat.R16SFloat
            or VkFormat.D16UNorm or VkFormat.D16UNormS8UInt => (2, 1, 1),

        // 3 bytes
        VkFormat.R8G8B8UNorm or VkFormat.R8G8B8SNorm or VkFormat.R8G8B8UScaled or VkFormat.R8G8B8SScaled
            or VkFormat.R8G8B8UInt or VkFormat.R8G8B8SInt or VkFormat.R8G8B8SRGB
            or VkFormat.B8G8R8UNorm or VkFormat.B8G8R8SNorm or VkFormat.B8G8R8UScaled or VkFormat.B8G8R8SScaled
            or VkFormat.B8G8R8UInt or VkFormat.B8G8R8SInt or VkFormat.B8G8R8SRGB => (3, 1, 1),

        // 4 bytes
        VkFormat.R8G8B8A8UNorm or VkFormat.R8G8B8A8SNorm or VkFormat.R8G8B8A8UScaled or VkFormat.R8G8B8A8SScaled
            or VkFormat.R8G8B8A8UInt or VkFormat.R8G8B8A8SInt or VkFormat.R8G8B8A8SRGB
            or VkFormat.B8G8R8A8UNorm or VkFormat.B8G8R8A8SNorm or VkFormat.B8G8R8A8UScaled or VkFormat.B8G8R8A8SScaled
            or VkFormat.B8G8R8A8UInt or VkFormat.B8G8R8A8SInt or VkFormat.B8G8R8A8SRGB
            or VkFormat.A8B8G8R8UNormPack32 or VkFormat.A8B8G8R8SNormPack32 or VkFormat.A8B8G8R8UScaledPack32
            or VkFormat.A8B8G8R8SScaledPack32 or VkFormat.A8B8G8R8UIntPack32 or VkFormat.A8B8G8R8SIntPack32
            or VkFormat.A8B8G8R8SRGBPack32
            or VkFormat.A2R10G10B10UNormPack32 or VkFormat.A2R10G10B10SNormPack32 or VkFormat.A2R10G10B10UScaledPack32
            or VkFormat.A2R10G10B10SScaledPack32 or VkFormat.A2R10G10B10UIntPack32 or VkFormat.A2R10G10B10SIntPack32
            or VkFormat.A2B10G10R10UNormPack32 or VkFormat.A2B10G10R10SNormPack32 or VkFormat.A2B10G10R10UScaledPack32
            or VkFormat.A2B10G10R10SScaledPack32 or VkFormat.A2B10G10R10UIntPack32 or VkFormat.A2B10G10R10SIntPack32
            or VkFormat.R16G16UNorm or VkFormat.R16G16SNorm or VkFormat.R16G16UScaled or VkFormat.R16G16SScaled
            or VkFormat.R16G16UInt or VkFormat.R16G16SInt or VkFormat.R16G16SFloat
            or VkFormat.R32UInt or VkFormat.R32SInt or VkFormat.R32SFloat
            or VkFormat.B10G11R11UfloatPack32 or VkFormat.E5B9G9R9UfloatPack32
            or VkFormat.X8D24UNormPack32 or VkFormat.D32SFloat
            or VkFormat.D24UNormS8UInt or VkFormat.D32SFloatS8UInt => (4, 1, 1),

        // 6 bytes
        VkFormat.R16G16B16UNorm or VkFormat.R16G16B16SNorm or VkFormat.R16G16B16UScaled or VkFormat.R16G16B16SScaled
            or VkFormat.R16G16B16UInt or VkFormat.R16G16B16SInt or VkFormat.R16G16B16SFloat => (6, 1, 1),

        // 8 bytes
        VkFormat.R16G16B16A16UNorm or VkFormat.R16G16B16A16SNorm or VkFormat.R16G16B16A16UScaled
            or VkFormat.R16G16B16A16SScaled or VkFormat.R16G16B16A16UInt or VkFormat.R16G16B16A16SInt
            or VkFormat.R16G16B16A16SFloat
            or VkFormat.R32G32UInt or VkFormat.R32G32SInt or VkFormat.R32G32SFloat
            or VkFormat.R64UInt or VkFormat.R64SInt or VkFormat.R64SFloat => (8, 1, 1),

        // 12 / 16 / 24 / 32 bytes
        VkFormat.R32G32B32UInt or VkFormat.R32G32B32SInt or VkFormat.R32G32B32SFloat => (12, 1, 1),
        VkFormat.R32G32B32A32UInt or VkFormat.R32G32B32A32SInt or VkFormat.R32G32B32A32SFloat
            or VkFormat.R64G64UInt or VkFormat.R64G64SInt or VkFormat.R64G64SFloat => (16, 1, 1),
        VkFormat.R64G64B64UInt or VkFormat.R64G64B64SInt or VkFormat.R64G64B64SFloat => (24, 1, 1),
        VkFormat.R64G64B64A64UInt or VkFormat.R64G64B64A64SInt or VkFormat.R64G64B64A64SFloat => (32, 1, 1),

        // BC: 4x4 blocks
        VkFormat.Bc1RGBUNormBlock or VkFormat.Bc1RGBSRGBBlock or VkFormat.Bc1RGBAUNormBlock or VkFormat.Bc1RGBASRGBBlock
            or VkFormat.Bc4UNormBlock or VkFormat.Bc4SNormBlock => (8, 4, 4),
        VkFormat.Bc2UNormBlock or VkFormat.Bc2SRGBBlock or VkFormat.Bc3UNormBlock or VkFormat.Bc3SRGBBlock
            or VkFormat.Bc5UNormBlock or VkFormat.Bc5SNormBlock or VkFormat.Bc6HUfloatBlock or VkFormat.Bc6HSFloatBlock
            or VkFormat.Bc7UNormBlock or VkFormat.Bc7SRGBBlock => (16, 4, 4),

        // ETC2 / EAC: 4x4 blocks
        VkFormat.Etc2R8G8B8UNormBlock or VkFormat.Etc2R8G8B8SRGBBlock
            or VkFormat.Etc2R8G8B8A1UNormBlock or VkFormat.Etc2R8G8B8A1SRGBBlock
            or VkFormat.EacR11UNormBlock or VkFormat.EacR11SNormBlock => (8, 4, 4),
        VkFormat.Etc2R8G8B8A8UNormBlock or VkFormat.Etc2R8G8B8A8SRGBBlock
            or VkFormat.EacR11G11UNormBlock or VkFormat.EacR11G11SNormBlock => (16, 4, 4),

        // PVRTC (unsupported by WebGPU, sizes for completeness)
        VkFormat.Pvrtc14BppUNormBlockIMG or VkFormat.Pvrtc14BppSRGBBlockIMG
            or VkFormat.Pvrtc24BppUNormBlockIMG or VkFormat.Pvrtc24BppSRGBBlockIMG => (8, 4, 4),
        VkFormat.Pvrtc12BppUNormBlockIMG or VkFormat.Pvrtc12BppSRGBBlockIMG
            or VkFormat.Pvrtc22BppUNormBlockIMG or VkFormat.Pvrtc22BppSRGBBlockIMG => (8, 8, 4),

        // ASTC (LDR UNORM/SRGB and HDR SFLOAT share block sizes): 16 bytes per block
        VkFormat.Astc4X4UNormBlock or VkFormat.Astc4X4SRGBBlock or VkFormat.Astc4X4SFloatBlock => (16, 4, 4),
        VkFormat.Astc5X4UNormBlock or VkFormat.Astc5X4SRGBBlock or VkFormat.Astc5X4SFloatBlock => (16, 5, 4),
        VkFormat.Astc5X5UNormBlock or VkFormat.Astc5X5SRGBBlock or VkFormat.Astc5X5SFloatBlock => (16, 5, 5),
        VkFormat.Astc6X5UNormBlock or VkFormat.Astc6X5SRGBBlock or VkFormat.Astc6X5SFloatBlock => (16, 6, 5),
        VkFormat.Astc6X6UNormBlock or VkFormat.Astc6X6SRGBBlock or VkFormat.Astc6X6SFloatBlock => (16, 6, 6),
        VkFormat.Astc8X5UNormBlock or VkFormat.Astc8X5SRGBBlock or VkFormat.Astc8X5SFloatBlock => (16, 8, 5),
        VkFormat.Astc8X6UNormBlock or VkFormat.Astc8X6SRGBBlock or VkFormat.Astc8X6SFloatBlock => (16, 8, 6),
        VkFormat.Astc8X8UNormBlock or VkFormat.Astc8X8SRGBBlock or VkFormat.Astc8X8SFloatBlock => (16, 8, 8),
        VkFormat.Astc10X5UNormBlock or VkFormat.Astc10X5SRGBBlock or VkFormat.Astc10X5SFloatBlock => (16, 10, 5),
        VkFormat.Astc10X6UNormBlock or VkFormat.Astc10X6SRGBBlock or VkFormat.Astc10X6SFloatBlock => (16, 10, 6),
        VkFormat.Astc10X8UNormBlock or VkFormat.Astc10X8SRGBBlock or VkFormat.Astc10X8SFloatBlock => (16, 10, 8),
        VkFormat.Astc10X10UNormBlock or VkFormat.Astc10X10SRGBBlock or VkFormat.Astc10X10SFloatBlock => (16, 10, 10),
        VkFormat.Astc12X10UNormBlock or VkFormat.Astc12X10SRGBBlock or VkFormat.Astc12X10SFloatBlock => (16, 12, 10),
        VkFormat.Astc12X12UNormBlock or VkFormat.Astc12X12SRGBBlock or VkFormat.Astc12X12SFloatBlock => (16, 12, 12),

        _ => (0, 1, 1),
    };

    // ======================================================================================================
    // Vertex formats
    // ======================================================================================================

    /// <summary>
    /// GPUVertexFormat string for <paramref name="format"/>, or <c>null</c> when WebGPU cannot fetch it
    /// (3-component 8/16-bit, SCALED, 64-bit, ...).
    /// </summary>
    public static string? ToGpuVertexFormat(VkFormat format) => format switch
    {
        VkFormat.R8UInt => "uint8",
        VkFormat.R8G8UInt => "uint8x2",
        VkFormat.R8G8B8A8UInt => "uint8x4",
        VkFormat.R8SInt => "sint8",
        VkFormat.R8G8SInt => "sint8x2",
        VkFormat.R8G8B8A8SInt => "sint8x4",
        VkFormat.R8UNorm => "unorm8",
        VkFormat.R8G8UNorm => "unorm8x2",
        VkFormat.R8G8B8A8UNorm => "unorm8x4",
        VkFormat.R8SNorm => "snorm8",
        VkFormat.R8G8SNorm => "snorm8x2",
        VkFormat.R8G8B8A8SNorm => "snorm8x4",
        VkFormat.B8G8R8A8UNorm => "unorm8x4-bgra",

        VkFormat.R16UInt => "uint16",
        VkFormat.R16G16UInt => "uint16x2",
        VkFormat.R16G16B16A16UInt => "uint16x4",
        VkFormat.R16SInt => "sint16",
        VkFormat.R16G16SInt => "sint16x2",
        VkFormat.R16G16B16A16SInt => "sint16x4",
        VkFormat.R16UNorm => "unorm16",
        VkFormat.R16G16UNorm => "unorm16x2",
        VkFormat.R16G16B16A16UNorm => "unorm16x4",
        VkFormat.R16SNorm => "snorm16",
        VkFormat.R16G16SNorm => "snorm16x2",
        VkFormat.R16G16B16A16SNorm => "snorm16x4",
        VkFormat.R16SFloat => "float16",
        VkFormat.R16G16SFloat => "float16x2",
        VkFormat.R16G16B16A16SFloat => "float16x4",

        VkFormat.R32SFloat => "float32",
        VkFormat.R32G32SFloat => "float32x2",
        VkFormat.R32G32B32SFloat => "float32x3",
        VkFormat.R32G32B32A32SFloat => "float32x4",
        VkFormat.R32UInt => "uint32",
        VkFormat.R32G32UInt => "uint32x2",
        VkFormat.R32G32B32UInt => "uint32x3",
        VkFormat.R32G32B32A32UInt => "uint32x4",
        VkFormat.R32SInt => "sint32",
        VkFormat.R32G32SInt => "sint32x2",
        VkFormat.R32G32B32SInt => "sint32x3",
        VkFormat.R32G32B32A32SInt => "sint32x4",

        VkFormat.A2B10G10R10UNormPack32 => "unorm10-10-10-2",
        VkFormat.A2B10G10R10SNormPack32 => "snorm10-10-10-2",

        _ => null,
    };

    /// <summary>bufferFeatures to report: <see cref="VkFormatFeatureFlags.VertexBufferBit"/> exactly for the formats <see cref="ToGpuVertexFormat"/> maps.</summary>
    public static VkFormatFeatureFlags BufferFeatures(VkFormat format) =>
        ToGpuVertexFormat(format) is null ? None : VkFormatFeatureFlags.VertexBufferBit;

    // ======================================================================================================
    // Format features
    // ======================================================================================================

    /// <summary>
    /// optimalTilingFeatures to report for <paramref name="format"/> given the WebGPU feature names the device has.
    /// Follows the feature letters of format-tables.md (core, non-compatibility values): S SampledImage, L FilterLinear,
    /// C ColorAttachment, B Blend, W StorageImage, D DepthStencilAttachment, T TransferSrc|Dst, X BlitSrc (when S) / BlitDst (when C).
    /// Formats without a WebGPU equivalent, or whose gating feature is missing, report <see cref="VkFormatFeatureFlags.None"/>.
    /// </summary>
    public static VkFormatFeatureFlags OptimalTilingFeatures(VkFormat format, IReadOnlySet<string> gpuFeatures)
    {
        switch (format)
        {
            // ---- 8-bit ----
            case VkFormat.R8UNorm:
            case VkFormat.R8G8UNorm:
                return FilterableRenderable | (Tier1(gpuFeatures) ? Storage : None);
            case VkFormat.R8SNorm:
            case VkFormat.R8G8SNorm:
                return FilterableOnly | (Tier1(gpuFeatures) ? Color | Blend | Storage : None);
            case VkFormat.R8UInt:
            case VkFormat.R8SInt:
            case VkFormat.R8G8UInt:
            case VkFormat.R8G8SInt:
                return IntegerRenderable | (Tier1(gpuFeatures) ? Storage : None);
            case VkFormat.R8G8B8A8UNorm:
                return FilterableRenderable | Storage;
            case VkFormat.R8G8B8A8SRGB:
            case VkFormat.B8G8R8A8SRGB:
                return FilterableRenderable;
            case VkFormat.R8G8B8A8SNorm:
                return FilterableOnly | Storage | (Tier1(gpuFeatures) ? Color | Blend : None);
            case VkFormat.R8G8B8A8UInt:
            case VkFormat.R8G8B8A8SInt:
                return IntegerRenderable | Storage;
            case VkFormat.B8G8R8A8UNorm:
                return FilterableRenderable | (gpuFeatures.Contains(FeatureBgra8Storage) ? Storage : None);

            // ---- 16-bit ----
            case VkFormat.R16UNorm:
            case VkFormat.R16SNorm:
            case VkFormat.R16G16UNorm:
            case VkFormat.R16G16SNorm:
            case VkFormat.R16G16B16A16UNorm:
            case VkFormat.R16G16B16A16SNorm:
                // Exist only with texture-formats-tier1; sample type is unfilterable-float, so no L.
                return Tier1(gpuFeatures) ? Sampled | Color | Blend | Storage | Copy : None;
            case VkFormat.R16UInt:
            case VkFormat.R16SInt:
            case VkFormat.R16G16UInt:
            case VkFormat.R16G16SInt:
                return IntegerRenderable | (Tier1(gpuFeatures) ? Storage : None);
            case VkFormat.R16SFloat:
            case VkFormat.R16G16SFloat:
                return FilterableRenderable | (Tier1(gpuFeatures) ? Storage : None);
            case VkFormat.R16G16B16A16UInt:
            case VkFormat.R16G16B16A16SInt:
                return IntegerRenderable | Storage;
            case VkFormat.R16G16B16A16SFloat:
                return FilterableRenderable | Storage;

            // ---- 32-bit ----
            case VkFormat.R32UInt:
            case VkFormat.R32SInt:
            case VkFormat.R32G32UInt:
            case VkFormat.R32G32SInt:
            case VkFormat.R32G32B32A32UInt:
            case VkFormat.R32G32B32A32SInt:
                return IntegerRenderable | Storage;
            case VkFormat.R32SFloat:
            case VkFormat.R32G32SFloat:
            case VkFormat.R32G32B32A32SFloat:
                return IntegerRenderable | Storage
                    | (gpuFeatures.Contains(FeatureFloat32Filterable) ? Linear : None)
                    | (gpuFeatures.Contains(FeatureFloat32Blendable) ? Blend : None);

            // ---- packed ----
            case VkFormat.A2B10G10R10UNormPack32:
                return FilterableRenderable | (Tier1(gpuFeatures) ? Storage : None);
            case VkFormat.A2B10G10R10UIntPack32:
                return IntegerRenderable | (Tier1(gpuFeatures) ? Storage : None);
            case VkFormat.B10G11R11UfloatPack32:
                return FilterableOnly
                    | (gpuFeatures.Contains(FeatureRg11B10Renderable) || Tier1(gpuFeatures) ? Color | Blend : None)
                    | (Tier1(gpuFeatures) ? Storage : None);
            case VkFormat.E5B9G9R9UfloatPack32:
                return FilterableOnly;

            // ---- depth / stencil (never L) ----
            case VkFormat.D16UNorm:
            case VkFormat.S8UInt:
                return Sampled | DepthStencil | Copy;
            case VkFormat.X8D24UNormPack32:
            case VkFormat.D24UNormS8UInt:
                return Sampled | DepthStencil; // no depth copies at all (stencil-only copies are not representable)
            case VkFormat.D32SFloat:
                return Sampled | DepthStencil | CopySrc;
            case VkFormat.D32SFloatS8UInt:
                return gpuFeatures.Contains(FeatureDepth32Stencil8) ? Sampled | DepthStencil | CopySrc : None;

            // ---- block-compressed: S L T + BlitSrc when the family feature is present ----
            // (VkFormat values are fixed by the Vulkan registry: BC1 RGBA..BC7 = 133..146, ETC2/EAC = 147..156, ASTC LDR = 157..184)
            case >= VkFormat.Bc1RGBAUNormBlock and <= VkFormat.Bc7SRGBBlock:
                return gpuFeatures.Contains(FeatureBc) ? CompressedSampled : None;
            case >= VkFormat.Etc2R8G8B8UNormBlock and <= VkFormat.EacR11G11SNormBlock:
                return gpuFeatures.Contains(FeatureEtc2) ? CompressedSampled : None;
            case >= VkFormat.Astc4X4UNormBlock and <= VkFormat.Astc12X12SRGBBlock:
                return gpuFeatures.Contains(FeatureAstc) ? CompressedSampled : None;

            default:
                return None;
        }
    }

    /// <summary>texture-formats-tier1 (tier2 implies tier1).</summary>
    private static bool Tier1(IReadOnlySet<string> gpuFeatures) =>
        gpuFeatures.Contains(FeatureTier1) || gpuFeatures.Contains(FeatureTier2);

    /// <summary>
    /// WebGPU texture sampleType used for a sampled view of <paramref name="format"/>: <c>"depth"</c> for depth formats,
    /// <c>"uint"</c> / <c>"sint"</c> for integer formats (and the stencil aspect of S8), <c>"unfilterable-float"</c> for
    /// r32float / rg32float / rgba32float and the 16-bit unorm/snorm formats, otherwise <c>"float"</c>.
    /// </summary>
    public static string SampleType(VkFormat format)
    {
        if (HasDepth(format))
        {
            return "depth";
        }

        switch (format)
        {
            case VkFormat.S8UInt:
                return "uint";
            case VkFormat.R32SFloat:
            case VkFormat.R32G32SFloat:
            case VkFormat.R32G32B32A32SFloat:
            case VkFormat.R16UNorm:
            case VkFormat.R16SNorm:
            case VkFormat.R16G16UNorm:
            case VkFormat.R16G16SNorm:
            case VkFormat.R16G16B16A16UNorm:
            case VkFormat.R16G16B16A16SNorm:
                return "unfilterable-float";
        }

        return IntegerKind(format) switch
        {
            1 => "uint",
            2 => "sint",
            _ => "float",
        };
    }

    // ======================================================================================================
    // Surface formats
    // ======================================================================================================

    /// <summary>
    /// The two VkFormats reported by vkGetPhysicalDeviceSurfaceFormatsKHR for a canvas whose preferred format is
    /// <paramref name="preferredCanvasFormat"/> (<c>"bgra8unorm"</c> or <c>"rgba8unorm"</c>): the UNORM format and its SRGB twin.
    /// Any other string falls back to the BGRA pair.
    /// </summary>
    public static (VkFormat Unorm, VkFormat Srgb) SurfaceFormats(string preferredCanvasFormat) =>
        preferredCanvasFormat == "rgba8unorm"
            ? (VkFormat.R8G8B8A8UNorm, VkFormat.R8G8B8A8SRGB)
            : (VkFormat.B8G8R8A8UNorm, VkFormat.B8G8R8A8SRGB);
}
