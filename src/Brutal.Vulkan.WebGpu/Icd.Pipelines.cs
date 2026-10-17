using System.Text.Json;
using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

internal static unsafe partial class Icd
{
    // ---- pipeline layouts ------------------------------------------------------------------------------------------------

    public static VkResult vkCreatePipelineLayout(VkDevice device, VkPipelineLayoutCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator,
        VkPipelineLayout* pPipelineLayout)
    {
        const string fn = "vkCreatePipelineLayout";
        var layout = new PipelineLayoutObject { SetLayouts = new DescriptorSetLayoutObject?[pCreateInfo->SetLayoutCount] };
        for (var i = 0; i < pCreateInfo->SetLayoutCount; i++)
            layout.SetLayouts[i] = HandleTable.Lookup<DescriptorSetLayoutObject>(pCreateInfo->SetLayouts[i].VkHandle, fn);
        for (var i = 0; i < pCreateInfo->PushConstantRangeCount; i++)
        {
            ref var r = ref pCreateInfo->PushConstantRanges[i];
            layout.PushSize = Math.Max(layout.PushSize, (int)(r.Offset.Value + r.Size.Value));
            layout.PushStages |= r.StageFlags;
        }
        if (layout.PushSize > Profile.MaxPushConstantsSize)
            WebProfileLint.Fail(fn, $"push constants are at most {Profile.MaxPushConstantsSize} bytes", $"{layout.PushSize} bytes");
        var maxSets = Profile.Limits.MaxBindGroups - (layout.PushSize > 0 ? 1 : 0);
        if (layout.SetLayouts.Length > maxSets)
            WebProfileLint.Fail(fn, $"at most {Profile.Limits.MaxBindGroups} bind groups (one is taken by push constants)",
                $"{layout.SetLayouts.Length} descriptor sets{(layout.PushSize > 0 ? " plus push constants" : "")}");
        HandleTable.Add(layout);
        *pPipelineLayout = new VkPipelineLayout(layout.Handle);
        return VkResult.Success;
    }

    public static void vkDestroyPipelineLayout(VkDevice device, VkPipelineLayout pipelineLayout, VkAllocationCallbacks* pAllocator)
    {
        // GPUPipelineLayouts stay referenced by the pipelines created from them; only the Vulkan handle goes away
        if (HandleTable.Get<PipelineLayoutObject>(pipelineLayout.VkHandle) is { } obj)
            HandleTable.Remove(obj.Handle);
    }

    /// <summary>
    /// The GPUPipelineLayout of a pipeline: groups its shaders use get their set layout's bind group layout, unused groups the
    /// empty layout (bound to the empty bind group at draw), and push constants the reserved group 3.
    /// </summary>
    private static int GpuPipelineLayout(PipelineLayoutObject layout, uint usedGroups, PushMode push, string fn, out uint fillerMask, out uint groupCount)
    {
        var pushGroup = ShaderManifest.PushConstantGroup;
        groupCount = 0;
        for (var g = 0; g < 4; g++)
            if ((usedGroups & (1u << g)) != 0)
                groupCount = (uint)g + 1;
        if (push != PushMode.None)
        {
            if ((usedGroups & (1u << pushGroup)) != 0)
                IcdLog.Error($"{fn}: descriptor set {pushGroup} is used together with push constants; group {pushGroup} is reserved for push constants on WebGPU");
            groupCount = (uint)pushGroup + 1;
        }
        fillerMask = 0;
        var key = (int)usedGroups | ((int)push << 8);
        if (layout.GpuLayouts.TryGetValue(key, out var cached))
        {
            for (var g = 0; g < groupCount; g++)
                if (!(push != PushMode.None && g == pushGroup) && ((usedGroups & (1u << g)) == 0 || g >= layout.SetLayouts.Length || layout.SetLayouts[g] is null))
                    fillerMask |= 1u << g;
            return cached;
        }

        string json;
        using (var j = new Json())
        {
            var w = j.W;
            w.WriteStartObject();
            w.WriteStartArray("bindGroupLayouts");
            for (var g = 0; g < groupCount; g++)
            {
                if (push != PushMode.None && g == pushGroup)
                    w.WriteNumberValue(push == PushMode.Storage ? ReservedHandles.PushStorageLayout : ReservedHandles.PushUniformLayout);
                else if ((usedGroups & (1u << g)) != 0 && g < layout.SetLayouts.Length && layout.SetLayouts[g] is { } set)
                    w.WriteNumberValue(MaterializeBindGroupLayout(set, fn));
                else
                {
                    if ((usedGroups & (1u << g)) != 0)
                        IcdLog.Error($"{fn}: the shaders use descriptor set {g}, which the pipeline layout does not define");
                    w.WriteNumberValue(ReservedHandles.EmptyBindGroupLayout);
                    fillerMask |= 1u << g;
                }
            }
            w.WriteEndArray();
            w.WriteEndObject();
            json = j.ToString();
        }
        var gpu = WebIcd.Bridge.CreatePipelineLayout(json);
        if (gpu == 0)
            IcdLog.Error($"{fn}: createPipelineLayout failed for {json}");
        layout.GpuLayouts[key] = gpu;
        return gpu;
    }

