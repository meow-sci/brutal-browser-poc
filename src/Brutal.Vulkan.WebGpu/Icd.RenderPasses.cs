using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// Render passes: dynamic rendering (vkCmdBeginRendering) and single-subpass VkRenderPass/VkFramebuffer both become one
/// <see cref="Op.BeginRenderPass"/> record. Views are resolved at replay, so swapchain image views pick up the canvas texture
/// of the frame being replayed. LOAD/NONE → load, CLEAR/DONT_CARE → clear; STORE/NONE → store, DONT_CARE → discard, except
/// for images that may be read later (SAMPLED/TRANSFER_SRC/INPUT_ATTACHMENT usage), which are stored (WebGPU "discard"
/// reads back as zero).
/// </summary>
internal static unsafe partial class Icd
{
    private struct ColorAttachmentRecord
    {
        public ImageViewObject? View, Resolve;
        public VkAttachmentLoadOp Load;
        public VkAttachmentStoreOp Store;
        public VkClearColorValue Clear;
    }

    private struct DepthAttachmentRecord
    {
        public ImageViewObject? View;
        public VkAttachmentLoadOp DepthLoad, StencilLoad;
        public VkAttachmentStoreOp DepthStore, StencilStore;
        public float ClearDepth;
        public int ClearStencil;
        /// <summary><see cref="StreamCodes.DepthReadOnly"/> / <see cref="StreamCodes.StencilReadOnly"/> from the attachment layout.</summary>
        public uint ReadOnly;
    }

    /// <summary>Read-only depth/stencil layouts: the aspect can be sampled during the pass (WebGPU depthReadOnly/stencilReadOnly).</summary>
    private static uint ReadOnlyFlags(VkImageLayout layout) => layout switch
    {
        VkImageLayout.DepthStencilReadOnlyOptimal or VkImageLayout.ReadOnlyOptimal => StreamCodes.DepthReadOnly | StreamCodes.StencilReadOnly,
        VkImageLayout.DepthReadOnlyStencilAttachmentOptimal or VkImageLayout.DepthReadOnlyOptimal => StreamCodes.DepthReadOnly,
        VkImageLayout.DepthAttachmentStencilReadOnlyOptimal or VkImageLayout.StencilReadOnlyOptimal => StreamCodes.StencilReadOnly,
        _ => 0,
    };

    /// <summary>Scratch for the colour attachments of one vkCmdBegin* call (recording is single-threaded).</summary>
    private static readonly ColorAttachmentRecord[] s_colorScratch = new ColorAttachmentRecord[8];

