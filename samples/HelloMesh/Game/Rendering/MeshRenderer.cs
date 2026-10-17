using Brutal;
using System.Runtime.CompilerServices;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using HelloMesh.Assets;

namespace HelloMesh.Rendering;

/// <summary>
/// Owns the GPU copy of a <see cref="GlbModel"/> and draws it: descriptor set layout (dynamic UBO, texture, sampler),
/// pipeline layout, dynamic-rendering pipeline, per-frame uniform buffers and one draw per primitive.
/// </summary>
public sealed class MeshRenderer : IDisposable
{
    private readonly DeviceEx _device;
    private readonly GlbModel _model;
    private readonly List<GpuMesh> _meshes = [];
    private readonly List<GpuTexture> _textures = [];
    private readonly DynamicUniformBuffer[] _uniforms = new DynamicUniformBuffer[FrameResources.FramesInFlight];
    private readonly VkDescriptorSet[,] _descriptorSets;
    private DescriptorSetLayoutEx _setLayout = null!;
    private DescriptorPoolEx _descriptorPool = null!;
    private VkPipelineLayout _pipelineLayout;
    private VkPipeline _pipeline;

    /// <summary>Uploads the model, then builds the pipeline for the given colour format.</summary>
    public MeshRenderer(VulkanContext context, GlbModel model, VkFormat colorFormat)
    {
        _device = context.Device;
        _model = model;
        Upload(context);
        for (var frame = 0; frame < _uniforms.Length; frame++)
            _uniforms[frame] = new DynamicUniformBuffer(_device, Math.Max(model.Primitives.Count, 1), context.UniformOffsetAlignment);
        CreateDescriptors();
        _descriptorSets = AllocateDescriptorSets();
        CreatePipeline(colorFormat);
    }

    private void Upload(VulkanContext context)
    {
        using var staging = _device.CreateStagingPool(context.Queue, 1);
        var cmd = staging.NextCommandBuffer();
        cmd.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
        _textures.Add(GpuTexture.UploadWhite(_device, staging, cmd));
        foreach (var image in _model.Images)
            _textures.Add(GpuTexture.Upload(_device, staging, cmd, image.Width, image.Height, image.Rgba));
        foreach (var primitive in _model.Primitives)
            _meshes.Add(GpuMesh.Upload(_device, staging, cmd, primitive));
        cmd.End();
        staging.Submit().Wait();
    }

