using Brutal.Vulkan.WebGpu;
using Brutal.VulkanApi;
using Xunit;

// NOTE: the namespace must not be Brutal.Vulkan.* (it would shadow BRUTAL's Vulkan class).
namespace WebGpuIcd.Tests;

public sealed class FormatTests
{
    // Enum.GetValues also yields aliases (KHR/EXT alias names share values); Distinct keeps one entry per value.
    private static readonly VkFormat[] AllFormats = Enum.GetValues<VkFormat>().Distinct().ToArray();

    private static readonly IReadOnlySet<string> NoFeatures = new HashSet<string>();

    private static readonly IReadOnlySet<string> EveryFeature = new HashSet<string>
    {
        "texture-formats-tier1", "texture-formats-tier2",
        "texture-compression-bc", "texture-compression-etc2", "texture-compression-astc",
        "float32-filterable", "float32-blendable", "rg11b10ufloat-renderable", "bgra8unorm-storage",
        "depth32float-stencil8",
    };

    private static IReadOnlySet<string> Features(params string[] names) => new HashSet<string>(names);

    private static List<VkFormat> MappedTextureFormats() =>
        AllFormats.Where(f => Formats.ToGpuTextureFormat(f) is not null).ToList();

    private static List<VkFormat> MappedVertexFormats() =>
        AllFormats.Where(f => Formats.ToGpuVertexFormat(f) is not null).ToList();

    private static void AssertHas(VkFormatFeatureFlags actual, VkFormatFeatureFlags expected) =>
        Assert.True((actual & expected) == expected, $"expected {expected} within {actual}");

    private static void AssertLacks(VkFormatFeatureFlags actual, VkFormatFeatureFlags unexpected) =>
        Assert.True((actual & unexpected) == VkFormatFeatureFlags.None, $"{unexpected} unexpectedly present in {actual}");

    private const VkFormatFeatureFlags Transfer = VkFormatFeatureFlags.TransferSrcBit | VkFormatFeatureFlags.TransferDstBit;

    // ----- texture format mapping -----

    [Fact]
    public void TextureFormat_RoundTripsForEveryMappedFormat()
    {
        var mapped = MappedTextureFormats();
        Assert.NotEmpty(mapped);
        foreach (var format in mapped)
        {
            string gpu = Formats.ToGpuTextureFormat(format)!;
            Assert.Equal(format, Formats.FromGpuTextureFormat(gpu));
        }
    }

    [Fact]
    public void TextureFormat_StringsAreUniqueAndMappedCountIsStable()
    {
        var mapped = MappedTextureFormats();
        var strings = mapped.Select(f => Formats.ToGpuTextureFormat(f)!).ToList();
        Assert.Equal(strings.Count, strings.Distinct().Count());
        // 43 plain colour + 6 depth/stencil + 14 BC + 10 ETC2/EAC + 28 ASTC LDR
        Assert.Equal(101, mapped.Count);
    }

    [Theory]
    [InlineData(VkFormat.R8G8B8A8UNorm, "rgba8unorm")]
    [InlineData(VkFormat.R8G8B8A8SRGB, "rgba8unorm-srgb")]
    [InlineData(VkFormat.B8G8R8A8UNorm, "bgra8unorm")]
    [InlineData(VkFormat.B8G8R8A8SRGB, "bgra8unorm-srgb")]
    [InlineData(VkFormat.R16G16B16A16SFloat, "rgba16float")]
    [InlineData(VkFormat.R32SFloat, "r32float")]
    [InlineData(VkFormat.A2B10G10R10UNormPack32, "rgb10a2unorm")]
    [InlineData(VkFormat.A2B10G10R10UIntPack32, "rgb10a2uint")]
    [InlineData(VkFormat.B10G11R11UfloatPack32, "rg11b10ufloat")]
    [InlineData(VkFormat.E5B9G9R9UfloatPack32, "rgb9e5ufloat")]
    [InlineData(VkFormat.D16UNorm, "depth16unorm")]
    [InlineData(VkFormat.X8D24UNormPack32, "depth24plus")]
    [InlineData(VkFormat.D24UNormS8UInt, "depth24plus-stencil8")]
    [InlineData(VkFormat.D32SFloat, "depth32float")]
    [InlineData(VkFormat.D32SFloatS8UInt, "depth32float-stencil8")]
    [InlineData(VkFormat.S8UInt, "stencil8")]
    [InlineData(VkFormat.Bc1RGBAUNormBlock, "bc1-rgba-unorm")]
    [InlineData(VkFormat.Bc4SNormBlock, "bc4-r-snorm")]
    [InlineData(VkFormat.Bc5UNormBlock, "bc5-rg-unorm")]
    [InlineData(VkFormat.Bc6HUfloatBlock, "bc6h-rgb-ufloat")]
    [InlineData(VkFormat.Bc6HSFloatBlock, "bc6h-rgb-float")]
    [InlineData(VkFormat.Bc7SRGBBlock, "bc7-rgba-unorm-srgb")]
    [InlineData(VkFormat.Etc2R8G8B8A1UNormBlock, "etc2-rgb8a1unorm")]
    [InlineData(VkFormat.Etc2R8G8B8A8SRGBBlock, "etc2-rgba8unorm-srgb")]
    [InlineData(VkFormat.EacR11G11SNormBlock, "eac-rg11snorm")]
    [InlineData(VkFormat.Astc4X4UNormBlock, "astc-4x4-unorm")]
    [InlineData(VkFormat.Astc10X6SRGBBlock, "astc-10x6-unorm-srgb")]
    [InlineData(VkFormat.Astc12X12SRGBBlock, "astc-12x12-unorm-srgb")]
    public void TextureFormat_SpotChecks(VkFormat format, string expected)
    {
        Assert.Equal(expected, Formats.ToGpuTextureFormat(format));
        Assert.Equal(format, Formats.FromGpuTextureFormat(expected));
    }