    private static void WriteBeginRenderPass(CommandBufferObject cb, string fn, VkRect2D area, ReadOnlySpan<ColorAttachmentRecord> colors,
        in DepthAttachmentRecord depth)
    {
        var hasDepth = depth.View is not null;
        var p = cb.Stream.Record(Op.BeginRenderPass, 8 + 9 * colors.Length + (hasDepth ? 8 : 0));
        p[0] = (uint)colors.Length;
        p[1] = hasDepth ? StreamCodes.RenderPassHasDepthStencil : 0;
        p[2] = (uint)area.Offset.X;
        p[3] = (uint)area.Offset.Y;
        p[4] = (uint)area.Extent.Width;
        p[5] = (uint)area.Extent.Height;
        int width = 0, height = 0;
        var q = p + 8;
        foreach (ref readonly var c in colors)
        {
            if (c.View is { } v)
            {
                width = v.Width;
                height = v.Height;
            }
            q[0] = (uint)(c.View?.AttachmentGpu ?? 0);
            q[1] = (uint)(c.Resolve?.AttachmentGpu ?? 0);
            q[2] = Mappings.LoadOp(c.Load);
            q[3] = StoreFor(c.View, c.Store, fn);
            var format = c.View?.Format ?? VkFormat.Undefined;
            q[4] = !Formats.IsInteger(format) ? StreamCodes.ClearFloat : Formats.SampleType(format) == "sint" ? StreamCodes.ClearSint : StreamCodes.ClearUint;
            var clear = c.Clear;
            var raw = (uint*)&clear;
            q[5] = raw[0]; q[6] = raw[1]; q[7] = raw[2]; q[8] = raw[3];
            q += 9;
        }
        if (hasDepth)
        {
            var v = depth.View!;
            if (width == 0)
            {
                width = v.Width;
                height = v.Height;
            }
            q[0] = (uint)v.AttachmentGpu;
            var hasD = Formats.HasDepth(v.Format);
            var hasS = Formats.HasStencil(v.Format);
            var depthRo = hasD && (depth.ReadOnly & StreamCodes.DepthReadOnly) != 0;
            var stencilRo = hasS && (depth.ReadOnly & StreamCodes.StencilReadOnly) != 0;
            // read-only aspects take no load/store ops in WebGPU
            q[1] = hasD && !depthRo ? Mappings.LoadOp(depth.DepthLoad) : StreamCodes.LoadOmit;
            q[2] = hasD && !depthRo ? StoreFor(v, depth.DepthStore, fn) : StreamCodes.StoreOmit;
            q[3] = CommandStream.F(depth.ClearDepth);
            q[4] = hasS && !stencilRo ? Mappings.LoadOp(depth.StencilLoad) : StreamCodes.LoadOmit;
            q[5] = hasS && !stencilRo ? StoreFor(v, depth.StencilStore, fn) : StreamCodes.StoreOmit;
            q[6] = (uint)depth.ClearStencil;
            q[7] = (depthRo ? StreamCodes.DepthReadOnly : 0) | (stencilRo ? StreamCodes.StencilReadOnly : 0);
        }
        p[6] = (uint)width;
        p[7] = (uint)height;
        cb.InRenderPass = true;
        if (WebProfileLint.Enabled && width > 0 && (area.Offset.X != 0 || area.Offset.Y != 0 || area.Extent.Width < width || area.Extent.Height < height))
        {
            var clears = depth.View is not null && depth.DepthLoad == VkAttachmentLoadOp.Clear;
            foreach (ref readonly var c in colors)
                clears |= c.View is not null && c.Load is VkAttachmentLoadOp.Clear or VkAttachmentLoadOp.DontCare;
            if (clears)
                WebProfileLint.Report(fn, "clears cover the whole attachment",
                    $"render area {area.Extent.Width}x{area.Extent.Height}+{area.Offset.X}+{area.Offset.Y} of a {width}x{height} attachment: WebGPU clears the whole attachment, not the render area");
        }
    }

    private static uint StoreFor(ImageViewObject? view, VkAttachmentStoreOp op, string fn)
    {
        if (op != VkAttachmentStoreOp.DontCare || view is null)
            return Mappings.StoreOp(op);
        const VkImageUsageFlags readLater = VkImageUsageFlags.SampledBit | VkImageUsageFlags.TransferSrcBit | VkImageUsageFlags.InputAttachmentBit;
        if ((view.Image.Usage & readLater) != 0 || view.Image.IsSwapchainImage)
        {
            WebProfileLint.Report(fn, "storeOp STORE for attachments read later",
                $"storeOp DONT_CARE on {view.Image}, which can be read later; WebGPU \"discard\" reads back as zero, so the ICD stores it");
            return StreamCodes.StoreStore;
        }
        return StreamCodes.StoreDiscard;
    }

    // ---- dynamic rendering -----------------------------------------------------------------------------------------------

