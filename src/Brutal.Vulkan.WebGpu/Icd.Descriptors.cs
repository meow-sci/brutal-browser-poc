using System.Text.Json;
using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// Descriptor set layouts → GPUBindGroupLayout (materialized lazily, see <see cref="DescriptorSetLayoutObject"/>), descriptor
/// sets → CPU records whose GPUBindGroup is (re)built when bound after a write. Mapping: UNIFORM_BUFFER(_DYNAMIC) → uniform
/// buffer (hasDynamicOffset); STORAGE_BUFFER(_DYNAMIC) → storage / read-only-storage (from the WGSL access; read-only when
/// unknown and visible to the vertex stage); SAMPLED_IMAGE / INPUT_ATTACHMENT → texture; SAMPLER → sampler;
/// COMBINED_IMAGE_SAMPLER → texture at b + sampler at b + 500; STORAGE_IMAGE → storage texture.
/// </summary>
internal static unsafe partial class Icd
{
    public static VkResult vkCreateDescriptorSetLayout(VkDevice device, VkDescriptorSetLayoutCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator,
        VkDescriptorSetLayout* pSetLayout)
    {
        const string fn = "vkCreateDescriptorSetLayout";
        if ((pCreateInfo->Flags & (VkDescriptorSetLayoutCreateFlags.UpdateAfterBindPoolBit | VkDescriptorSetLayoutCreateFlags.PushDescriptorBit)) != 0)
            IcdLog.Ignored(fn, $"flags {pCreateInfo->Flags} (update-after-bind / push descriptors)");

        var bindings = new List<DescriptorBindingInfo>(pCreateInfo->BindingCount);
        var dynamicCount = 0;
        for (var i = 0; i < pCreateInfo->BindingCount; i++)
        {
            ref var b = ref pCreateInfo->Bindings[i];
            if (b.DescriptorCount == 0)
                continue;
            if (b.DescriptorCount > 1)
            {
                IcdLog.Unsupported(fn, $"descriptor arrays (binding {b.Binding} has descriptorCount {b.DescriptorCount}; WebGPU has no binding arrays)");
                return VkResult.ErrorFeatureNotPresent;
            }
            switch (b.DescriptorType)
            {
                case VkDescriptorType.UniformBuffer or VkDescriptorType.StorageBuffer or VkDescriptorType.SampledImage or VkDescriptorType.Sampler
                    or VkDescriptorType.StorageImage or VkDescriptorType.InputAttachment:
                    break;
                case VkDescriptorType.UniformBufferDynamic or VkDescriptorType.StorageBufferDynamic:
                    dynamicCount++;
                    break;
                case VkDescriptorType.CombinedImageSampler:
                    if (b.Binding + ShaderManifest.CombinedSamplerOffset >= 1000)
                    {
                        IcdLog.Unsupported(fn, $"combined image sampler at binding {b.Binding} (must be < {1000 - ShaderManifest.CombinedSamplerOffset}: its sampler moves to binding + {ShaderManifest.CombinedSamplerOffset})");
                        return VkResult.ErrorFeatureNotPresent;
                    }
                    break;
                default:
                    IcdLog.Unsupported(fn, $"descriptor type {b.DescriptorType}");
                    return VkResult.ErrorFeatureNotPresent;
            }
            SamplerObject?[]? immutable = null;
            if (b.ImmutableSamplers != null && b.DescriptorType is VkDescriptorType.Sampler or VkDescriptorType.CombinedImageSampler)
            {
                immutable = new SamplerObject?[b.DescriptorCount];
                for (var s = 0; s < b.DescriptorCount; s++)
                    immutable[s] = HandleTable.Lookup<SamplerObject>(b.ImmutableSamplers[s].VkHandle, fn);
            }
            bindings.Add(new DescriptorBindingInfo
            {
                Binding = b.Binding, Type = b.DescriptorType, Count = b.DescriptorCount, Stages = b.StageFlags, ImmutableSamplers = immutable,
            });
        }
        bindings.Sort((x, y) => x.Binding.CompareTo(y.Binding));
        if (WebIcd.Options.ValidateProfile && dynamicCount > Profile.Limits.MaxDynamicUniformBuffersPerPipelineLayout)
            WebProfileLint.Report(fn, "at most 8 dynamic uniform buffers", $"{dynamicCount} dynamic buffers in one set");

        var layout = new DescriptorSetLayoutObject { Bindings = bindings.ToArray(), DynamicCount = dynamicCount };
        HandleTable.Add(layout);
        *pSetLayout = new VkDescriptorSetLayout(layout.Handle);
        return VkResult.Success;
    }