    [Theory]
    [InlineData(VkFormat.Undefined)]
    [InlineData(VkFormat.R8G8B8UNorm)]
    [InlineData(VkFormat.B8G8R8UNorm)]
    [InlineData(VkFormat.R16G16B16SFloat)]
    [InlineData(VkFormat.R32G32B32SFloat)]
    [InlineData(VkFormat.R5G6B5UNormPack16)]
    [InlineData(VkFormat.R4G4B4A4UNormPack16)]
    [InlineData(VkFormat.R8UScaled)]
    [InlineData(VkFormat.R16G16B16A16SScaled)]
    [InlineData(VkFormat.R8SRGB)]
    [InlineData(VkFormat.B8G8R8A8SNorm)]
    [InlineData(VkFormat.A2R10G10B10UNormPack32)]
    [InlineData(VkFormat.A2B10G10R10SNormPack32)]
    [InlineData(VkFormat.R64UInt)]
    [InlineData(VkFormat.R64G64B64A64SFloat)]
    [InlineData(VkFormat.D16UNormS8UInt)]
    [InlineData(VkFormat.Bc1RGBUNormBlock)]
    [InlineData(VkFormat.Bc1RGBSRGBBlock)]
    [InlineData(VkFormat.Astc4X4SFloatBlock)]
    [InlineData(VkFormat.Pvrtc14BppUNormBlockIMG)]
    [InlineData(VkFormat.G8B8R82Plane420UNorm)]
    public void TextureFormat_UnmappedFormatsReturnNull(VkFormat format)
    {
        Assert.Null(Formats.ToGpuTextureFormat(format));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("RGBA8UNORM")]
    [InlineData("rgb8unorm")]
    [InlineData("astc-4x4-float")]
    public void FromGpuTextureFormat_UnknownStringIsUndefined(string gpu)
    {
        Assert.Equal(VkFormat.Undefined, Formats.FromGpuTextureFormat(gpu));
    }

    [Fact]
    public void FromGpuTextureFormat_CanvasFormats()
    {
        Assert.Equal(VkFormat.B8G8R8A8UNorm, Formats.FromGpuTextureFormat("bgra8unorm"));
        Assert.Equal(VkFormat.R8G8B8A8UNorm, Formats.FromGpuTextureFormat("rgba8unorm"));
        Assert.Equal(VkFormat.R16G16B16A16SFloat, Formats.FromGpuTextureFormat("rgba16float"));
    }

    // ----- sRGB -----

    [Fact]
    public void SrgbTwin_IsSymmetricAndFlipsIsSrgb()
    {
        int pairs = 0;
        foreach (var format in AllFormats)
        {
            var twin = Formats.SrgbTwin(format);
            if (twin == VkFormat.Undefined)
            {
                continue;
            }

            pairs++;
            Assert.NotEqual(format, twin);
            Assert.Equal(format, Formats.SrgbTwin(twin));
            Assert.NotEqual(Formats.IsSrgb(format), Formats.IsSrgb(twin));
        }

        Assert.True(pairs > 0);
    }

    [Fact]
    public void SrgbTwin_CoversEveryMappedSrgbName()
    {
        foreach (var format in MappedTextureFormats())
        {
            string gpu = Formats.ToGpuTextureFormat(format)!;
            bool srgbName = gpu.EndsWith("-srgb", StringComparison.Ordinal);
            Assert.Equal(srgbName, Formats.IsSrgb(format));

            var twin = Formats.SrgbTwin(format);
            if (srgbName)
            {
                Assert.Equal(gpu[..^"-srgb".Length], Formats.ToGpuTextureFormat(twin));
            }
            else if (twin != VkFormat.Undefined)
            {
                Assert.Equal(gpu + "-srgb", Formats.ToGpuTextureFormat(twin));
            }
        }
    }

    [Theory]
    [InlineData(VkFormat.R8G8B8A8UNorm, VkFormat.R8G8B8A8SRGB)]
    [InlineData(VkFormat.B8G8R8A8UNorm, VkFormat.B8G8R8A8SRGB)]
    [InlineData(VkFormat.Bc1RGBAUNormBlock, VkFormat.Bc1RGBASRGBBlock)]
    [InlineData(VkFormat.Bc2UNormBlock, VkFormat.Bc2SRGBBlock)]
    [InlineData(VkFormat.Bc3UNormBlock, VkFormat.Bc3SRGBBlock)]
    [InlineData(VkFormat.Bc7UNormBlock, VkFormat.Bc7SRGBBlock)]
    [InlineData(VkFormat.Etc2R8G8B8UNormBlock, VkFormat.Etc2R8G8B8SRGBBlock)]
    [InlineData(VkFormat.Etc2R8G8B8A1UNormBlock, VkFormat.Etc2R8G8B8A1SRGBBlock)]
    [InlineData(VkFormat.Etc2R8G8B8A8UNormBlock, VkFormat.Etc2R8G8B8A8SRGBBlock)]
    [InlineData(VkFormat.Astc4X4UNormBlock, VkFormat.Astc4X4SRGBBlock)]
    [InlineData(VkFormat.Astc8X6UNormBlock, VkFormat.Astc8X6SRGBBlock)]
    [InlineData(VkFormat.Astc12X12UNormBlock, VkFormat.Astc12X12SRGBBlock)]
    public void SrgbTwin_KnownPairs(VkFormat unorm, VkFormat srgb)
    {
        Assert.Equal(srgb, Formats.SrgbTwin(unorm));
        Assert.Equal(unorm, Formats.SrgbTwin(srgb));
        Assert.False(Formats.IsSrgb(unorm));
        Assert.True(Formats.IsSrgb(srgb));
    }