    public static void vkCmdBeginRendering(VkCommandBuffer commandBuffer, VkRenderingInfo* pRenderingInfo)
    {
        const string fn = "vkCmdBeginRendering";
        var cb = Recording(commandBuffer, fn);
        if (cb is null)
            return;
        ref readonly var ri = ref *pRenderingInfo;
        // a suspended pass continues in the next one: WebGPU passes are separate, so resumed passes load and suspended ones store
        var resuming = (ri.Flags & VkRenderingFlags.ResumingBit) != 0;
        var suspending = (ri.Flags & VkRenderingFlags.SuspendingBit) != 0;
        if (ri.LayerCount > 1 || ri.ViewMask != 0)
            IcdLog.Ignored(fn, "layered/multiview rendering");
        var count = Math.Min(ri.ColorAttachmentCount, 8);
        var colors = s_colorScratch.AsSpan(0, count);
        for (var i = 0; i < count; i++)
        {
            ref var a = ref ri.ColorAttachments[i];
            colors[i] = new ColorAttachmentRecord
            {
                View = HandleTable.Lookup<ImageViewObject>(a.ImageView.VkHandle, fn),
                Resolve = a.ResolveMode != VkResolveModeFlags.None ? HandleTable.Lookup<ImageViewObject>(a.ResolveImageView.VkHandle, fn) : null,
                Load = resuming ? VkAttachmentLoadOp.Load : a.LoadOp,
                Store = suspending ? VkAttachmentStoreOp.Store : a.StoreOp,
                Clear = a.ClearValue.Color,
            };
        }
        DepthAttachmentRecord depth = default;
        var d = ri.DepthAttachment;
        var s = ri.StencilAttachment;
        if (d != null && d->ImageView.VkHandle != 0)
        {
            depth.View = HandleTable.Lookup<ImageViewObject>(d->ImageView.VkHandle, fn);
            depth.DepthLoad = resuming ? VkAttachmentLoadOp.Load : d->LoadOp;
            depth.DepthStore = suspending ? VkAttachmentStoreOp.Store : d->StoreOp;
            depth.ClearDepth = d->ClearValue.DepthStencil.Depth;
            depth.ReadOnly |= ReadOnlyFlags(d->ImageLayout) & StreamCodes.DepthReadOnly;
        }
        if (s != null && s->ImageView.VkHandle != 0)
        {
            depth.View ??= HandleTable.Lookup<ImageViewObject>(s->ImageView.VkHandle, fn);
            depth.StencilLoad = resuming ? VkAttachmentLoadOp.Load : s->LoadOp;
            depth.StencilStore = suspending ? VkAttachmentStoreOp.Store : s->StoreOp;
            depth.ClearStencil = s->ClearValue.DepthStencil.Stencil;
            depth.ReadOnly |= ReadOnlyFlags(s->ImageLayout) & StreamCodes.StencilReadOnly;
        }
        else
        {
            depth.StencilLoad = VkAttachmentLoadOp.Load;
            depth.StencilStore = VkAttachmentStoreOp.Store;
        }
        if (d == null || d->ImageView.VkHandle == 0)
        {
            depth.DepthLoad = VkAttachmentLoadOp.Load;
            depth.DepthStore = VkAttachmentStoreOp.Store;
        }
        WriteBeginRenderPass(cb, fn, ri.RenderArea, colors, depth);
    }

    public static void vkCmdEndRendering(VkCommandBuffer commandBuffer)
    {
        var cb = Recording(commandBuffer, "vkCmdEndRendering");
        if (cb is null)
            return;
        cb.Stream.Op0(Op.EndRenderPass);
        cb.InRenderPass = false;
    }

    // ---- legacy render passes --------------------------------------------------------------------------------------------

    public static VkResult vkCreateRenderPass(VkDevice device, VkRenderPassCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator, VkRenderPass* pRenderPass)
    {
        const string fn = "vkCreateRenderPass";
        ref readonly var ci = ref *pCreateInfo;
        if (ci.SubpassCount != 1)
        {
            IcdLog.Unsupported(fn, $"{ci.SubpassCount} subpasses (one subpass per render pass on WebGPU)");
            return VkResult.ErrorFeatureNotPresent;
        }
        var rp = new RenderPassObject { Attachments = new AttachmentDescription[ci.AttachmentCount], SubpassCount = ci.SubpassCount };
        for (var i = 0; i < ci.AttachmentCount; i++)
        {
            ref var a = ref ci.Attachments[i];
            rp.Attachments[i] = new AttachmentDescription
            {
                Format = a.Format, Samples = Mappings.SampleCount(a.Samples), LoadOp = a.LoadOp, StoreOp = a.StoreOp,
                StencilLoadOp = a.StencilLoadOp, StencilStoreOp = a.StencilStoreOp,
            };
        }
        ref var sp = ref ci.Subpasses[0];
        if (sp.InputAttachmentCount > 0)
        {
            IcdLog.Unsupported(fn, "input attachments");
            return VkResult.ErrorFeatureNotPresent;
        }
        rp.ColorRefs = new int[sp.ColorAttachmentCount];
        rp.ResolveRefs = new int[sp.ColorAttachmentCount];
        for (var i = 0; i < sp.ColorAttachmentCount; i++)
        {
            rp.ColorRefs[i] = sp.ColorAttachments[i].Attachment;
            rp.ResolveRefs[i] = sp.ResolveAttachments != null ? sp.ResolveAttachments[i].Attachment : -1;
        }
        rp.DepthRef = sp.DepthStencilAttachment != null ? sp.DepthStencilAttachment->Attachment : -1;
        rp.DepthLayout = sp.DepthStencilAttachment != null ? sp.DepthStencilAttachment->Layout : VkImageLayout.Undefined;
        HandleTable.Add(rp);
        *pRenderPass = new VkRenderPass(rp.Handle);
        return VkResult.Success;
    }

