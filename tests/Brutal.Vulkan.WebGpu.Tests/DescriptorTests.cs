using System.Text.Json;
using Brutal;
using Brutal.Vulkan.WebGpu;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace WebGpuIcd.Tests;

/// <summary>Descriptor set layouts → bind group layouts, sets → bind groups, WGSL reflection, pipeline layouts.</summary>
public sealed unsafe class DescriptorTests
{
    private readonly RecordingBridge _bridge = TestIcd.Reset();

    private static JsonElement Json(RecordingBridge.Call c) => JsonDocument.Parse(c.Json!).RootElement;

    private static (ImageEx Image, ImageViewEx View) Image(TestDevice dev, VkFormat format, VkImageUsageFlags usage)
    {
        var image = dev.Device.CreateImage(new ImageEx.CreateInfo
        {
            Name = "img", ImageType = VkImageType._2D, ImageFormat = format, ImageExtent = new VkExtent3D(4, 4, 1), ImageMipLevels = 1,
            ImageArrayLayers = 1, ImageSamples = VkSampleCountFlags._1Bit, ImageTiling = VkImageTiling.Optimal, ImageUsage = usage,
            AllocPreference = MemoryPreference.PreferGpu, AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
        });
        var aspect = Formats.HasDepth(format) ? VkImageAspectFlags.DepthBit : VkImageAspectFlags.ColorBit;
        return (image, image.CreateImageView(VkImageViewType._2D, Subresources.Full(aspect), null));
    }

    [Fact]
    public void CombinedImageSamplersSplitIntoTextureAndSamplerAtBindingPlus500()
    {
        using var dev = new TestDevice();
        Span<VkDescriptorSetLayoutBinding> bindings =
        [
            new() { Binding = 0, DescriptorType = VkDescriptorType.UniformBuffer, DescriptorCount = 1, StageFlags = VkShaderStageFlags.VertexBit },
            new() { Binding = 1, DescriptorType = VkDescriptorType.CombinedImageSampler, DescriptorCount = 1, StageFlags = VkShaderStageFlags.FragmentBit },
        ];
        using var layout = dev.Device.CreateDescriptorSetLayout(new DescriptorSetLayoutEx.CreateInfo { Bindings = bindings }, null);
        Span<VkDescriptorPoolSize> sizes = [new() { Type = VkDescriptorType.UniformBuffer, DescriptorCount = 1 }, new() { Type = VkDescriptorType.CombinedImageSampler, DescriptorCount = 1 }];
        using var pool = dev.Device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo { MaxSets = 1, PoolSizes = sizes }, null);
        var set = dev.Device.AllocateDescriptorSet(pool, layout);
        using var ubo = dev.HostBuffer(256, VkBufferUsageFlags.UniformBufferBit);
        var (image, view) = Image(dev, VkFormat.R8G8B8A8SRGB, VkImageUsageFlags.SampledBit);
        var sampler = dev.Device.CreateSampler(new VkSamplerCreateInfo { MagFilter = VkFilter.Linear, MinFilter = VkFilter.Linear, MaxLod = 1f }, null);
        var bufferInfo = new VkDescriptorBufferInfo { Buffer = ubo.VkBuffer, Range = VK.WHOLE_SIZE };
        var imageInfo = new VkDescriptorImageInfo { Sampler = sampler, ImageView = view.VkImageView, ImageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        dev.Device.UpdateDescriptorSets(
        [
            new VkWriteDescriptorSet { DstSet = set, DstBinding = 0, DescriptorCount = 1, DescriptorType = VkDescriptorType.UniformBuffer, BufferInfo = &bufferInfo },
            new VkWriteDescriptorSet { DstSet = set, DstBinding = 1, DescriptorCount = 1, DescriptorType = VkDescriptorType.CombinedImageSampler, ImageInfo = &imageInfo },
        ], []);
        var pipelineLayout = dev.Device.CreatePipelineLayout([layout.Handle], ReadOnlySpan<VkPushConstantRange>.Empty, null);
        var cmd = dev.CommandBuffer(out var commandPool);
        cmd.Begin();
        cmd.BindDescriptorSets(VkPipelineBindPoint.Graphics, pipelineLayout, 0, [set], []);
        cmd.End();

        var bgl = Json(_bridge.Named("createBindGroupLayout").Single()).GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal([0, 1, 501], bgl.Select(e => e.GetProperty("binding").GetInt32()));
        Assert.Equal("texture", bgl[1].EnumerateObject().Last().Name);
        Assert.Equal("sampler", bgl[2].EnumerateObject().Last().Name);

        var group = Json(_bridge.Named("createBindGroup").Single());
        var entries = group.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal([0, 1, 501], entries.Select(e => e.GetProperty("binding").GetInt32()));
        Assert.Equal(256, entries[0].GetProperty("size").GetInt32()); // VK_WHOLE_SIZE → rest of the buffer
        Assert.Equal(HandleTable.Get<ImageViewObject>(view.VkImageView.VkHandle)!.Gpu, entries[1].GetProperty("view").GetInt32());
        Assert.Equal(HandleTable.Get<SamplerObject>(sampler.VkHandle)!.Gpu, entries[2].GetProperty("sampler").GetInt32());

        // the bind group is rebuilt only after another write
        cmd.Begin();
        cmd.BindDescriptorSets(VkPipelineBindPoint.Graphics, pipelineLayout, 0, [set], []);
        cmd.End();
        Assert.Single(_bridge.Named("createBindGroup"));
        dev.Device.UpdateDescriptorSets([new VkWriteDescriptorSet { DstSet = set, DstBinding = 1, DescriptorCount = 1, DescriptorType = VkDescriptorType.CombinedImageSampler, ImageInfo = &imageInfo }], []);
        cmd.Begin();
        cmd.BindDescriptorSets(VkPipelineBindPoint.Graphics, pipelineLayout, 0, [set], []);
        cmd.End();
        Assert.Equal(2, _bridge.Named("createBindGroup").Count());
        Assert.Contains(_bridge.Named("createBindGroup").First().Handle, _bridge.Released);

        dev.Device.DestroyCommandPool(commandPool, null);
        dev.Device.DestroySampler(sampler, null);
        view.Dispose();
        image.Dispose();
        Assert.Empty(TestIcd.Errors());
    }