    [Theory]
    [InlineData(VkFormat.R32SFloat)]
    [InlineData(VkFormat.R16G16B16A16SFloat)]
    [InlineData(VkFormat.D32SFloat)]
    [InlineData(VkFormat.Bc4UNormBlock)]
    [InlineData(VkFormat.Bc6HUfloatBlock)]
    [InlineData(VkFormat.EacR11UNormBlock)]
    [InlineData(VkFormat.A2B10G10R10UNormPack32)]
    [InlineData(VkFormat.Undefined)]
    public void SrgbTwin_NoneForOtherFormats(VkFormat format)
    {
        Assert.Equal(VkFormat.Undefined, Formats.SrgbTwin(format));
        Assert.False(Formats.IsSrgb(format));
    }

    [Theory]
    [InlineData(VkFormat.R8UNorm, VkFormat.R8SRGB)]
    [InlineData(VkFormat.R8G8B8UNorm, VkFormat.R8G8B8SRGB)]
    [InlineData(VkFormat.A8B8G8R8UNormPack32, VkFormat.A8B8G8R8SRGBPack32)]
    [InlineData(VkFormat.Bc1RGBUNormBlock, VkFormat.Bc1RGBSRGBBlock)]
    public void SrgbTwin_OnlyForPairsWebGpuCanRepresent(VkFormat unorm, VkFormat srgb)
    {
        // The sRGB flavour is still recognised as sRGB, but the pair has no WebGPU texture format, so no twin is offered.
        Assert.True(Formats.IsSrgb(srgb));
        Assert.False(Formats.IsSrgb(unorm));
        Assert.Equal(VkFormat.Undefined, Formats.SrgbTwin(unorm));
        Assert.Equal(VkFormat.Undefined, Formats.SrgbTwin(srgb));
    }

    [Fact]
    public void SrgbTwin_OfMappedFormatIsAlwaysMapped()
    {
        foreach (var f in MappedTextureFormats())
        {
            var twin = Formats.SrgbTwin(f);
            if (twin != VkFormat.Undefined)
            {
                Assert.NotNull(Formats.ToGpuTextureFormat(twin));
            }
        }
    }

    // ----- class predicates -----

    [Fact]
    public void DepthStencilPredicates()
    {
        VkFormat[] depthOnly = { VkFormat.D16UNorm, VkFormat.X8D24UNormPack32, VkFormat.D32SFloat };
        VkFormat[] depthStencil = { VkFormat.D16UNormS8UInt, VkFormat.D24UNormS8UInt, VkFormat.D32SFloatS8UInt };

        foreach (var f in depthOnly)
        {
            Assert.True(Formats.HasDepth(f));
            Assert.False(Formats.HasStencil(f));
            Assert.True(Formats.IsDepthOrStencil(f));
        }

        foreach (var f in depthStencil)
        {
            Assert.True(Formats.HasDepth(f));
            Assert.True(Formats.HasStencil(f));
            Assert.True(Formats.IsDepthOrStencil(f));
        }

        Assert.False(Formats.HasDepth(VkFormat.S8UInt));
        Assert.True(Formats.HasStencil(VkFormat.S8UInt));
        Assert.True(Formats.IsDepthOrStencil(VkFormat.S8UInt));

        foreach (var f in new[] { VkFormat.R8G8B8A8UNorm, VkFormat.R32SFloat, VkFormat.Bc7UNormBlock, VkFormat.Undefined, VkFormat.R8UInt })
        {
            Assert.False(Formats.HasDepth(f));
            Assert.False(Formats.HasStencil(f));
            Assert.False(Formats.IsDepthOrStencil(f));
        }
    }

    [Fact]
    public void IsDepthOrStencil_MatchesGpuNames()
    {
        foreach (var f in MappedTextureFormats())
        {
            string gpu = Formats.ToGpuTextureFormat(f)!;
            bool expected = gpu.StartsWith("depth", StringComparison.Ordinal) || gpu == "stencil8";
            Assert.Equal(expected, Formats.IsDepthOrStencil(f));
        }
    }

    [Fact]
    public void IsCompressed_MatchesGpuFamiliesAndBlockInfo()
    {
        foreach (var f in AllFormats)
        {
            var (bytes, w, h) = Formats.BlockInfo(f);
            if (Formats.IsCompressed(f))
            {
                Assert.True(bytes > 0 && w >= 4 && h >= 4, $"{f} compressed but block info is {bytes}/{w}x{h}");
            }
            else
            {
                Assert.Equal(1, w);
                Assert.Equal(1, h);
            }
        }

        foreach (var f in MappedTextureFormats())
        {
            string gpu = Formats.ToGpuTextureFormat(f)!;
            bool expected = gpu.StartsWith("bc", StringComparison.Ordinal)
                || gpu.StartsWith("etc2", StringComparison.Ordinal)
                || gpu.StartsWith("eac", StringComparison.Ordinal)
                || gpu.StartsWith("astc", StringComparison.Ordinal);
            Assert.Equal(expected, Formats.IsCompressed(f));
        }

        Assert.False(Formats.IsCompressed(VkFormat.R8G8B8A8UNorm));
        Assert.False(Formats.IsCompressed(VkFormat.D32SFloat));
        Assert.True(Formats.IsCompressed(VkFormat.Bc1RGBUNormBlock));
        Assert.True(Formats.IsCompressed(VkFormat.Astc6X5SFloatBlock));
        Assert.True(Formats.IsCompressed(VkFormat.Pvrtc22BppSRGBBlockIMG));
    }