    public static VkResult vkCreateRenderPass2(VkDevice device, VkRenderPassCreateInfo2* pCreateInfo, VkAllocationCallbacks* pAllocator, VkRenderPass* pRenderPass)
    {
        const string fn = "vkCreateRenderPass2";
        ref readonly var ci = ref *pCreateInfo;
        if (ci.SubpassCount != 1)
        {
            IcdLog.Unsupported(fn, $"{ci.SubpassCount} subpasses (one subpass per render pass on WebGPU)");
            return VkResult.ErrorFeatureNotPresent;
        }
        var rp = new RenderPassObject { Attachments = new AttachmentDescription[ci.AttachmentCount], SubpassCount = 1 };
        for (var i = 0; i < ci.AttachmentCount; i++)
        {
            ref var a = ref ci.Attachments[i];
            rp.Attachments[i] = new AttachmentDescription
            {
                Format = a.Format, Samples = Mappings.SampleCount(a.Samples), LoadOp = a.LoadOp, StoreOp = a.StoreOp,
                StencilLoadOp = a.StencilLoadOp, StencilStoreOp = a.StencilStoreOp,
            };
        }
        ref var sp = ref ci.Subpasses[0];
        if (sp.InputAttachmentCount > 0)
        {
            IcdLog.Unsupported(fn, "input attachments");
            return VkResult.ErrorFeatureNotPresent;
        }
        rp.ColorRefs = new int[sp.ColorAttachmentCount];
        rp.ResolveRefs = new int[sp.ColorAttachmentCount];
        for (var i = 0; i < sp.ColorAttachmentCount; i++)
        {
            rp.ColorRefs[i] = sp.ColorAttachments[i].Attachment;
            rp.ResolveRefs[i] = sp.ResolveAttachments != null ? sp.ResolveAttachments[i].Attachment : -1;
        }
        rp.DepthRef = sp.DepthStencilAttachment != null ? sp.DepthStencilAttachment->Attachment : -1;
        rp.DepthLayout = sp.DepthStencilAttachment != null ? sp.DepthStencilAttachment->Layout : VkImageLayout.Undefined;
        HandleTable.Add(rp);
        *pRenderPass = new VkRenderPass(rp.Handle);
        return VkResult.Success;
    }

    public static void vkDestroyRenderPass(VkDevice device, VkRenderPass renderPass, VkAllocationCallbacks* pAllocator)
    {
        if (HandleTable.Get<RenderPassObject>(renderPass.VkHandle) is { } obj)
            HandleTable.Remove(obj.Handle);
    }

    public static void vkGetRenderAreaGranularity(VkDevice device, VkRenderPass renderPass, VkExtent2D* pGranularity) =>
        *pGranularity = new VkExtent2D(1, 1);

    public static VkResult vkCreateFramebuffer(VkDevice device, VkFramebufferCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator,
        VkFramebuffer* pFramebuffer)
    {
        const string fn = "vkCreateFramebuffer";
        var fb = new FramebufferObject { Width = pCreateInfo->Width, Height = pCreateInfo->Height, Views = new ImageViewObject?[pCreateInfo->AttachmentCount] };
        if (pCreateInfo->Attachments != null) // null for imageless framebuffers (views come with vkCmdBeginRenderPass)
            for (var i = 0; i < pCreateInfo->AttachmentCount; i++)
                fb.Views[i] = HandleTable.Lookup<ImageViewObject>(pCreateInfo->Attachments[i].VkHandle, fn);
        HandleTable.Add(fb);
        *pFramebuffer = new VkFramebuffer(fb.Handle);
        return VkResult.Success;
    }