    private unsafe void CreateDescriptors()
    {
        var bindings = stackalloc VkDescriptorSetLayoutBinding[3];
        bindings[0] = new VkDescriptorSetLayoutBinding
        {
            Binding = 0, DescriptorType = VkDescriptorType.UniformBufferDynamic, DescriptorCount = 1,
            StageFlags = VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit,
        };
        bindings[1] = new VkDescriptorSetLayoutBinding
        {
            Binding = 1, DescriptorType = VkDescriptorType.SampledImage, DescriptorCount = 1, StageFlags = VkShaderStageFlags.FragmentBit,
        };
        bindings[2] = new VkDescriptorSetLayoutBinding
        {
            Binding = 2, DescriptorType = VkDescriptorType.Sampler, DescriptorCount = 1, StageFlags = VkShaderStageFlags.FragmentBit,
        };
        _setLayout = _device.CreateDescriptorSetLayout(new DescriptorSetLayoutEx.CreateInfo { Bindings = new Span<VkDescriptorSetLayoutBinding>(bindings, 3) }, null);

        var sets = FrameResources.FramesInFlight * _textures.Count;
        var sizes = stackalloc VkDescriptorPoolSize[3];
        sizes[0] = new VkDescriptorPoolSize { Type = VkDescriptorType.UniformBufferDynamic, DescriptorCount = sets };
        sizes[1] = new VkDescriptorPoolSize { Type = VkDescriptorType.SampledImage, DescriptorCount = sets };
        sizes[2] = new VkDescriptorPoolSize { Type = VkDescriptorType.Sampler, DescriptorCount = sets };
        _descriptorPool = _device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo { MaxSets = sets, PoolSizes = new Span<VkDescriptorPoolSize>(sizes, 3) }, null);
    }

    /// <summary>One set per (frame in flight, texture): the uniform buffer differs per frame, the texture per material.</summary>
    private unsafe VkDescriptorSet[,] AllocateDescriptorSets()
    {
        var sets = new VkDescriptorSet[FrameResources.FramesInFlight, _textures.Count];
        var writes = stackalloc VkWriteDescriptorSet[3];
        for (var frame = 0; frame < FrameResources.FramesInFlight; frame++)
            for (var texture = 0; texture < _textures.Count; texture++)
            {
                var set = sets[frame, texture] = _device.AllocateDescriptorSet(_descriptorPool, _setLayout);
                var bufferInfo = new VkDescriptorBufferInfo { Buffer = _uniforms[frame].Buffer, Offset = default, Range = (ByteSize64)(ulong)Unsafe.SizeOf<PerDraw>() };
                var imageInfo = new VkDescriptorImageInfo { ImageView = _textures[texture].View, ImageLayout = VkImageLayout.ShaderReadOnlyOptimal };
                var samplerInfo = new VkDescriptorImageInfo { Sampler = _textures[texture].Sampler };
                writes[0] = new VkWriteDescriptorSet { DstSet = set, DstBinding = 0, DescriptorCount = 1, DescriptorType = VkDescriptorType.UniformBufferDynamic, BufferInfo = &bufferInfo };
                writes[1] = new VkWriteDescriptorSet { DstSet = set, DstBinding = 1, DescriptorCount = 1, DescriptorType = VkDescriptorType.SampledImage, ImageInfo = &imageInfo };
                writes[2] = new VkWriteDescriptorSet { DstSet = set, DstBinding = 2, DescriptorCount = 1, DescriptorType = VkDescriptorType.Sampler, ImageInfo = &samplerInfo };
                _device.UpdateDescriptorSets(new ReadOnlySpan<VkWriteDescriptorSet>(writes, 3), []);
            }
        return sets;
    }

    private unsafe void CreatePipeline(VkFormat colorFormat)
    {
        _pipelineLayout = _device.CreatePipelineLayout([_setLayout.Handle], [], null);
        var vertexModule = ShaderModuleExtensions.FromSpirv(_device, EmbeddedAssets.Read("Shaders/mesh.vert.spv"), null);
        var fragmentModule = ShaderModuleExtensions.FromSpirv(_device, EmbeddedAssets.Read("Shaders/mesh.frag.spv"), null);
        using var stages = new ShaderStages(_device, [Presets.ShaderStage.Vertex(vertexModule), Presets.ShaderStage.Fragment(fragmentModule)], null);

        var binding = new VkVertexInputBindingDescription { Binding = 0, Stride = (ByteSize32)Vertex.Stride, InputRate = VkVertexInputRate.Vertex };
        var attributes = stackalloc VkVertexInputAttributeDescription[3];
        attributes[0] = new VkVertexInputAttributeDescription { Location = 0, Binding = 0, Format = VkFormat.R32G32B32SFloat, Offset = (ByteSize32)0 };
        attributes[1] = new VkVertexInputAttributeDescription { Location = 1, Binding = 0, Format = VkFormat.R32G32B32SFloat, Offset = (ByteSize32)12 };
        attributes[2] = new VkVertexInputAttributeDescription { Location = 2, Binding = 0, Format = VkFormat.R32G32SFloat, Offset = (ByteSize32)24 };
        var vertexInput = new VkPipelineVertexInputStateCreateInfo
        {
            VertexBindingDescriptionCount = 1, VertexBindingDescriptions = &binding,
            VertexAttributeDescriptionCount = 3, VertexAttributeDescriptions = attributes,
        };
        var viewportState = new VkPipelineViewportStateCreateInfo { ViewportCount = 1, ScissorCount = 1 };
        var dynamicStates = stackalloc VkDynamicState[2] { VkDynamicState.Viewport, VkDynamicState.Scissor };
        var dynamicState = new VkPipelineDynamicStateCreateInfo { DynamicStateCount = 2, DynamicStates = dynamicStates };
        var renderingInfo = new VkPipelineRenderingCreateInfo { ColorAttachmentCount = 1, ColorAttachmentFormats = &colorFormat, DepthAttachmentFormat = Swapchain.DepthFormat };

        _pipeline = _device.CreateGraphicsPipeline(default, new VkGraphicsPipelineCreateInfo
        {
            Next = &renderingInfo,
            StageCount = stages.Count,
            Stages = stages,
            VertexInputState = &vertexInput,
            InputAssemblyState = Presets.InputAssembly.TriangleList,
            ViewportState = &viewportState,
            RasterizationState = Presets.Rasterization.Fill.CullBack,
            MultisampleState = Presets.Multisample.None,
            DepthStencilState = Presets.DepthStencil.DepthTestWrite,
            ColorBlendState = Presets.BlendState.BlendNone,
            DynamicState = &dynamicState,
            Layout = _pipelineLayout,
        }, null);
    }

    /// <summary>Records the draws of every primitive; must be called inside a dynamic-rendering pass of <paramref name="extent"/>.</summary>
    public void Draw(CommandBuffer cmd, int frame, VkExtent2D extent, in float4x4 view, in float4x4 projection, float3 lightDirection)
    {
        cmd.BindPipeline(VkPipelineBindPoint.Graphics, _pipeline);
        cmd.SetViewport(0, [new VkViewport { Width = extent.Width, Height = extent.Height, MinDepth = 0f, MaxDepth = 1f }]);
        cmd.SetScissor(0, [new VkRect2D(extent)]);

        var light = new float4(float3.Normalize(lightDirection), 0f);
        for (var i = 0; i < _meshes.Count; i++)
        {
            var mesh = _meshes[i];
            var material = _model.Materials[mesh.MaterialIndex];
            var offset = _uniforms[frame].Write(i, new PerDraw { Model = mesh.World, View = view, Proj = projection, BaseColor = material.BaseColor, LightDir = light });
            var texture = material.TextureIndex is { } image ? image + 1 : 0;
            cmd.BindVertexBuffer(0, mesh.VertexBuffer);
            cmd.BindIndexBuffer(mesh.IndexBuffer, default, mesh.IndexType);
            cmd.BindDescriptorSets(VkPipelineBindPoint.Graphics, _pipelineLayout, 0, [_descriptorSets[frame, texture]], [(ByteSize32)offset]);
            cmd.DrawIndexed(mesh.IndexCount, 1, 0, 0, 0);
        }
    }

    public void Dispose()
    {
        _device.DestroyPipeline(_pipeline, null);
        _device.DestroyPipelineLayout(_pipelineLayout, null);
        _descriptorPool.Dispose();
        _setLayout.Dispose();
        foreach (var uniforms in _uniforms)
            uniforms.Dispose();
        foreach (var mesh in _meshes)
            mesh.Dispose();
        foreach (var texture in _textures)
            texture.Dispose();
    }
}