    [Fact]
    public void IsInteger_MatchesGpuNamesAndExcludesDepthStencil()
    {
        foreach (var f in MappedTextureFormats())
        {
            string gpu = Formats.ToGpuTextureFormat(f)!;
            bool expected = !Formats.IsDepthOrStencil(f)
                && (gpu.EndsWith("uint", StringComparison.Ordinal) || gpu.EndsWith("sint", StringComparison.Ordinal));
            Assert.Equal(expected, Formats.IsInteger(f));
        }

        Assert.True(Formats.IsInteger(VkFormat.R8UInt));
        Assert.True(Formats.IsInteger(VkFormat.R32G32B32A32SInt));
        Assert.True(Formats.IsInteger(VkFormat.A2B10G10R10UIntPack32));
        Assert.True(Formats.IsInteger(VkFormat.R8G8B8UInt)); // unmapped but still an integer colour format
        Assert.False(Formats.IsInteger(VkFormat.S8UInt));
        Assert.False(Formats.IsInteger(VkFormat.D24UNormS8UInt));
        Assert.False(Formats.IsInteger(VkFormat.R8UNorm));
        Assert.False(Formats.IsInteger(VkFormat.R8UScaled));
        Assert.False(Formats.IsInteger(VkFormat.R32SFloat));
        Assert.False(Formats.IsInteger(VkFormat.Undefined));
    }

    // ----- block info -----

    [Theory]
    [InlineData(VkFormat.R8G8B8A8UNorm, 4, 1, 1)]
    [InlineData(VkFormat.B8G8R8A8SRGB, 4, 1, 1)]
    [InlineData(VkFormat.R8UNorm, 1, 1, 1)]
    [InlineData(VkFormat.R8G8UNorm, 2, 1, 1)]
    [InlineData(VkFormat.R16SFloat, 2, 1, 1)]
    [InlineData(VkFormat.R16G16B16A16SFloat, 8, 1, 1)]
    [InlineData(VkFormat.R32G32SFloat, 8, 1, 1)]
    [InlineData(VkFormat.R32G32B32A32SFloat, 16, 1, 1)]
    [InlineData(VkFormat.R32G32B32SFloat, 12, 1, 1)]
    [InlineData(VkFormat.A2B10G10R10UNormPack32, 4, 1, 1)]
    [InlineData(VkFormat.B10G11R11UfloatPack32, 4, 1, 1)]
    [InlineData(VkFormat.E5B9G9R9UfloatPack32, 4, 1, 1)]
    [InlineData(VkFormat.R64G64B64A64SFloat, 32, 1, 1)]
    [InlineData(VkFormat.D16UNorm, 2, 1, 1)]
    [InlineData(VkFormat.D32SFloat, 4, 1, 1)]
    [InlineData(VkFormat.S8UInt, 1, 1, 1)]
    [InlineData(VkFormat.X8D24UNormPack32, 4, 1, 1)]
    [InlineData(VkFormat.D24UNormS8UInt, 4, 1, 1)]
    [InlineData(VkFormat.D32SFloatS8UInt, 4, 1, 1)]
    [InlineData(VkFormat.Bc1RGBAUNormBlock, 8, 4, 4)]
    [InlineData(VkFormat.Bc1RGBSRGBBlock, 8, 4, 4)]
    [InlineData(VkFormat.Bc3UNormBlock, 16, 4, 4)]
    [InlineData(VkFormat.Bc4UNormBlock, 8, 4, 4)]
    [InlineData(VkFormat.Bc5SNormBlock, 16, 4, 4)]
    [InlineData(VkFormat.Bc7UNormBlock, 16, 4, 4)]
    [InlineData(VkFormat.Etc2R8G8B8UNormBlock, 8, 4, 4)]
    [InlineData(VkFormat.Etc2R8G8B8A8SRGBBlock, 16, 4, 4)]
    [InlineData(VkFormat.EacR11UNormBlock, 8, 4, 4)]
    [InlineData(VkFormat.EacR11G11SNormBlock, 16, 4, 4)]
    [InlineData(VkFormat.Astc4X4UNormBlock, 16, 4, 4)]
    [InlineData(VkFormat.Astc6X5SRGBBlock, 16, 6, 5)]
    [InlineData(VkFormat.Astc10X8UNormBlock, 16, 10, 8)]
    [InlineData(VkFormat.Astc12X12SRGBBlock, 16, 12, 12)]
    [InlineData(VkFormat.Astc12X10SFloatBlock, 16, 12, 10)]
    [InlineData(VkFormat.Undefined, 0, 1, 1)]
    [InlineData(VkFormat.G8B8R82Plane420UNorm, 0, 1, 1)]
    public void BlockInfo_Values(VkFormat format, int bytes, int width, int height)
    {
        Assert.Equal((bytes, width, height), Formats.BlockInfo(format));
    }

    [Fact]
    public void BlockInfo_IsDefinedForEveryMappedFormat()
    {
        foreach (var f in MappedTextureFormats())
        {
            var (bytes, w, h) = Formats.BlockInfo(f);
            Assert.True(bytes > 0, $"{f} has no block size");
            Assert.True(w >= 1 && h >= 1);
        }
    }

    // ----- vertex formats -----