    [Fact]
    public void DescriptorWritesBeforeFirstUseRefineTheBindGroupLayout()
    {
        using var dev = new TestDevice();
        Span<VkDescriptorSetLayoutBinding> bindings =
        [
            new() { Binding = 0, DescriptorType = VkDescriptorType.SampledImage, DescriptorCount = 1, StageFlags = VkShaderStageFlags.FragmentBit },
            new() { Binding = 1, DescriptorType = VkDescriptorType.Sampler, DescriptorCount = 1, StageFlags = VkShaderStageFlags.FragmentBit },
        ];
        using var layout = dev.Device.CreateDescriptorSetLayout(new DescriptorSetLayoutEx.CreateInfo { Bindings = bindings }, null);
        Span<VkDescriptorPoolSize> sizes = [new() { Type = VkDescriptorType.SampledImage, DescriptorCount = 1 }, new() { Type = VkDescriptorType.Sampler, DescriptorCount = 1 }];
        using var pool = dev.Device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo { MaxSets = 1, PoolSizes = sizes }, null);
        var set = dev.Device.AllocateDescriptorSet(pool, layout);
        var (image, view) = Image(dev, VkFormat.D32SFloat, VkImageUsageFlags.SampledBit | VkImageUsageFlags.DepthStencilAttachmentBit);
        var shadowSampler = dev.Device.CreateSampler(new VkSamplerCreateInfo { CompareEnable = true, CompareOp = VkCompareOp.Less, MaxLod = 1f }, null);
        var imageInfo = new VkDescriptorImageInfo { ImageView = view.VkImageView };
        var samplerInfo = new VkDescriptorImageInfo { Sampler = shadowSampler };
        dev.Device.UpdateDescriptorSets(
        [
            new VkWriteDescriptorSet { DstSet = set, DstBinding = 0, DescriptorCount = 1, DescriptorType = VkDescriptorType.SampledImage, ImageInfo = &imageInfo },
            new VkWriteDescriptorSet { DstSet = set, DstBinding = 1, DescriptorCount = 1, DescriptorType = VkDescriptorType.Sampler, ImageInfo = &samplerInfo },
        ], []);
        var pipelineLayout = dev.Device.CreatePipelineLayout([layout.Handle], ReadOnlySpan<VkPushConstantRange>.Empty, null);
        var cmd = dev.CommandBuffer(out var commandPool);
        cmd.Begin();
        cmd.BindDescriptorSets(VkPipelineBindPoint.Graphics, pipelineLayout, 0, [set], []);
        cmd.End();