    public static void vkDestroyDescriptorSetLayout(VkDevice device, VkDescriptorSetLayout descriptorSetLayout, VkAllocationCallbacks* pAllocator)
    {
        // the GPUBindGroupLayout stays alive: pipeline layouts and sets created from it may outlive the Vulkan handle
        if (HandleTable.Get<DescriptorSetLayoutObject>(descriptorSetLayout.VkHandle) is { } obj)
            HandleTable.Remove(obj.Handle);
    }

    public static void vkGetDescriptorSetLayoutSupport(VkDevice device, VkDescriptorSetLayoutCreateInfo* pCreateInfo, VkDescriptorSetLayoutSupport* pSupport)
    {
        var supported = true;
        for (var i = 0; i < pCreateInfo->BindingCount; i++)
        {
            ref var b = ref pCreateInfo->Bindings[i];
            supported &= b.DescriptorCount <= 1 && b.DescriptorType is VkDescriptorType.UniformBuffer or VkDescriptorType.StorageBuffer
                or VkDescriptorType.UniformBufferDynamic or VkDescriptorType.StorageBufferDynamic or VkDescriptorType.SampledImage
                or VkDescriptorType.Sampler or VkDescriptorType.CombinedImageSampler or VkDescriptorType.StorageImage or VkDescriptorType.InputAttachment;
        }
        pSupport->Supported = supported;
    }

    /// <summary>Creates the GPUBindGroupLayout of a set layout on first need.</summary>
    internal static int MaterializeBindGroupLayout(DescriptorSetLayoutObject layout, string fn)
    {
        if (layout.Bgl != 0)
            return layout.Bgl;
        string json;
        using (var j = new Json())
        {
            var w = j.W;
            w.WriteStartObject();
            w.WriteStartArray("entries");
            foreach (var b in layout.Bindings)
            {
                var visibility = Mappings.Visibility(b.Stages);
                layout.Refinements.TryGetValue(b.Binding, out var r);
                switch (b.Type)
                {
                    case VkDescriptorType.UniformBuffer:
                    case VkDescriptorType.UniformBufferDynamic:
                        BufferEntry(w, b.Binding, visibility, "uniform", b.IsDynamic);
                        break;
                    case VkDescriptorType.StorageBuffer:
                    case VkDescriptorType.StorageBufferDynamic:
                    {
                        var kind = r?.Kind is "storage" or "read-only-storage" ? r.Kind
                            : (visibility & GpuShaderStage.Vertex) != 0 ? "read-only-storage" : "storage";
                        if (kind == "storage" && (visibility & GpuShaderStage.Vertex) != 0)
                        {
                            IcdLog.Ignored(fn, $"vertex-stage visibility of writable storage buffer binding {b.Binding} (WebGPU allows only read-only storage in vertex shaders)");
                            visibility &= ~GpuShaderStage.Vertex;
                        }
                        BufferEntry(w, b.Binding, visibility, kind, b.IsDynamic);
                        layout.Effective[b.Binding] = new WgslBinding { Binding = b.Binding, Kind = kind };
                        break;
                    }
                    case VkDescriptorType.SampledImage:
                    case VkDescriptorType.InputAttachment:
                        layout.Effective[b.Binding] = TextureEntry(w, b.Binding, visibility, r);
                        break;
                    case VkDescriptorType.Sampler:
                        layout.Effective[b.Binding] = SamplerEntry(w, b.Binding, visibility, r, b.ImmutableSamplers);
                        break;
                    case VkDescriptorType.CombinedImageSampler:
                        layout.Effective[b.Binding] = TextureEntry(w, b.Binding, visibility, r);
                        layout.Refinements.TryGetValue(b.Binding + ShaderManifest.CombinedSamplerOffset, out var rs);
                        layout.Effective[b.Binding + ShaderManifest.CombinedSamplerOffset] =
                            SamplerEntry(w, b.Binding + ShaderManifest.CombinedSamplerOffset, visibility, rs, b.ImmutableSamplers);
                        break;
                    case VkDescriptorType.StorageImage:
                    {
                        var format = r?.StorageFormat;
                        if (format is null)
                        {
                            IcdLog.Error($"{fn}: the texel format of storage image binding {b.Binding} is unknown (create a pipeline using the layout or write the descriptor first); assuming rgba8unorm");
                            format = "rgba8unorm";
                        }
                        if ((visibility & GpuShaderStage.Vertex) != 0)
                            visibility &= ~GpuShaderStage.Vertex;
                        layout.Effective[b.Binding] = new WgslBinding
                        {
                            Binding = b.Binding, Kind = "storageTexture", StorageFormat = format, ViewDimension = r?.ViewDimension ?? "2d",
                            StorageAccess = r?.StorageAccess ?? "write-only",
                        };
                        w.WriteStartObject();
                        w.WriteNumber("binding", b.Binding);
                        w.WriteNumber("visibility", visibility);
                        w.WriteStartObject("storageTexture");
                        w.WriteString("access", r?.StorageAccess ?? "write-only");
                        w.WriteString("format", format);
                        w.WriteString("viewDimension", r?.ViewDimension ?? "2d");
                        w.WriteEndObject();
                        w.WriteEndObject();
                        break;
                    }
                }
            }
            w.WriteEndArray();
            w.WriteEndObject();
            json = j.ToString();
        }
        layout.Bgl = WebIcd.Bridge.CreateBindGroupLayout(json);
        if (layout.Bgl == 0)
            IcdLog.Error($"{fn}: createBindGroupLayout failed for {json}");
        return layout.Bgl;
    }

