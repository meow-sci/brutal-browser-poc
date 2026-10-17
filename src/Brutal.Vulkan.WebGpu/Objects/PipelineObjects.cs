using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

internal sealed class ShaderModuleObject : IcdObject
{
    public override string VkTypeName => "VkShaderModule";
    public ShaderEntry Entry = null!;
    public int Gpu;
    public override int GpuHandle => Gpu;
}

internal sealed class PipelineCacheObject : IcdObject
{
    public override string VkTypeName => "VkPipelineCache";
}

/// <summary>
/// A VkPipelineLayout. The GPUPipelineLayout of a pipeline is built per (groups used by its shaders, push mode): groups no
/// shader uses get the empty layout, push constants take group 3 (docs/research/shaders/wgsl-conventions-for-vulkan-layer.md C8).
/// </summary>
internal sealed class PipelineLayoutObject : IcdObject
{
    public override string VkTypeName => "VkPipelineLayout";
    public DescriptorSetLayoutObject?[] SetLayouts = [];
    public int PushSize;
    public VkShaderStageFlags PushStages;
    public readonly Dictionary<int, int> GpuLayouts = new();
}

internal sealed class PipelineObject : IcdObject
{
    public override string VkTypeName => "VkPipeline";
    public int Gpu;
    public uint BindPoint;
    public uint PushMode;
    public uint FillerMask;
    public uint GroupCount;
    public PipelineLayoutObject Layout = null!;
    /// <summary>Strip topologies: WebGPU needs the index format in the pipeline; variants are built on the first indexed draw.</summary>
    public bool IsStrip;
    public string? DescriptorJson;
    public int StripUint16, StripUint32;
    /// <summary>Viewport/scissor baked into the pipeline (not dynamic): re-emitted at every vkCmdBindPipeline.</summary>
    public bool HasStaticViewport, HasStaticScissor;
    public VkViewport StaticViewport;
    public VkRect2D StaticScissor;
    public override int GpuHandle => Gpu;
}

internal struct AttachmentDescription
{
    public VkFormat Format;
    public int Samples;
    public VkAttachmentLoadOp LoadOp, StencilLoadOp;
    public VkAttachmentStoreOp StoreOp, StencilStoreOp;
}

/// <summary>A single-subpass VkRenderPass (the record vkCmdBeginRenderPass turns into a WebGPU render pass).</summary>
internal sealed class RenderPassObject : IcdObject
{
    public override string VkTypeName => "VkRenderPass";
    public AttachmentDescription[] Attachments = [];
    public int[] ColorRefs = [];
    public int[] ResolveRefs = [];
    public int DepthRef = -1;
    public VkImageLayout DepthLayout;
    public int SubpassCount;
}

internal sealed class FramebufferObject : IcdObject
{
    public override string VkTypeName => "VkFramebuffer";
    public ImageViewObject?[] Views = [];
    public int Width, Height;
}
