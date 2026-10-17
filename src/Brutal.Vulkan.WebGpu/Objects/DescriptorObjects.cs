using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

internal sealed class DescriptorBindingInfo
{
    public int Binding;
    public VkDescriptorType Type;
    public int Count;
    public VkShaderStageFlags Stages;
    public SamplerObject?[]? ImmutableSamplers;
    public bool IsDynamic => Type is VkDescriptorType.UniformBufferDynamic or VkDescriptorType.StorageBufferDynamic;
}

/// <summary>
/// A VkDescriptorSetLayout. Its GPUBindGroupLayout is materialized on first need (pipeline creation or bind-group build),
/// because WebGPU wants details Vulkan layouts lack; they come from, in order: the WGSL of the first pipeline that uses the
/// layout, the first descriptor write, defaults (float 2d textures, filtering samplers).
/// </summary>
internal sealed class DescriptorSetLayoutObject : IcdObject
{
    public override string VkTypeName => "VkDescriptorSetLayout";
    public DescriptorBindingInfo[] Bindings = [];
    public int DynamicCount;
    public int Bgl;
    /// <summary>Per WebGPU binding number: reflected/hinted binding details (consumed at materialization).</summary>
    public readonly Dictionary<int, WgslBinding> Refinements = new();
    /// <summary>Per WebGPU binding number: what the bind group layout was actually built with (after materialization).</summary>
    public readonly Dictionary<int, WgslBinding> Effective = new();
    public bool RefinedFromShader;

    public override int GpuHandle => Bgl;

    public int IndexOf(int binding)
    {
        for (var i = 0; i < Bindings.Length; i++)
            if (Bindings[i].Binding == binding)
                return i;
        return -1;
    }
}

internal sealed class DescriptorPoolObject : IcdObject
{
    public override string VkTypeName => "VkDescriptorPool";
    public readonly List<DescriptorSetObject> Sets = new();
    public int MaxSets;
}

internal struct DescriptorSlot
{
    public BufferObject? Buffer;
    public ulong Offset, Range;
    public ImageViewObject? View;
    public SamplerObject? Sampler;
}

internal sealed class DescriptorSetObject : IcdObject
{
    public override string VkTypeName => "VkDescriptorSet";
    public DescriptorSetLayoutObject Layout = null!;
    public DescriptorPoolObject Pool = null!;
    /// <summary>Parallel to <see cref="DescriptorSetLayoutObject.Bindings"/>.</summary>
    public DescriptorSlot[] Slots = [];
    public bool Dirty = true;
    public int BindGroup;

    public override int GpuHandle => BindGroup;
}