    public static void vkDestroyFramebuffer(VkDevice device, VkFramebuffer framebuffer, VkAllocationCallbacks* pAllocator)
    {
        if (HandleTable.Get<FramebufferObject>(framebuffer.VkHandle) is { } obj)
            HandleTable.Remove(obj.Handle);
    }

    public static void vkCmdBeginRenderPass(VkCommandBuffer commandBuffer, VkRenderPassBeginInfo* pRenderPassBegin, VkSubpassContents contents)
    {
        const string fn = "vkCmdBeginRenderPass";
        var cb = Recording(commandBuffer, fn);
        var rp = HandleTable.Lookup<RenderPassObject>(pRenderPassBegin->RenderPass.VkHandle, fn);
        var fb = HandleTable.Lookup<FramebufferObject>(pRenderPassBegin->Framebuffer.VkHandle, fn);
        if (cb is null || rp is null || fb is null)
            return;
        var imageless = FindNext<VkRenderPassAttachmentBeginInfo>(pRenderPassBegin->Next, VkStructureType.RenderPassAttachmentBeginInfo);
        ImageViewObject? ViewAt(int attachment)
        {
            if (attachment < 0)
                return null;
            if (imageless != null && attachment < imageless->AttachmentCount)
                return HandleTable.Lookup<ImageViewObject>(imageless->Attachments[attachment].VkHandle, fn);
            return attachment < fb.Views.Length ? fb.Views[attachment] : null;
        }
        VkClearValue ClearAt(int attachment) =>
            attachment >= 0 && attachment < pRenderPassBegin->ClearValueCount ? pRenderPassBegin->ClearValues[attachment] : default;

        var colors = s_colorScratch.AsSpan(0, Math.Min(8, rp.ColorRefs.Length));
        for (var i = 0; i < colors.Length; i++)
        {
            var a = rp.ColorRefs[i];
            colors[i] = new ColorAttachmentRecord
            {
                View = ViewAt(a), Resolve = ViewAt(rp.ResolveRefs[i]),
                Load = a >= 0 ? rp.Attachments[a].LoadOp : VkAttachmentLoadOp.Load,
                Store = a >= 0 ? rp.Attachments[a].StoreOp : VkAttachmentStoreOp.Store,
                Clear = ClearAt(a).Color,
            };
        }
        DepthAttachmentRecord depth = default;
        if (rp.DepthRef >= 0)
        {
            ref var a = ref rp.Attachments[rp.DepthRef];
            var clear = ClearAt(rp.DepthRef).DepthStencil;
            depth = new DepthAttachmentRecord
            {
                View = ViewAt(rp.DepthRef), DepthLoad = a.LoadOp, DepthStore = a.StoreOp, StencilLoad = a.StencilLoadOp, StencilStore = a.StencilStoreOp,
                ClearDepth = clear.Depth, ClearStencil = clear.Stencil, ReadOnly = ReadOnlyFlags(rp.DepthLayout),
            };
        }
        WriteBeginRenderPass(cb, fn, pRenderPassBegin->RenderArea, colors, depth);
    }

    public static void vkCmdBeginRenderPass2(VkCommandBuffer commandBuffer, VkRenderPassBeginInfo* pRenderPassBegin, VkSubpassBeginInfo* pSubpassBeginInfo) =>
        vkCmdBeginRenderPass(commandBuffer, pRenderPassBegin, pSubpassBeginInfo != null ? pSubpassBeginInfo->Contents : VkSubpassContents.Inline);

    public static void vkCmdNextSubpass(VkCommandBuffer commandBuffer, VkSubpassContents contents) =>
        IcdLog.Unsupported("vkCmdNextSubpass", "multiple subpasses");

    public static void vkCmdNextSubpass2(VkCommandBuffer commandBuffer, VkSubpassBeginInfo* pSubpassBeginInfo, VkSubpassEndInfo* pSubpassEndInfo) =>
        IcdLog.Unsupported("vkCmdNextSubpass2", "multiple subpasses");

    public static void vkCmdEndRenderPass(VkCommandBuffer commandBuffer) => vkCmdEndRendering(commandBuffer);

    public static void vkCmdEndRenderPass2(VkCommandBuffer commandBuffer, VkSubpassEndInfo* pSubpassEndInfo) => vkCmdEndRendering(commandBuffer);
}