    [Theory]
    [InlineData(VkFormat.R32G32B32SFloat, "float32x3")]
    [InlineData(VkFormat.R32SFloat, "float32")]
    [InlineData(VkFormat.R32G32SFloat, "float32x2")]
    [InlineData(VkFormat.R32G32B32A32SFloat, "float32x4")]
    [InlineData(VkFormat.R16G16B16A16SFloat, "float16x4")]
    [InlineData(VkFormat.R16G16SFloat, "float16x2")]
    [InlineData(VkFormat.R8G8B8A8UNorm, "unorm8x4")]
    [InlineData(VkFormat.B8G8R8A8UNorm, "unorm8x4-bgra")]
    [InlineData(VkFormat.R8G8B8A8UInt, "uint8x4")]
    [InlineData(VkFormat.R8SInt, "sint8")]
    [InlineData(VkFormat.R8G8SNorm, "snorm8x2")]
    [InlineData(VkFormat.R16G16B16A16UInt, "uint16x4")]
    [InlineData(VkFormat.R16SNorm, "snorm16")]
    [InlineData(VkFormat.R16G16UNorm, "unorm16x2")]
    [InlineData(VkFormat.R32G32B32A32UInt, "uint32x4")]
    [InlineData(VkFormat.R32G32SInt, "sint32x2")]
    [InlineData(VkFormat.A2B10G10R10UNormPack32, "unorm10-10-10-2")]
    [InlineData(VkFormat.A2B10G10R10SNormPack32, "snorm10-10-10-2")]
    public void VertexFormat_Mappings(VkFormat format, string expected)
    {
        Assert.Equal(expected, Formats.ToGpuVertexFormat(format));
    }

    [Theory]
    [InlineData(VkFormat.R8G8B8UNorm)]
    [InlineData(VkFormat.R8G8B8SNorm)]
    [InlineData(VkFormat.R16G16B16SFloat)]
    [InlineData(VkFormat.R16G16B16UNorm)]
    [InlineData(VkFormat.R8UScaled)]
    [InlineData(VkFormat.R8G8B8A8SScaled)]
    [InlineData(VkFormat.R16G16UScaled)]
    [InlineData(VkFormat.R64SFloat)]
    [InlineData(VkFormat.R8G8B8A8SRGB)]
    [InlineData(VkFormat.B8G8R8A8SNorm)]
    [InlineData(VkFormat.B10G11R11UfloatPack32)]
    [InlineData(VkFormat.A2R10G10B10UNormPack32)]
    [InlineData(VkFormat.D32SFloat)]
    [InlineData(VkFormat.Bc1RGBAUNormBlock)]
    [InlineData(VkFormat.Undefined)]
    public void VertexFormat_UnsupportedReturnsNull(VkFormat format)
    {
        Assert.Null(Formats.ToGpuVertexFormat(format));
        Assert.Equal(VkFormatFeatureFlags.None, Formats.BufferFeatures(format));
    }

    [Fact]
    public void VertexFormat_ExactlyTheFortyTwoWebGpuFormats()
    {
        var mapped = MappedVertexFormats();
        var strings = mapped.Select(f => Formats.ToGpuVertexFormat(f)!).ToList();
        Assert.Equal(42, mapped.Count);
        Assert.Equal(42, strings.Distinct().Count());

        foreach (var f in mapped)
        {
            Assert.Equal(VkFormatFeatureFlags.VertexBufferBit, Formats.BufferFeatures(f));
        }

        foreach (var f in AllFormats.Except(mapped))
        {
            Assert.Equal(VkFormatFeatureFlags.None, Formats.BufferFeatures(f));
        }
    }

    // ----- optimalTilingFeatures -----

    [Fact]
    public void Features_Rgba8UNorm_IsTheWorkhorse()
    {
        var flags = Formats.OptimalTilingFeatures(VkFormat.R8G8B8A8UNorm, NoFeatures);
        AssertHas(flags,
            VkFormatFeatureFlags.SampledImageBit
            | VkFormatFeatureFlags.SampledImageFilterLinearBit
            | VkFormatFeatureFlags.ColorAttachmentBit
            | VkFormatFeatureFlags.ColorAttachmentBlendBit
            | VkFormatFeatureFlags.StorageImageBit
            | Transfer
            | VkFormatFeatureFlags.BlitSrcBit
            | VkFormatFeatureFlags.BlitDstBit);
        AssertLacks(flags, VkFormatFeatureFlags.DepthStencilAttachmentBit | VkFormatFeatureFlags.VertexBufferBit);
    }

    [Fact]
    public void Features_SrgbFormats_AreRenderableButNotStorage()
    {
        foreach (var f in new[] { VkFormat.R8G8B8A8SRGB, VkFormat.B8G8R8A8SRGB })
        {
            var flags = Formats.OptimalTilingFeatures(f, NoFeatures);
            AssertHas(flags, VkFormatFeatureFlags.SampledImageBit | VkFormatFeatureFlags.SampledImageFilterLinearBit
                | VkFormatFeatureFlags.ColorAttachmentBit | VkFormatFeatureFlags.ColorAttachmentBlendBit | Transfer);
            AssertLacks(flags, VkFormatFeatureFlags.StorageImageBit);
        }
    }

    [Fact]
    public void Features_Bgra8UNorm_StorageNeedsFeature()
    {
        AssertLacks(Formats.OptimalTilingFeatures(VkFormat.B8G8R8A8UNorm, NoFeatures), VkFormatFeatureFlags.StorageImageBit);
        AssertHas(Formats.OptimalTilingFeatures(VkFormat.B8G8R8A8UNorm, Features("bgra8unorm-storage")), VkFormatFeatureFlags.StorageImageBit);
        AssertHas(Formats.OptimalTilingFeatures(VkFormat.B8G8R8A8UNorm, NoFeatures), VkFormatFeatureFlags.ColorAttachmentBit);
    }

    [Fact]
    public void Features_R32Float_FilterAndBlendNeedFeatures()
    {
        foreach (var f in new[] { VkFormat.R32SFloat, VkFormat.R32G32SFloat, VkFormat.R32G32B32A32SFloat })
        {
            var without = Formats.OptimalTilingFeatures(f, NoFeatures);
            AssertHas(without, VkFormatFeatureFlags.SampledImageBit | VkFormatFeatureFlags.ColorAttachmentBit
                | VkFormatFeatureFlags.StorageImageBit | Transfer);
            AssertLacks(without, VkFormatFeatureFlags.SampledImageFilterLinearBit | VkFormatFeatureFlags.ColorAttachmentBlendBit);

            var filterable = Formats.OptimalTilingFeatures(f, Features("float32-filterable"));
            AssertHas(filterable, VkFormatFeatureFlags.SampledImageFilterLinearBit);
            AssertLacks(filterable, VkFormatFeatureFlags.ColorAttachmentBlendBit);

            var blendable = Formats.OptimalTilingFeatures(f, Features("float32-blendable"));
            AssertHas(blendable, VkFormatFeatureFlags.ColorAttachmentBlendBit);
            AssertLacks(blendable, VkFormatFeatureFlags.SampledImageFilterLinearBit);
        }
    }