    private static void BufferEntry(Utf8JsonWriter w, int binding, int visibility, string type, bool dynamic)
    {
        w.WriteStartObject();
        w.WriteNumber("binding", binding);
        w.WriteNumber("visibility", visibility);
        w.WriteStartObject("buffer");
        w.WriteString("type", type);
        if (dynamic)
            w.WriteBoolean("hasDynamicOffset", true);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static WgslBinding TextureEntry(Utf8JsonWriter w, int binding, int visibility, WgslBinding? r)
    {
        var e = new WgslBinding
        {
            Binding = binding, Kind = "texture", SampleType = r?.SampleType ?? "float", ViewDimension = r?.ViewDimension ?? "2d",
            Multisampled = r?.Multisampled == true,
        };
        w.WriteStartObject();
        w.WriteNumber("binding", binding);
        w.WriteNumber("visibility", visibility);
        w.WriteStartObject("texture");
        w.WriteString("sampleType", e.SampleType);
        w.WriteString("viewDimension", e.ViewDimension);
        if (e.Multisampled)
            w.WriteBoolean("multisampled", true);
        w.WriteEndObject();
        w.WriteEndObject();
        return e;
    }

    private static WgslBinding SamplerEntry(Utf8JsonWriter w, int binding, int visibility, WgslBinding? r, SamplerObject?[]? immutable)
    {
        var comparison = r?.Kind == "comparison" || immutable is [{ Comparison: true }, ..];
        w.WriteStartObject();
        w.WriteNumber("binding", binding);
        w.WriteNumber("visibility", visibility);
        w.WriteStartObject("sampler");
        w.WriteString("type", comparison ? "comparison" : "filtering");
        w.WriteEndObject();
        w.WriteEndObject();
        return new WgslBinding { Binding = binding, Kind = comparison ? "comparison" : "sampler" };
    }

    /// <summary>Records WGSL-reflected binding details for a set layout that is not materialized yet.</summary>
    internal static void RefineFromShader(DescriptorSetLayoutObject layout, int group, ShaderEntry entry, string fn)
    {
        foreach (var wb in entry.Bindings)
        {
            if (wb.Group != group)
                continue;
            if (layout.Bgl == 0)
            {
                layout.Refinements[wb.Binding] = wb;
                layout.RefinedFromShader = true;
            }
            else if (layout.Effective.TryGetValue(wb.Binding, out var built) && !Compatible(built, wb))
            {
                // the layout was built from an earlier pipeline (or defaults): WebGPU will reject this pipeline/bind group
                IcdLog.ErrorOnce($"refine|{layout.Handle}|{wb.Binding}",
                    $"{fn}: shader {entry.Name} declares @group({group}) @binding({wb.Binding}) as {Describe(wb)}, but the bind group layout of this " +
                    $"descriptor set layout was already built as {Describe(built)} (from the first pipeline using it, or defaults); use separate set layouts");
            }
        }
    }

    private static bool Compatible(WgslBinding built, WgslBinding shader) => shader.Kind switch
    {
        "texture" => built.Kind == "texture" && built.ViewDimension == shader.ViewDimension && built.Multisampled == shader.Multisampled
                     && (built.SampleType == shader.SampleType || shader.SampleType == "float" && built.SampleType == "unfilterable-float"),
        "sampler" or "comparison" => built.Kind == shader.Kind,
        "storage" => built.Kind == "storage",
        "read-only-storage" => built.Kind is "read-only-storage" or "storage",
        "storageTexture" => built.Kind == "storageTexture" && built.StorageFormat == shader.StorageFormat && built.ViewDimension == shader.ViewDimension,
        _ => true,
    };

    private static string Describe(WgslBinding b) => b.Kind switch
    {
        "texture" => $"texture<{b.SampleType}, {b.ViewDimension}{(b.Multisampled ? ", multisampled" : "")}>",
        "storageTexture" => $"storage texture<{b.StorageFormat}, {b.StorageAccess}, {b.ViewDimension}>",
        _ => b.Kind,
    };

    // ---- pools and sets --------------------------------------------------------------------------------------------------

    public static VkResult vkCreateDescriptorPool(VkDevice device, VkDescriptorPoolCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator,
        VkDescriptorPool* pDescriptorPool)
    {
        var pool = new DescriptorPoolObject { MaxSets = pCreateInfo->MaxSets };
        HandleTable.Add(pool);
        *pDescriptorPool = new VkDescriptorPool(pool.Handle);
        return VkResult.Success;
    }

    public static void vkDestroyDescriptorPool(VkDevice device, VkDescriptorPool descriptorPool, VkAllocationCallbacks* pAllocator)
    {
        var pool = HandleTable.Get<DescriptorPoolObject>(descriptorPool.VkHandle);
        if (pool is null)
            return;
        FreeAllSets(pool);
        HandleTable.Remove(pool.Handle);
    }

    public static VkResult vkResetDescriptorPool(VkDevice device, VkDescriptorPool descriptorPool, VkFlags flags)
    {
        if (HandleTable.Lookup<DescriptorPoolObject>(descriptorPool.VkHandle, "vkResetDescriptorPool") is { } pool)
            FreeAllSets(pool);
        return VkResult.Success;
    }

    private static void FreeAllSets(DescriptorPoolObject pool)
    {
        foreach (var set in pool.Sets)
            ReleaseSet(set);
        pool.Sets.Clear();
    }

    private static void ReleaseSet(DescriptorSetObject set)
    {
        if (set.BindGroup != 0)
            WebIcd.Bridge.Release(set.BindGroup);
        set.BindGroup = 0;
        HandleTable.Remove(set.Handle);
    }

    public static VkResult vkAllocateDescriptorSets(VkDevice device, VkDescriptorSetAllocateInfo* pAllocateInfo, VkDescriptorSet* pDescriptorSets)
    {
        const string fn = "vkAllocateDescriptorSets";
        var pool = HandleTable.Lookup<DescriptorPoolObject>(pAllocateInfo->DescriptorPool.VkHandle, fn);
        if (pool is null)
            return VkResult.ErrorOutOfPoolMemory;
        if (pool.Sets.Count + pAllocateInfo->DescriptorSetCount > pool.MaxSets)
            return VkResult.ErrorOutOfPoolMemory;
        for (var i = 0; i < pAllocateInfo->DescriptorSetCount; i++)
        {
            var layout = HandleTable.Lookup<DescriptorSetLayoutObject>(pAllocateInfo->SetLayouts[i].VkHandle, fn);
            if (layout is null)
            {
                for (var k = 0; k < i; k++)
                    pDescriptorSets[k] = default;
                return VkResult.ErrorOutOfPoolMemory;
            }
            var set = new DescriptorSetObject { Layout = layout, Pool = pool, Slots = new DescriptorSlot[layout.Bindings.Length] };
            for (var b = 0; b < layout.Bindings.Length; b++)
                if (layout.Bindings[b].ImmutableSamplers is [var immutable, ..])
                    set.Slots[b].Sampler = immutable;
            HandleTable.Add(set);
            pool.Sets.Add(set);
            pDescriptorSets[i] = new VkDescriptorSet(set.Handle);
        }
        return VkResult.Success;
    }

    public static VkResult vkFreeDescriptorSets(VkDevice device, VkDescriptorPool descriptorPool, int descriptorSetCount, VkDescriptorSet* pDescriptorSets)
    {
        for (var i = 0; i < descriptorSetCount; i++)
        {
            var set = HandleTable.Get<DescriptorSetObject>(pDescriptorSets[i].VkHandle);
            if (set is null)
                continue;
            set.Pool.Sets.Remove(set);
            ReleaseSet(set);
        }
        return VkResult.Success;
    }

    public static void vkUpdateDescriptorSets(VkDevice device, int descriptorWriteCount, VkWriteDescriptorSet* pDescriptorWrites,
        int descriptorCopyCount, VkCopyDescriptorSet* pDescriptorCopies)
    {
        const string fn = "vkUpdateDescriptorSets";
        for (var i = 0; i < descriptorWriteCount; i++)
        {
            ref var write = ref pDescriptorWrites[i];
            var set = HandleTable.Lookup<DescriptorSetObject>(write.DstSet.VkHandle, fn);
            if (set is null)
                continue;
            var index = set.Layout.IndexOf(write.DstBinding);
            for (var e = 0; e < write.DescriptorCount; e++, index++)
            {
                // consecutive descriptors continue into the next binding (bindings have one element each here)
                if (index < 0 || index >= set.Layout.Bindings.Length)
                {
                    IcdLog.Error($"{fn}: write to binding {write.DstBinding} (+{e}) of {set} which its layout does not declare");
                    break;
                }
                var info = set.Layout.Bindings[index];
                ref var slot = ref set.Slots[index];
                switch (write.DescriptorType)
                {
                    case VkDescriptorType.UniformBuffer or VkDescriptorType.UniformBufferDynamic or VkDescriptorType.StorageBuffer
                        or VkDescriptorType.StorageBufferDynamic:
                    {
                        ref var bi = ref write.BufferInfo[e];
                        slot.Buffer = HandleTable.Lookup<BufferObject>(bi.Buffer.VkHandle, fn);
                        slot.Offset = bi.Offset.Value;
                        slot.Range = bi.Range.Value;
                        break;
                    }
                    case VkDescriptorType.SampledImage or VkDescriptorType.StorageImage or VkDescriptorType.InputAttachment:
                        slot.View = HandleTable.Lookup<ImageViewObject>(write.ImageInfo[e].ImageView.VkHandle, fn);
                        HintFromView(set.Layout, info.Binding, slot.View, write.DescriptorType == VkDescriptorType.StorageImage);
                        break;
                    case VkDescriptorType.Sampler:
                        if (info.ImmutableSamplers is null)
                            slot.Sampler = HandleTable.Lookup<SamplerObject>(write.ImageInfo[e].Sampler.VkHandle, fn);
                        HintFromSampler(set.Layout, info.Binding, slot.Sampler);
                        break;
                    case VkDescriptorType.CombinedImageSampler:
                        slot.View = HandleTable.Lookup<ImageViewObject>(write.ImageInfo[e].ImageView.VkHandle, fn);
                        if (info.ImmutableSamplers is null)
                            slot.Sampler = HandleTable.Lookup<SamplerObject>(write.ImageInfo[e].Sampler.VkHandle, fn);
                        HintFromView(set.Layout, info.Binding, slot.View, false);
                        HintFromSampler(set.Layout, info.Binding + ShaderManifest.CombinedSamplerOffset, slot.Sampler);
                        break;
                    default:
                        IcdLog.Unsupported(fn, $"descriptor type {write.DescriptorType}");
                        break;
                }
                set.Dirty = true;
            }
        }
        for (var i = 0; i < descriptorCopyCount; i++)
        {
            ref var copy = ref pDescriptorCopies[i];
            var src = HandleTable.Lookup<DescriptorSetObject>(copy.SrcSet.VkHandle, fn);
            var dst = HandleTable.Lookup<DescriptorSetObject>(copy.DstSet.VkHandle, fn);
            if (src is null || dst is null)
                continue;
            var si = src.Layout.IndexOf(copy.SrcBinding);
            var di = dst.Layout.IndexOf(copy.DstBinding);
            for (var e = 0; e < copy.DescriptorCount && si >= 0 && di >= 0 && si < src.Slots.Length && di < dst.Slots.Length; e++, si++, di++)
                dst.Slots[di] = src.Slots[si];
            dst.Dirty = true;
        }
    }

    private static void HintFromView(DescriptorSetLayoutObject layout, int binding, ImageViewObject? view, bool storage)
    {
        if (layout.Bgl != 0 || view is null || layout.Refinements.ContainsKey(binding))
            return;
        layout.Refinements[binding] = new WgslBinding
        {
            Binding = binding,
            Kind = storage ? "storageTexture" : "texture",
            ViewDimension = Mappings.ViewDimension(view.ViewType) ?? "2d",
            SampleType = Formats.HasDepth(view.Format) ? "depth" : Formats.SampleType(view.Format),
            Multisampled = view.Image.Samples > 1,
            StorageFormat = storage ? Formats.ToGpuTextureFormat(view.Format) : null,
        };
    }

    private static void HintFromSampler(DescriptorSetLayoutObject layout, int binding, SamplerObject? sampler)
    {
        if (layout.Bgl != 0 || sampler is null || layout.Refinements.ContainsKey(binding))
            return;
        layout.Refinements[binding] = new WgslBinding { Binding = binding, Kind = sampler.Comparison ? "comparison" : "sampler" };
    }

    /// <summary>Builds (or rebuilds after writes) the GPUBindGroup of a descriptor set.</summary>
    internal static int EnsureBindGroup(DescriptorSetObject set, string fn)
    {
        if (!set.Dirty && set.BindGroup != 0)
            return set.BindGroup;
        var bgl = MaterializeBindGroupLayout(set.Layout, fn);
        if (bgl == 0)
            return 0;
        string json;
        using (var j = new Json())
        {
            var w = j.W;
            w.WriteStartObject();
            w.WriteNumber("layout", bgl);
            w.WriteStartArray("entries");
            for (var i = 0; i < set.Layout.Bindings.Length; i++)
            {
                var info = set.Layout.Bindings[i];
                ref var slot = ref set.Slots[i];
                switch (info.Type)
                {
                    case VkDescriptorType.UniformBuffer or VkDescriptorType.UniformBufferDynamic or VkDescriptorType.StorageBuffer
                        or VkDescriptorType.StorageBufferDynamic:
                    {
                        if (slot.Buffer is null)
                        {
                            StandInWarning(set, info, fn);
                            w.WriteStartObject();
                            w.WriteNumber("binding", info.Binding);
                            w.WriteNumber("buffer", StandIns.Buffer);
                            w.WriteNumber("offset", 0);
                            w.WriteNumber("size", info.IsDynamic ? 256 : 65536);
                            w.WriteEndObject();
                            break;
                        }
                        var uniform = info.Type is VkDescriptorType.UniformBuffer or VkDescriptorType.UniformBufferDynamic;
                        if (!WebProfileLint.Aligned(slot.Offset, 256))
                            WebProfileLint.Fail(fn, "buffer binding offsets are multiples of 256", $"{set} binding {info.Binding} offset {slot.Offset}");
                        var remaining = slot.Buffer.GpuSize > slot.Offset ? slot.Buffer.GpuSize - slot.Offset : 0;
                        ulong size;
                        if (Mappings.IsWholeSize(slot.Range))
                        {
                            size = remaining;
                            if (info.IsDynamic && size > 256)
                            {
                                IcdLog.WarnOnce("dyn-whole|" + set.Layout.Handle,
                                    $"{fn}: dynamic {info.Type} binding {info.Binding} uses VK_WHOLE_SIZE; WebGPU binds a fixed size, using 256 bytes (pass an explicit range)");
                                size = 256;
                            }
                        }
                        else
                            size = Math.Min((slot.Range + 3) & ~3UL, remaining);
                        var max = uniform ? (ulong)Profile.Limits.MaxUniformBufferBindingSize : (ulong)Profile.Limits.MaxStorageBufferBindingSize;
                        if (size > max)
                        {
                            IcdLog.WarnOnce("bindsize|" + set.Layout.Handle + "|" + info.Binding, $"{fn}: binding {info.Binding} range {size} exceeds the WebGPU limit {max}; clamped");
                            size = max;
                        }
                        w.WriteStartObject();
                        w.WriteNumber("binding", info.Binding);
                        w.WriteNumber("buffer", slot.Buffer.Gpu);
                        w.WriteNumber("offset", slot.Offset);
                        w.WriteNumber("size", size);
                        w.WriteEndObject();
                        break;
                    }
                    case VkDescriptorType.SampledImage or VkDescriptorType.StorageImage or VkDescriptorType.InputAttachment:
                    {
                        var view = slot.View?.Gpu ?? StandInView(set, info, info.Binding, fn);
                        if (view == 0)
                            return MissingDescriptor(set, info, fn);
                        if (slot.View is not null)
                            CheckView(set, info.Binding, slot.View, fn);
                        ViewEntry(w, info.Binding, view);
                        break;
                    }
                    case VkDescriptorType.Sampler:
                        SamplerRef(w, info.Binding, slot.Sampler?.Gpu ?? StandInSampler(set, info, info.Binding, fn));
                        break;
                    case VkDescriptorType.CombinedImageSampler:
                    {
                        var view = slot.View?.Gpu ?? StandInView(set, info, info.Binding, fn);
                        if (view == 0)
                            return MissingDescriptor(set, info, fn);
                        if (slot.View is not null)
                            CheckView(set, info.Binding, slot.View, fn);
                        ViewEntry(w, info.Binding, view);
                        var samplerBinding = info.Binding + ShaderManifest.CombinedSamplerOffset;
                        SamplerRef(w, samplerBinding, slot.Sampler?.Gpu ?? StandInSampler(set, info, samplerBinding, fn));
                        break;
                    }
                }
            }
            w.WriteEndArray();
            w.WriteEndObject();
            json = j.ToString();
        }
        var group = WebIcd.Bridge.CreateBindGroup(json);
        if (group == 0)
        {
            IcdLog.Error($"{fn}: createBindGroup failed for {set}: {json}");
            return 0;
        }
        if (set.BindGroup != 0)
            WebIcd.Bridge.Release(set.BindGroup);
        set.BindGroup = group;
        set.Dirty = false;
        return group;
    }

    /// <summary>Explains the WebGPU validation error a view/layout mismatch will cause (sample type, dimension).</summary>
    private static void CheckView(DescriptorSetObject set, int binding, ImageViewObject view, string fn)
    {
        if (!set.Layout.Effective.TryGetValue(binding, out var built) || built.Kind != "texture")
            return;
        var formatType = Formats.HasDepth(view.Format) ? "depth" : Formats.SampleType(view.Format);
        var ok = built.SampleType == formatType
                 || built.SampleType == "unfilterable-float" && formatType is "float" or "depth"
                 || built.SampleType == "float" && formatType == "unfilterable-float" && Profile.GpuFeatures.Contains("float32-filterable");
        var dimension = Mappings.ViewDimension(view.ViewType);
        if (!ok || dimension != built.ViewDimension)
            IcdLog.ErrorOnce($"view|{set.Layout.Handle}|{binding}|{view.Format}|{view.ViewType}",
                $"{fn}: {view} ({view.Format}, {dimension}) is bound to {set} binding {binding}, whose bind group layout expects {Describe(built)}; " +
                "WebGPU rejects the bind group (depth and 32-bit float views need texture_depth_*/unfilterable sampling in the shader)");
    }

    private static int MissingDescriptor(DescriptorSetObject set, DescriptorBindingInfo info, string fn)
    {
        IcdLog.ErrorOnce("missing|" + set.Handle + "|" + info.Binding,
            $"{fn}: {set} binding {info.Binding} ({info.Type}) was never written and has no stand-in; WebGPU bind groups must be complete");
        return 0;
    }

    /// <summary>
    /// Stand-ins for descriptors the application never wrote (legal in Vulkan when the shaders do not use them; WebGPU bind
    /// groups must be complete): a 64 KiB buffer, 1x1 2D float and depth textures, a filtering and a comparison sampler.
    /// </summary>
    private static class StandIns
    {
        private static int s_buffer, s_floatView, s_depthView, s_sampler, s_comparisonSampler;

        public static int Buffer => s_buffer != 0 ? s_buffer
            : s_buffer = WebIcd.Bridge.CreateBuffer(65536, GpuBufferUsage.Uniform | GpuBufferUsage.Storage | GpuBufferUsage.CopyDst, "unwritten descriptor");

        public static int View(WgslBinding? built)
        {
            if (built is null || built.Kind != "texture" || built.ViewDimension != "2d" || built.Multisampled || built.SampleType is "sint" or "uint")
                return 0;
            if (built.SampleType == "depth")
                return s_depthView != 0 ? s_depthView : s_depthView = Texture("depth32float");
            return s_floatView != 0 ? s_floatView : s_floatView = Texture("rgba8unorm");
        }

        public static int Sampler(WgslBinding? built) => built?.Kind == "comparison"
            ? s_comparisonSampler != 0 ? s_comparisonSampler : s_comparisonSampler = WebIcd.Bridge.CreateSampler("{\"compare\":\"less\"}")
            : s_sampler != 0 ? s_sampler : s_sampler = WebIcd.Bridge.CreateSampler("{}");

        private static int Texture(string format)
        {
            var texture = WebIcd.Bridge.CreateTexture($"{{\"size\":[1,1,1],\"format\":\"{format}\",\"usage\":{GpuTextureUsage.TextureBinding}}}");
            return WebIcd.Bridge.CreateTextureView(texture, "{}");
        }

        internal static void Reset() => s_buffer = s_floatView = s_depthView = s_sampler = s_comparisonSampler = 0;
    }

    private static int StandInView(DescriptorSetObject set, DescriptorBindingInfo info, int binding, string fn)
    {
        set.Layout.Effective.TryGetValue(binding, out var built);
        var view = info.Type == VkDescriptorType.StorageImage ? 0 : StandIns.View(built);
        if (view != 0)
            StandInWarning(set, info, fn);
        return view;
    }

    private static int StandInSampler(DescriptorSetObject set, DescriptorBindingInfo info, int binding, string fn)
    {
        StandInWarning(set, info, fn);
        set.Layout.Effective.TryGetValue(binding, out var built);
        return StandIns.Sampler(built);
    }

    internal static void ResetStandIns() => StandIns.Reset();

    private static void StandInWarning(DescriptorSetObject set, DescriptorBindingInfo info, string fn) =>
        IcdLog.WarnOnce("standin|" + set.Handle + "|" + info.Binding,
            $"{fn}: {set} binding {info.Binding} ({info.Type}) was never written; a stand-in resource is bound (WebGPU bind groups must be complete)");

    private static void ViewEntry(Utf8JsonWriter w, int binding, int view)
    {
        w.WriteStartObject();
        w.WriteNumber("binding", binding);
        w.WriteNumber("view", view);
        w.WriteEndObject();
    }

    private static void SamplerRef(Utf8JsonWriter w, int binding, int sampler)
    {
        w.WriteStartObject();
        w.WriteNumber("binding", binding);
        w.WriteNumber("sampler", sampler);
        w.WriteEndObject();
    }
}