        var entries = Json(_bridge.Named("createBindGroupLayout").Single()).GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal("depth", entries[0].GetProperty("texture").GetProperty("sampleType").GetString());
        Assert.Equal("comparison", entries[1].GetProperty("sampler").GetProperty("type").GetString());
        Assert.Equal("less", Json(_bridge.Named("createSampler").Single()).GetProperty("compare").GetString());
        dev.Device.DestroyCommandPool(commandPool, null);
        dev.Device.DestroySampler(shadowSampler, null);
        view.Dispose();
        image.Dispose();
    }

    [Fact]
    public void WgslReflectionReadsBindingTypes()
    {
        const string wgsl = """
            @group(0) @binding(0)
            var<uniform> u: T;
            @group(0) @binding(1) var<storage, read> ro: array<f32>;
            @group(0) @binding(2) var<storage, read_write> rw: array<f32>;
            @group(1) @binding(0) var shadow: texture_depth_2d;
            @group(1) @binding(500) var cmp: sampler_comparison;
            @group(1) @binding(1) var cube: texture_cube<f32>;
            @group(1) @binding(2) var ids: texture_2d_array<u32>;
            @group(2) @binding(0) var img: texture_storage_2d<rgba16float, write>;
            @group(2) @binding(1) var ms: texture_multisampled_2d<f32>;
            @id(7) override scale: f32 = 1.0;
            """;
        var b = ShaderManifest.Reflect(wgsl).ToDictionary(x => (x.Group, x.Binding));
        Assert.Equal("uniform", b[(0, 0)].Kind);
        Assert.Equal("read-only-storage", b[(0, 1)].Kind);
        Assert.Equal("storage", b[(0, 2)].Kind);
        Assert.Equal(("texture", "depth"), (b[(1, 0)].Kind, b[(1, 0)].SampleType));
        Assert.Equal("comparison", b[(1, 500)].Kind);
        Assert.Equal("cube", b[(1, 1)].ViewDimension);
        Assert.Equal(("2d-array", "uint"), (b[(1, 2)].ViewDimension, b[(1, 2)].SampleType));
        Assert.Equal(("storageTexture", "rgba16float", "write-only"), (b[(2, 0)].Kind, b[(2, 0)].StorageFormat, b[(2, 0)].StorageAccess));
        Assert.True(b[(2, 1)].Multisampled);
    }

    [Fact]
    public void ManifestEntriesCarryPushModeAndReflectedBindings()
    {
        var frag = TestIcd.Resource("Shaders/tri.frag.spv");
        Assert.True(ShaderManifest.TryGet(ShaderHash.Hex(frag), out var entry));
        Assert.Equal(("tri.frag", "fragment", "main"), (entry.Name, entry.Stage, entry.EntryPoint));
        Assert.Equal((PushMode.Uniform, 16), (entry.Push, entry.PushSize));
        Assert.Contains(entry.Bindings, x => x is { Group: 3, Binding: 0, Kind: "uniform" });
        Assert.Contains(entry.Bindings, x => x is { Group: 0, Binding: 1, Kind: "texture", SampleType: "float", ViewDimension: "2d" });
        Assert.Equal(500, ShaderManifest.CombinedSamplerOffset);
    }

    [Fact]
    public void DescriptorArraysAreRejected()
    {
        using var dev = new TestDevice();
        var binding = new VkDescriptorSetLayoutBinding { Binding = 0, DescriptorType = VkDescriptorType.SampledImage, DescriptorCount = 4, StageFlags = VkShaderStageFlags.FragmentBit };
        var info = new VkDescriptorSetLayoutCreateInfo { BindingCount = 1, Bindings = &binding };
        VkDescriptorSetLayout layout;
        Assert.Equal(VkResult.ErrorFeatureNotPresent, Icd.vkCreateDescriptorSetLayout(dev.Device.Handle, &info, null, &layout));
        Assert.Contains(TestIcd.Errors(), l => l.Contains("descriptor arrays"));
    }
}