    [Fact]
    public void Features_SixteenBitNorm_ExistOnlyWithTier1AndAreNeverFilterable()
    {
        foreach (var f in new[] { VkFormat.R16UNorm, VkFormat.R16SNorm, VkFormat.R16G16UNorm, VkFormat.R16G16SNorm, VkFormat.R16G16B16A16UNorm, VkFormat.R16G16B16A16SNorm })
        {
            Assert.Equal(VkFormatFeatureFlags.None, Formats.OptimalTilingFeatures(f, NoFeatures));
            var flags = Formats.OptimalTilingFeatures(f, Features("texture-formats-tier1"));
            AssertHas(flags, VkFormatFeatureFlags.SampledImageBit | VkFormatFeatureFlags.ColorAttachmentBit
                | VkFormatFeatureFlags.ColorAttachmentBlendBit | VkFormatFeatureFlags.StorageImageBit | Transfer);
            AssertLacks(flags, VkFormatFeatureFlags.SampledImageFilterLinearBit);
        }
    }

    [Fact]
    public void Features_SnormAndRg11_RenderabilityNeedsFeature()
    {
        AssertLacks(Formats.OptimalTilingFeatures(VkFormat.R8G8B8A8SNorm, NoFeatures), VkFormatFeatureFlags.ColorAttachmentBit);
        AssertHas(Formats.OptimalTilingFeatures(VkFormat.R8G8B8A8SNorm, NoFeatures),
            VkFormatFeatureFlags.SampledImageBit | VkFormatFeatureFlags.SampledImageFilterLinearBit | VkFormatFeatureFlags.StorageImageBit | Transfer);
        AssertHas(Formats.OptimalTilingFeatures(VkFormat.R8G8B8A8SNorm, Features("texture-formats-tier1")),
            VkFormatFeatureFlags.ColorAttachmentBit | VkFormatFeatureFlags.ColorAttachmentBlendBit);

        AssertLacks(Formats.OptimalTilingFeatures(VkFormat.B10G11R11UfloatPack32, NoFeatures), VkFormatFeatureFlags.ColorAttachmentBit);
        AssertHas(Formats.OptimalTilingFeatures(VkFormat.B10G11R11UfloatPack32, NoFeatures),
            VkFormatFeatureFlags.SampledImageBit | VkFormatFeatureFlags.SampledImageFilterLinearBit | Transfer);
        AssertHas(Formats.OptimalTilingFeatures(VkFormat.B10G11R11UfloatPack32, Features("rg11b10ufloat-renderable")),
            VkFormatFeatureFlags.ColorAttachmentBit | VkFormatFeatureFlags.ColorAttachmentBlendBit);
        AssertHas(Formats.OptimalTilingFeatures(VkFormat.B10G11R11UfloatPack32, Features("texture-formats-tier1")),
            VkFormatFeatureFlags.ColorAttachmentBit | VkFormatFeatureFlags.StorageImageBit);

        // rgb9e5 is never renderable
        var rgb9e5 = Formats.OptimalTilingFeatures(VkFormat.E5B9G9R9UfloatPack32, EveryFeature);
        AssertHas(rgb9e5, VkFormatFeatureFlags.SampledImageBit | VkFormatFeatureFlags.SampledImageFilterLinearBit | Transfer);
        AssertLacks(rgb9e5, VkFormatFeatureFlags.ColorAttachmentBit | VkFormatFeatureFlags.StorageImageBit);
    }

    [Fact]
    public void Features_IntegerFormats_NeverFilterableOrBlendable()
    {
        foreach (var f in AllFormats.Where(Formats.IsInteger))
        {
            var flags = Formats.OptimalTilingFeatures(f, EveryFeature);
            AssertLacks(flags, VkFormatFeatureFlags.SampledImageFilterLinearBit | VkFormatFeatureFlags.ColorAttachmentBlendBit);
            if (Formats.ToGpuTextureFormat(f) is not null)
            {
                AssertHas(flags, VkFormatFeatureFlags.SampledImageBit | VkFormatFeatureFlags.ColorAttachmentBit | Transfer);
            }
        }
    }

    [Fact]
    public void Features_Depth24Stencil8_IsAttachmentWithoutTransferSrc()
    {
        var flags = Formats.OptimalTilingFeatures(VkFormat.D24UNormS8UInt, NoFeatures);
        AssertHas(flags, VkFormatFeatureFlags.DepthStencilAttachmentBit | VkFormatFeatureFlags.SampledImageBit);
        AssertLacks(flags, VkFormatFeatureFlags.TransferSrcBit | VkFormatFeatureFlags.TransferDstBit
            | VkFormatFeatureFlags.SampledImageFilterLinearBit | VkFormatFeatureFlags.ColorAttachmentBit);

        var x8d24 = Formats.OptimalTilingFeatures(VkFormat.X8D24UNormPack32, NoFeatures);
        AssertHas(x8d24, VkFormatFeatureFlags.DepthStencilAttachmentBit | VkFormatFeatureFlags.SampledImageBit);
        AssertLacks(x8d24, VkFormatFeatureFlags.TransferSrcBit | VkFormatFeatureFlags.TransferDstBit);
    }