    // ---- graphics pipelines ----------------------------------------------------------------------------------------------

    public static VkResult vkCreateGraphicsPipelines(VkDevice device, VkPipelineCache pipelineCache, int createInfoCount,
        VkGraphicsPipelineCreateInfo* pCreateInfos, VkAllocationCallbacks* pAllocator, VkPipeline* pPipelines)
    {
        var result = VkResult.Success;
        for (var i = 0; i < createInfoCount; i++)
        {
            var pipeline = CreateGraphicsPipeline(ref pCreateInfos[i]);
            pPipelines[i] = new VkPipeline(pipeline?.Handle ?? 0);
            if (pipeline is null)
                result = VkResult.ErrorInvalidShaderNV;
        }
        return result;
    }

    private static PipelineObject? CreateGraphicsPipeline(ref VkGraphicsPipelineCreateInfo ci)
    {
        const string fn = "vkCreateGraphicsPipelines";
        var layout = HandleTable.Lookup<PipelineLayoutObject>(ci.Layout.VkHandle, fn);
        if (layout is null)
            return null;

        ShaderStage vertex = default, fragment = default;
        for (var s = 0; s < ci.StageCount; s++)
        {
            ref var st = ref ci.Stages[s];
            var stage = ResolveStage(ref st, fn);
            if (stage.Module is null)
                return null;
            if (st.Stage == VkShaderStageFlags.VertexBit) vertex = stage;
            else if (st.Stage == VkShaderStageFlags.FragmentBit) fragment = stage;
            else
            {
                IcdLog.Unsupported(fn, $"shader stage {st.Stage} (WebGPU has vertex, fragment and compute stages only)");
                return null;
            }
        }
        if (vertex.Module is null)
        {
            IcdLog.Error($"{fn}: no vertex stage");
            return null;
        }

        // attachment formats: VkPipelineRenderingCreateInfo (dynamic rendering) or the render pass subpass
        Span<VkFormat> colorFormats = stackalloc VkFormat[8];
        var colorCount = 0;
        VkFormat depthFormat = VkFormat.Undefined, stencilFormat = VkFormat.Undefined;
        var rendering = FindNext<VkPipelineRenderingCreateInfo>(ci.Next, VkStructureType.PipelineRenderingCreateInfo);
        var renderPass = HandleTable.Get<RenderPassObject>(ci.RenderPass.VkHandle);
        if (renderPass is not null)
        {
            if (ci.Subpass != 0)
            {
                IcdLog.Unsupported(fn, $"subpass {ci.Subpass} (render passes have one subpass on WebGPU)");
                return null;
            }
            foreach (var r in renderPass.ColorRefs)
                colorFormats[colorCount++] = r >= 0 ? renderPass.Attachments[r].Format : VkFormat.Undefined;
            if (renderPass.DepthRef >= 0)
            {
                var f = renderPass.Attachments[renderPass.DepthRef].Format;
                if (Formats.HasDepth(f)) depthFormat = f;
                if (Formats.HasStencil(f)) stencilFormat = f;
            }
        }
        else if (rendering != null)
        {
            colorCount = Math.Min(8, rendering->ColorAttachmentCount);
            for (var c = 0; c < colorCount; c++)
                colorFormats[c] = rendering->ColorAttachmentFormats[c];
            depthFormat = rendering->DepthAttachmentFormat;
            stencilFormat = rendering->StencilAttachmentFormat;
        }
        var dsFormat = depthFormat != VkFormat.Undefined ? depthFormat : stencilFormat;

        // push constants and the groups the shaders use
        var push = PushMode.None;
        uint usedGroups = 0;
        foreach (var stage in (ReadOnlySpan<ShaderStage>)[vertex, fragment])
        {
            if (stage.Module is null)
                continue;
            var e = stage.Module.Entry;
            if (e.Push != PushMode.None)
            {
                if (push != PushMode.None && push != e.Push)
                    IcdLog.Error($"{fn}: vertex and fragment shaders use different push-constant modes");
                push = e.Push;
            }
            foreach (var b in e.Bindings)
                if (!(e.Push != PushMode.None && b.Group == ShaderManifest.PushConstantGroup && b.Binding == ShaderManifest.PushConstantBinding))
                    usedGroups |= 1u << Math.Min(b.Group, 31);
        }
        if (push != PushMode.None && layout.PushSize == 0)
            IcdLog.Error($"{fn}: the shaders use push constants but the pipeline layout declares no push-constant range");
        for (var g = 0; g < layout.SetLayouts.Length && g < 4; g++)
            if ((usedGroups & (1u << g)) != 0 && layout.SetLayouts[g] is { } set)
            {
                RefineFromShader(set, g, vertex.Module.Entry, fn);
                if (fragment.Module is not null)
                    RefineFromShader(set, g, fragment.Module.Entry, fn);
            }
        var gpuLayout = GpuPipelineLayout(layout, usedGroups, push, fn, out var fillerMask, out var groupCount);
        if (gpuLayout == 0)
            return null;

        var topology = ci.InputAssemblyState != null ? ci.InputAssemblyState->Topology : VkPrimitiveTopology.TriangleList;
        var topologyName = Mappings.Topology(topology);
        if (topologyName is null)
        {
            IcdLog.Unsupported(fn, $"topology {topology}");
            return null;
        }
        if (ci.DynamicState != null)
            for (var d = 0; d < ci.DynamicState->DynamicStateCount; d++)
            {
                var ds = ci.DynamicState->DynamicStates[d];
                if (ds is not (VkDynamicState.Viewport or VkDynamicState.Scissor or VkDynamicState.BlendConstants or VkDynamicState.StencilReference
                    or VkDynamicState.ViewportWithCount or VkDynamicState.ScissorWithCount or VkDynamicState.LineWidth))
                    IcdLog.Ignored(fn, $"dynamic state {ds} (baked into WebGPU pipelines; the static value is used)");
            }

        string json;
        using (var j = new Json())
        {
            var w = j.W;
            w.WriteStartObject();
            w.WriteNumber("layout", gpuLayout);
            if (vertex.Module.Name is { } label)
                w.WriteString("label", label + (fragment.Module is null ? "" : "+" + fragment.Module.Name));

            // vertex stage + vertex buffers
            w.WriteStartObject("vertex");
            WriteStage(w, vertex);
            w.WriteStartArray("buffers");
            if (ci.VertexInputState != null && !WriteVertexBuffers(w, ci.VertexInputState, fn))
                return null;
            w.WriteEndArray();
            w.WriteEndObject();

            // primitive
            var raster = ci.RasterizationState;
            w.WriteStartObject("primitive");
            w.WriteString("topology", topologyName);
            if (raster != null)
            {
                if (raster->PolygonMode != VkPolygonMode.Fill)
                {
                    IcdLog.Unsupported(fn, $"polygon mode {raster->PolygonMode}");
                    return null;
                }
                if (raster->RasterizerDiscardEnable)
                    IcdLog.Ignored(fn, "rasterizerDiscardEnable");
                if (raster->LineWidth != 1f && raster->LineWidth != 0f)
                    IcdLog.Ignored(fn, $"line width {raster->LineWidth}");
                if (raster->CullMode == VkCullModeFlags.FrontAndBack)
                    IcdLog.Ignored(fn, "cull mode FRONT_AND_BACK (culls nothing on WebGPU)");
                w.WriteString("frontFace", Mappings.FrontFace(raster->FrontFace));
                w.WriteString("cullMode", Mappings.CullMode(raster->CullMode));
                if (raster->DepthClampEnable)
                {
                    if (Profile.GpuFeatures.Contains("depth-clip-control"))
                        w.WriteBoolean("unclippedDepth", true);
                    else
                        IcdLog.Ignored(fn, "depthClampEnable (needs the depth-clip-control feature)");
                }
            }
            w.WriteEndObject();

            // depth / stencil
            if (dsFormat != VkFormat.Undefined)
            {
                var gpuDs = Formats.ToGpuTextureFormat(dsFormat);
                if (gpuDs is null)
                {
                    IcdLog.Unsupported(fn, $"depth/stencil format {dsFormat}");
                    return null;
                }
                var dss = ci.DepthStencilState;
                w.WriteStartObject("depthStencil");
                w.WriteString("format", gpuDs);
                var depthTest = dss != null && dss->DepthTestEnable;
                w.WriteBoolean("depthWriteEnabled", depthTest && dss->DepthWriteEnable);
                w.WriteString("depthCompare", depthTest ? Mappings.Compare(dss->DepthCompareOp) ?? "always" : "always");
                if (dss != null && dss->DepthBoundsTestEnable)
                    IcdLog.Ignored(fn, "depth bounds test");
                if (dss != null && dss->StencilTestEnable && Formats.HasStencil(dsFormat))
                {
                    WriteStencilFace(w, "stencilFront", ref dss->Front);
                    WriteStencilFace(w, "stencilBack", ref dss->Back);
                    if (dss->Front.CompareMask != dss->Back.CompareMask || dss->Front.WriteMask != dss->Back.WriteMask)
                        IcdLog.Ignored(fn, "different front/back stencil masks (WebGPU has one read and one write mask; using the front face's)");
                    w.WriteNumber("stencilReadMask", (uint)dss->Front.CompareMask);
                    w.WriteNumber("stencilWriteMask", (uint)dss->Front.WriteMask);
                }
                if (raster != null && raster->DepthBiasEnable)
                {
                    w.WriteNumber("depthBias", (int)raster->DepthBiasConstantFactor);
                    w.WriteNumber("depthBiasSlopeScale", raster->DepthBiasSlopeFactor);
                    w.WriteNumber("depthBiasClamp", raster->DepthBiasClamp);
                }
                w.WriteEndObject();
            }

            // multisample
            var samples = 1;
            if (ci.MultisampleState != null)
            {
                var ms = ci.MultisampleState;
                samples = Mappings.SampleCount(ms->RasterizationSamples);
                if (samples < 0)
                {
                    IcdLog.Unsupported(fn, $"{ms->RasterizationSamples} samples (WebGPU supports 1 and 4)");
                    return null;
                }
                if (ms->SampleShadingEnable)
                    IcdLog.Ignored(fn, "sampleShadingEnable");
                if (ms->AlphaToOneEnable)
                    IcdLog.Ignored(fn, "alphaToOneEnable");
                w.WriteStartObject("multisample");
                w.WriteNumber("count", samples);
                if (ms->SampleMask != null)
                    w.WriteNumber("mask", ms->SampleMask[0].Value);
                if (ms->AlphaToCoverageEnable)
                    w.WriteBoolean("alphaToCoverageEnabled", true);
                w.WriteEndObject();
            }

            // fragment stage + colour targets
            if (fragment.Module is not null)
            {
                w.WriteStartObject("fragment");
                WriteStage(w, fragment);
                w.WriteStartArray("targets");
                var blend = ci.ColorBlendState;
                if (blend != null && blend->LogicOpEnable)
                    IcdLog.Ignored(fn, "logic ops");
                for (var c = 0; c < colorCount; c++)
                {
                    var gpuFormat = colorFormats[c] == VkFormat.Undefined ? null : Formats.ToGpuTextureFormat(colorFormats[c]);
                    if (gpuFormat is null)
                    {
                        w.WriteNullValue();
                        continue;
                    }
                    w.WriteStartObject();
                    w.WriteString("format", gpuFormat);
                    if (blend != null && c < blend->AttachmentCount)
                    {
                        ref var a = ref blend->Attachments[c];
                        if (a.BlendEnable)
                        {
                            w.WriteStartObject("blend");
                            if (!WriteBlendComponent(w, "color", a.SrcColorBlendFactor, a.DstColorBlendFactor, a.ColorBlendOp, fn)
                                || !WriteBlendComponent(w, "alpha", a.SrcAlphaBlendFactor, a.DstAlphaBlendFactor, a.AlphaBlendOp, fn))
                                return null;
                            w.WriteEndObject();
                        }
                        w.WriteNumber("writeMask", (int)a.ColorWriteMask & 0xF);
                    }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            else if (colorCount > 0)
            {
                // WebGPU wants the pipeline's targets to match the pass: an empty fragment stage that writes nothing
                w.WriteStartObject("fragment");
                w.WriteNumber("module", NullFragmentModule());
                w.WriteString("entryPoint", "main");
                w.WriteStartArray("targets");
                for (var c = 0; c < colorCount; c++)
                {
                    if (colorFormats[c] == VkFormat.Undefined || Formats.ToGpuTextureFormat(colorFormats[c]) is not { } gpuFormat)
                    {
                        w.WriteNullValue();
                        continue;
                    }
                    w.WriteStartObject();
                    w.WriteString("format", gpuFormat);
                    w.WriteNumber("writeMask", 0);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WriteEndObject();
            json = j.ToString();
        }

        var isStrip = topology is VkPrimitiveTopology.TriangleStrip or VkPrimitiveTopology.LineStrip;
        var gpu = WebIcd.Bridge.CreateRenderPipeline(json);
        if (gpu == 0)
        {
            IcdLog.Error($"{fn}: createRenderPipeline failed for {json}");
            return null;
        }
        var pipeline = new PipelineObject
        {
            Gpu = gpu, BindPoint = StreamCodes.BindGraphics, PushMode = PushCode(push), FillerMask = fillerMask, GroupCount = groupCount,
            Layout = layout, IsStrip = isStrip, DescriptorJson = isStrip ? json : null,
        };
        var dynamicViewport = false;
        var dynamicScissor = false;
        if (ci.DynamicState != null)
            for (var d = 0; d < ci.DynamicState->DynamicStateCount; d++)
            {
                var ds = ci.DynamicState->DynamicStates[d];
                dynamicViewport |= ds is VkDynamicState.Viewport or VkDynamicState.ViewportWithCount;
                dynamicScissor |= ds is VkDynamicState.Scissor or VkDynamicState.ScissorWithCount;
            }
        if (ci.ViewportState != null)
        {
            if (!dynamicViewport && ci.ViewportState->ViewportCount > 0 && ci.ViewportState->Viewports != null)
            {
                pipeline.HasStaticViewport = true;
                pipeline.StaticViewport = ci.ViewportState->Viewports[0];
            }
            if (!dynamicScissor && ci.ViewportState->ScissorCount > 0 && ci.ViewportState->Scissors != null)
            {
                pipeline.HasStaticScissor = true;
                pipeline.StaticScissor = ci.ViewportState->Scissors[0];
            }
            if (ci.ViewportState->ViewportCount > 1)
                IcdLog.Ignored(fn, "multiple viewports");
        }
        HandleTable.Add(pipeline);
        return pipeline;
    }

    private static int s_nullFragment;

    private static int NullFragmentModule() =>
        s_nullFragment != 0 ? s_nullFragment : s_nullFragment = WebIcd.Bridge.CreateShaderModule("@fragment fn main() {}", "null fragment (web ICD)");

    internal static void ResetNullFragment() => s_nullFragment = 0;

    private static uint PushCode(PushMode push) => push switch
    {
        PushMode.Uniform => StreamCodes.PushUniform,
        PushMode.Storage => StreamCodes.PushStorage,
        _ => StreamCodes.PushNone,
    };

    /// <summary>A strip pipeline variant with <c>stripIndexFormat</c> for indexed draws (created on first use).</summary>
    internal static int StripVariant(PipelineObject pipeline, uint indexFormat)
    {
        ref var slot = ref indexFormat == StreamCodes.IndexUint32 ? ref pipeline.StripUint32 : ref pipeline.StripUint16;
        if (slot != 0 || pipeline.DescriptorJson is null)
            return slot != 0 ? slot : pipeline.Gpu;
        var format = indexFormat == StreamCodes.IndexUint32 ? "uint32" : "uint16";
        var json = pipeline.DescriptorJson.Replace("\"topology\":\"triangle-strip\"", $"\"topology\":\"triangle-strip\",\"stripIndexFormat\":\"{format}\"")
            .Replace("\"topology\":\"line-strip\"", $"\"topology\":\"line-strip\",\"stripIndexFormat\":\"{format}\"");
        slot = WebIcd.Bridge.CreateRenderPipeline(json);
        return slot != 0 ? slot : pipeline.Gpu;
    }

    private struct ShaderStage
    {
        public ShaderModuleObject? Module;
        public string EntryPoint;
        public VkSpecializationInfo* Specialization;
    }

    private static ShaderStage ResolveStage(ref VkPipelineShaderStageCreateInfo st, string fn)
    {
        var module = HandleTable.Lookup<ShaderModuleObject>(st.Module.VkHandle, fn);
        var entry = st.Name == null ? "main" : Str(st.Name);
        if (module is not null && entry != "main" && entry != module.Entry.EntryPoint)
            IcdLog.Warn($"{fn}: entry point {entry} requested from {module.Entry.Name}, whose manifest entry point is {module.Entry.EntryPoint}");
        return new ShaderStage { Module = module, EntryPoint = module?.Entry.EntryPoint ?? entry, Specialization = st.SpecializationInfo };
    }

    private static void WriteStage(Utf8JsonWriter w, ShaderStage stage)
    {
        w.WriteNumber("module", stage.Module!.Gpu);
        w.WriteString("entryPoint", stage.EntryPoint);
        var spec = stage.Specialization;
        if (spec == null || spec->MapEntryCount == 0)
            return;
        w.WriteStartObject("constants");
        for (var i = 0; i < spec->MapEntryCount; i++)
        {
            ref var m = ref spec->MapEntries[i];
            var data = (byte*)spec->Data + m.Offset.Value;
            stage.Module.Entry.Overrides.TryGetValue(m.ConstantID, out var type);
            var name = m.ConstantID.ToString(System.Globalization.CultureInfo.InvariantCulture);
            switch (type)
            {
                case "bool": w.WriteNumber(name, *(int*)data != 0 ? 1 : 0); break;
                case "i32": w.WriteNumber(name, *(int*)data); break;
                case "u32": w.WriteNumber(name, *(uint*)data); break;
                case "f32": w.WriteNumber(name, *(float*)data); break;
                default:
                    w.WriteNumber(name, m.Size.Value == 8 ? *(double*)data : *(float*)data);
                    if (type is null)
                        IcdLog.WarnOnce("spec|" + stage.Module.Entry.Name + m.ConstantID,
                            $"specialization constant {m.ConstantID} is not an override of {stage.Module.Entry.Name}; passed as a float");
                    break;
            }
        }
        w.WriteEndObject();
    }

    private static bool WriteVertexBuffers(Utf8JsonWriter w, VkPipelineVertexInputStateCreateInfo* vi, string fn)
    {
        var maxBinding = -1;
        for (var b = 0; b < vi->VertexBindingDescriptionCount; b++)
            maxBinding = Math.Max(maxBinding, vi->VertexBindingDescriptions[b].Binding);
        if (maxBinding >= Profile.Limits.MaxVertexBuffers)
        {
            WebProfileLint.Fail(fn, $"at most {Profile.Limits.MaxVertexBuffers} vertex buffers", $"vertex binding {maxBinding}");
            return false;
        }
        for (var slot = 0; slot <= maxBinding; slot++)
        {
            VkVertexInputBindingDescription* desc = null;
            for (var b = 0; b < vi->VertexBindingDescriptionCount; b++)
                if (vi->VertexBindingDescriptions[b].Binding == slot)
                    desc = &vi->VertexBindingDescriptions[b];
            if (desc == null)
            {
                w.WriteNullValue();
                continue;
            }
            var stride = desc->Stride.Value;
            if (!WebProfileLint.CheckAligned4(fn, "vertex stride", stride))
                return false;
            w.WriteStartObject();
            w.WriteNumber("arrayStride", stride);
            w.WriteString("stepMode", desc->InputRate == VkVertexInputRate.Instance ? "instance" : "vertex");
            w.WriteStartArray("attributes");
            for (var a = 0; a < vi->VertexAttributeDescriptionCount; a++)
            {
                ref var attr = ref vi->VertexAttributeDescriptions[a];
                if (attr.Binding != slot)
                    continue;
                var format = Formats.ToGpuVertexFormat(attr.Format);
                if (format is null)
                {
                    IcdLog.Unsupported(fn, $"vertex format {attr.Format} (location {attr.Location})");
                    return false;
                }
                w.WriteStartObject();
                w.WriteString("format", format);
                w.WriteNumber("offset", attr.Offset.Value);
                w.WriteNumber("shaderLocation", attr.Location);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return true;
    }

    private static void WriteStencilFace(Utf8JsonWriter w, string name, ref VkStencilOpState s)
    {
        w.WriteStartObject(name);
        w.WriteString("compare", Mappings.Compare(s.CompareOp) ?? "always");
        w.WriteString("failOp", Mappings.StencilOp(s.FailOp) ?? "keep");
        w.WriteString("depthFailOp", Mappings.StencilOp(s.DepthFailOp) ?? "keep");
        w.WriteString("passOp", Mappings.StencilOp(s.PassOp) ?? "keep");
        w.WriteEndObject();
    }

    private static bool WriteBlendComponent(Utf8JsonWriter w, string name, VkBlendFactor src, VkBlendFactor dst, VkBlendOp op, string fn)
    {
        var s = Mappings.BlendFactor(src);
        var d = Mappings.BlendFactor(dst);
        var o = Mappings.BlendOp(op);
        if (s is null || d is null || o is null)
        {
            IcdLog.Unsupported(fn, $"blend {src}/{dst}/{op}");
            return false;
        }
        if (o is "min" or "max")
            s = d = "one"; // WebGPU requires factor "one" for min/max; Vulkan ignores the factors
        w.WriteStartObject(name);
        w.WriteString("srcFactor", s);
        w.WriteString("dstFactor", d);
        w.WriteString("operation", o);
        w.WriteEndObject();
        return true;
    }

    // ---- compute pipelines -----------------------------------------------------------------------------------------------

    public static VkResult vkCreateComputePipelines(VkDevice device, VkPipelineCache pipelineCache, int createInfoCount,
        VkComputePipelineCreateInfo* pCreateInfos, VkAllocationCallbacks* pAllocator, VkPipeline* pPipelines)
    {
        const string fn = "vkCreateComputePipelines";
        var result = VkResult.Success;
        for (var i = 0; i < createInfoCount; i++)
        {
            ref var ci = ref pCreateInfos[i];
            pPipelines[i] = default;
            var layout = HandleTable.Lookup<PipelineLayoutObject>(ci.Layout.VkHandle, fn);
            var stage = ResolveStage(ref ci.Stage, fn);
            if (layout is null || stage.Module is null)
            {
                result = VkResult.ErrorInvalidShaderNV;
                continue;
            }
            var e = stage.Module.Entry;
            uint used = 0;
            foreach (var b in e.Bindings)
                if (!(e.Push != PushMode.None && b.Group == ShaderManifest.PushConstantGroup && b.Binding == ShaderManifest.PushConstantBinding))
                    used |= 1u << Math.Min(b.Group, 31);
            for (var g = 0; g < layout.SetLayouts.Length && g < 4; g++)
                if ((used & (1u << g)) != 0 && layout.SetLayouts[g] is { } set)
                    RefineFromShader(set, g, e, fn);
            var gpuLayout = GpuPipelineLayout(layout, used, e.Push, fn, out var fillerMask, out var groupCount);
            string json;
            using (var j = new Json())
            {
                var w = j.W;
                w.WriteStartObject();
                w.WriteNumber("layout", gpuLayout);
                w.WriteString("label", e.Name);
                w.WriteStartObject("compute");
                WriteStage(w, stage);
                w.WriteEndObject();
                w.WriteEndObject();
                json = j.ToString();
            }
            var gpu = gpuLayout == 0 ? 0 : WebIcd.Bridge.CreateComputePipeline(json);
            if (gpu == 0)
            {
                IcdLog.Error($"{fn}: createComputePipeline failed for {json}");
                result = VkResult.ErrorInvalidShaderNV;
                continue;
            }
            var pipeline = new PipelineObject
            {
                Gpu = gpu, BindPoint = StreamCodes.BindCompute, PushMode = PushCode(e.Push), FillerMask = fillerMask, GroupCount = groupCount, Layout = layout,
            };
            HandleTable.Add(pipeline);
            pPipelines[i] = new VkPipeline(pipeline.Handle);
        }
        return result;
    }

    public static void vkDestroyPipeline(VkDevice device, VkPipeline pipeline, VkAllocationCallbacks* pAllocator)
    {
        var obj = HandleTable.Get<PipelineObject>(pipeline.VkHandle);
        if (obj is null)
            return;
        WebIcd.Bridge.Release(obj.Gpu);
        if (obj.StripUint16 != 0) WebIcd.Bridge.Release(obj.StripUint16);
        if (obj.StripUint32 != 0) WebIcd.Bridge.Release(obj.StripUint32);
        HandleTable.Remove(obj.Handle);
    }
}