    [Fact]
    public void Features_DepthCopyRules()
    {
        var d16 = Formats.OptimalTilingFeatures(VkFormat.D16UNorm, NoFeatures);
        AssertHas(d16, VkFormatFeatureFlags.DepthStencilAttachmentBit | VkFormatFeatureFlags.SampledImageBit | Transfer);

        var d32 = Formats.OptimalTilingFeatures(VkFormat.D32SFloat, NoFeatures);
        AssertHas(d32, VkFormatFeatureFlags.DepthStencilAttachmentBit | VkFormatFeatureFlags.SampledImageBit | VkFormatFeatureFlags.TransferSrcBit);
        AssertLacks(d32, VkFormatFeatureFlags.TransferDstBit);

        var s8 = Formats.OptimalTilingFeatures(VkFormat.S8UInt, NoFeatures);
        AssertHas(s8, VkFormatFeatureFlags.DepthStencilAttachmentBit | VkFormatFeatureFlags.SampledImageBit | Transfer);
    }

    [Fact]
    public void Features_Depth32Stencil8_RequiresFeature()
    {
        Assert.Equal(VkFormatFeatureFlags.None, Formats.OptimalTilingFeatures(VkFormat.D32SFloatS8UInt, NoFeatures));
        var flags = Formats.OptimalTilingFeatures(VkFormat.D32SFloatS8UInt, Features("depth32float-stencil8"));
        AssertHas(flags, VkFormatFeatureFlags.DepthStencilAttachmentBit | VkFormatFeatureFlags.SampledImageBit | VkFormatFeatureFlags.TransferSrcBit);
        AssertLacks(flags, VkFormatFeatureFlags.TransferDstBit);
    }

    [Fact]
    public void Features_DepthFormatsAreNeverLinearFilterable()
    {
        foreach (var f in AllFormats.Where(Formats.IsDepthOrStencil))
        {
            AssertLacks(Formats.OptimalTilingFeatures(f, EveryFeature), VkFormatFeatureFlags.SampledImageFilterLinearBit);
        }

        Assert.Equal(VkFormatFeatureFlags.None, Formats.OptimalTilingFeatures(VkFormat.D16UNormS8UInt, EveryFeature));
    }

    [Theory]
    [InlineData(VkFormat.Bc7UNormBlock, "texture-compression-bc")]
    [InlineData(VkFormat.Bc1RGBASRGBBlock, "texture-compression-bc")]
    [InlineData(VkFormat.Bc6HSFloatBlock, "texture-compression-bc")]
    [InlineData(VkFormat.Etc2R8G8B8UNormBlock, "texture-compression-etc2")]
    [InlineData(VkFormat.EacR11G11UNormBlock, "texture-compression-etc2")]
    [InlineData(VkFormat.Astc4X4UNormBlock, "texture-compression-astc")]
    [InlineData(VkFormat.Astc12X12SRGBBlock, "texture-compression-astc")]
    public void Features_CompressedFamilies_NeedTheirFeature(VkFormat format, string feature)
    {
        Assert.Equal(VkFormatFeatureFlags.None, Formats.OptimalTilingFeatures(format, NoFeatures));
        Assert.Equal(VkFormatFeatureFlags.None, Formats.OptimalTilingFeatures(format, Features("texture-formats-tier1", "float32-filterable")));

        var flags = Formats.OptimalTilingFeatures(format, Features(feature));
        AssertHas(flags, VkFormatFeatureFlags.SampledImageBit | VkFormatFeatureFlags.SampledImageFilterLinearBit
            | Transfer | VkFormatFeatureFlags.BlitSrcBit);
        AssertLacks(flags, VkFormatFeatureFlags.ColorAttachmentBit | VkFormatFeatureFlags.StorageImageBit
            | VkFormatFeatureFlags.DepthStencilAttachmentBit | VkFormatFeatureFlags.BlitDstBit);
    }

    [Fact]
    public void Features_Bc7_NoneWithoutFeatureAndSampledWith()
    {
        Assert.Equal(VkFormatFeatureFlags.None, Formats.OptimalTilingFeatures(VkFormat.Bc7UNormBlock, NoFeatures));
        AssertHas(Formats.OptimalTilingFeatures(VkFormat.Bc7UNormBlock, Features("texture-compression-bc")), VkFormatFeatureFlags.SampledImageBit);
        // another family's feature does not unlock BC
        Assert.Equal(VkFormatFeatureFlags.None,
            Formats.OptimalTilingFeatures(VkFormat.Bc7UNormBlock, Features("texture-compression-astc", "texture-compression-etc2")));
    }

    [Theory]
    [InlineData(VkFormat.R8G8B8UNorm)]
    [InlineData(VkFormat.R32G32B32SFloat)]
    [InlineData(VkFormat.R16G16B16A16UScaled)]
    [InlineData(VkFormat.R5G6B5UNormPack16)]
    [InlineData(VkFormat.R64SFloat)]
    [InlineData(VkFormat.D16UNormS8UInt)]
    [InlineData(VkFormat.Bc1RGBUNormBlock)]
    [InlineData(VkFormat.Astc4X4SFloatBlock)]
    [InlineData(VkFormat.G8B8R82Plane420UNorm)]
    [InlineData(VkFormat.Undefined)]
    public void Features_UnmappedFormatsAreNone(VkFormat format)
    {
        Assert.Equal(VkFormatFeatureFlags.None, Formats.OptimalTilingFeatures(format, EveryFeature));
        Assert.Equal(VkFormatFeatureFlags.None, Formats.OptimalTilingFeatures(format, NoFeatures));
    }

    [Fact]
    public void Features_EveryMappedFormatIsSampledWhenDeviceHasEverything_AndNoUnmappedFormatReportsAnything()
    {
        foreach (var f in AllFormats)
        {
            var flags = Formats.OptimalTilingFeatures(f, EveryFeature);
            if (Formats.ToGpuTextureFormat(f) is null)
            {
                Assert.Equal(VkFormatFeatureFlags.None, flags);
            }
            else
            {
                AssertHas(flags, VkFormatFeatureFlags.SampledImageBit);
                AssertLacks(flags, VkFormatFeatureFlags.VertexBufferBit); // bufferFeatures bit never leaks into optimal tiling
            }
        }
    }

    [Fact]
    public void Features_DependentBitsAreConsistent()
    {
        // BlitSrc implies SampledImage, BlitDst implies ColorAttachment, for every format and feature set.
        foreach (var features in new[] { NoFeatures, EveryFeature })
        {
            foreach (var f in AllFormats)
            {
                var flags = Formats.OptimalTilingFeatures(f, features);
                if ((flags & VkFormatFeatureFlags.BlitSrcBit) != 0)
                {
                    AssertHas(flags, VkFormatFeatureFlags.SampledImageBit);
                }

                if ((flags & VkFormatFeatureFlags.BlitDstBit) != 0)
                {
                    AssertHas(flags, VkFormatFeatureFlags.ColorAttachmentBit);
                }

                // Blend implies a colour attachment; linear filtering implies sampling.
                if ((flags & VkFormatFeatureFlags.ColorAttachmentBlendBit) != 0)
                {
                    AssertHas(flags, VkFormatFeatureFlags.ColorAttachmentBit);
                }

                if ((flags & VkFormatFeatureFlags.SampledImageFilterLinearBit) != 0)
                {
                    AssertHas(flags, VkFormatFeatureFlags.SampledImageBit);
                }
            }
        }
    }

    // ----- sample type -----

    [Theory]
    [InlineData(VkFormat.R8G8B8A8UNorm, "float")]
    [InlineData(VkFormat.R8G8B8A8SRGB, "float")]
    [InlineData(VkFormat.R16SFloat, "float")]
    [InlineData(VkFormat.Bc7UNormBlock, "float")]
    [InlineData(VkFormat.Astc8X8SRGBBlock, "float")]
    [InlineData(VkFormat.B10G11R11UfloatPack32, "float")]
    [InlineData(VkFormat.R32SFloat, "unfilterable-float")]
    [InlineData(VkFormat.R32G32SFloat, "unfilterable-float")]
    [InlineData(VkFormat.R32G32B32A32SFloat, "unfilterable-float")]
    [InlineData(VkFormat.R16UNorm, "unfilterable-float")]
    [InlineData(VkFormat.R16G16SNorm, "unfilterable-float")]
    [InlineData(VkFormat.R16G16B16A16UNorm, "unfilterable-float")]
    [InlineData(VkFormat.D16UNorm, "depth")]
    [InlineData(VkFormat.X8D24UNormPack32, "depth")]
    [InlineData(VkFormat.D32SFloat, "depth")]
    [InlineData(VkFormat.D24UNormS8UInt, "depth")]
    [InlineData(VkFormat.D32SFloatS8UInt, "depth")]
    [InlineData(VkFormat.S8UInt, "uint")]
    [InlineData(VkFormat.R8UInt, "uint")]
    [InlineData(VkFormat.R32G32B32A32UInt, "uint")]
    [InlineData(VkFormat.A2B10G10R10UIntPack32, "uint")]
    [InlineData(VkFormat.R16SInt, "sint")]
    [InlineData(VkFormat.R32SInt, "sint")]
    [InlineData(VkFormat.R8G8B8A8SInt, "sint")]
    public void SampleType_Values(VkFormat format, string expected)
    {
        Assert.Equal(expected, Formats.SampleType(format));
    }

    [Fact]
    public void SampleType_AgreesWithPredicatesForMappedFormats()
    {
        foreach (var f in MappedTextureFormats())
        {
            string type = Formats.SampleType(f);
            if (Formats.HasDepth(f))
            {
                Assert.Equal("depth", type);
            }
            else if (Formats.IsInteger(f) || f == VkFormat.S8UInt)
            {
                Assert.True(type is "uint" or "sint", $"{f}: {type}");
            }
            else
            {
                Assert.True(type is "float" or "unfilterable-float", $"{f}: {type}");
            }
        }
    }

    // ----- surface formats -----

    [Fact]
    public void SurfaceFormats_Bgra()
    {
        Assert.Equal((VkFormat.B8G8R8A8UNorm, VkFormat.B8G8R8A8SRGB), Formats.SurfaceFormats("bgra8unorm"));
    }

    [Fact]
    public void SurfaceFormats_Rgba()
    {
        Assert.Equal((VkFormat.R8G8B8A8UNorm, VkFormat.R8G8B8A8SRGB), Formats.SurfaceFormats("rgba8unorm"));
    }

    [Theory]
    [InlineData("bgra8unorm")]
    [InlineData("rgba8unorm")]
    public void SurfaceFormats_AreConsistentWithTheOtherTables(string canvas)
    {
        var (unorm, srgb) = Formats.SurfaceFormats(canvas);
        Assert.Equal(unorm, Formats.FromGpuTextureFormat(canvas));
        Assert.Equal(srgb, Formats.SrgbTwin(unorm));
        Assert.True(Formats.IsSrgb(srgb));
        Assert.False(Formats.IsSrgb(unorm));
        AssertHas(Formats.OptimalTilingFeatures(unorm, NoFeatures), VkFormatFeatureFlags.ColorAttachmentBit | VkFormatFeatureFlags.SampledImageBit);
        AssertHas(Formats.OptimalTilingFeatures(srgb, NoFeatures), VkFormatFeatureFlags.ColorAttachmentBit | VkFormatFeatureFlags.SampledImageBit);
    }
}
