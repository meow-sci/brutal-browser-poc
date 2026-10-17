using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// Command pools/buffers and vkCmd* recording. Every vkCmd* appends a fixed-size record to the command buffer's unmanaged
/// stream (no JS calls, no managed allocations in steady state); the stream is replayed into a GPUCommandEncoder at submit.
/// Barriers, events and layouts are no-ops on WebGPU (it tracks hazards per usage scope).
/// </summary>
internal static unsafe partial class Icd
{
    // ---- pools and buffers -----------------------------------------------------------------------------------------------

    public static VkResult vkCreateCommandPool(VkDevice device, VkCommandPoolCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator,
        VkCommandPool* pCommandPool)
    {
        var dev = HandleTable.Lookup<DeviceObject>(device.VkHandle, "vkCreateCommandPool");
        if (dev is null)
            return VkResult.ErrorInitializationFailed;
        var pool = new CommandPoolObject { Device = dev };
        HandleTable.Add(pool);
        *pCommandPool = new VkCommandPool(pool.Handle);
        return VkResult.Success;
    }

    public static void vkDestroyCommandPool(VkDevice device, VkCommandPool commandPool, VkAllocationCallbacks* pAllocator)
    {
        var pool = HandleTable.Get<CommandPoolObject>(commandPool.VkHandle);
        if (pool is null)
            return;
        foreach (var cb in pool.CommandBuffers)
        {
            cb.Stream.Dispose();
            HandleTable.Remove(cb.Handle);
        }
        pool.CommandBuffers.Clear();
        HandleTable.Remove(pool.Handle);
    }

    public static VkResult vkResetCommandPool(VkDevice device, VkCommandPool commandPool, VkCommandPoolResetFlags flags)
    {
        if (HandleTable.Lookup<CommandPoolObject>(commandPool.VkHandle, "vkResetCommandPool") is { } pool)
            foreach (var cb in pool.CommandBuffers)
            {
                cb.ResetRecording();
                cb.Recording = false;
            }
        return VkResult.Success;
    }

    public static void vkTrimCommandPool(VkDevice device, VkCommandPool commandPool, VkFlags flags) { }

    private const int CommandBufferCountOffset = 28;

    public static VkResult vkAllocateCommandBuffers(VkDevice device, VkCommandBufferAllocateInfo* pAllocateInfo, VkCommandBuffer* pCommandBuffers)
    {
        var pool = HandleTable.Lookup<CommandPoolObject>(pAllocateInfo->CommandPool.VkHandle, "vkAllocateCommandBuffers");
        if (pool is null)
            return VkResult.ErrorOutOfHostMemory;
        // BRUTAL declares VkCommandBufferAllocateInfo.CommandBufferCount internal (its wrappers set it): read it at its offset
        var count = *(int*)((byte*)pAllocateInfo + CommandBufferCountOffset);
        for (var i = 0; i < count; i++)
        {
            var cb = new CommandBufferObject { Pool = pool, Level = pAllocateInfo->Level };
            HandleTable.Add(cb);
            pool.CommandBuffers.Add(cb);
            pCommandBuffers[i] = new VkCommandBuffer(cb.Handle);
        }
        return VkResult.Success;
    }

    public static void vkFreeCommandBuffers(VkDevice device, VkCommandPool commandPool, int commandBufferCount, VkCommandBuffer* pCommandBuffers)
    {
        for (var i = 0; i < commandBufferCount; i++)
        {
            var cb = HandleTable.Get<CommandBufferObject>(pCommandBuffers[i].VkHandle);
            if (cb is null)
                continue;
            cb.Pool.CommandBuffers.Remove(cb);
            cb.Stream.Dispose();
            HandleTable.Remove(cb.Handle);
        }
    }

    public static VkResult vkBeginCommandBuffer(VkCommandBuffer commandBuffer, VkCommandBufferBeginInfo* pBeginInfo)
    {
        var cb = HandleTable.Lookup<CommandBufferObject>(commandBuffer.VkHandle, "vkBeginCommandBuffer");
        if (cb is null)
            return VkResult.ErrorOutOfHostMemory;
        cb.ResetRecording(); // implicit reset
        cb.Recording = true;
        if (cb.Level == VkCommandBufferLevel.Secondary && (pBeginInfo->Flags & VkCommandBufferUsageFlags.RenderPassContinueBit) != 0)
            cb.InRenderPass = true;
        return VkResult.Success;
    }

    public static VkResult vkEndCommandBuffer(VkCommandBuffer commandBuffer)
    {
        var cb = HandleTable.Lookup<CommandBufferObject>(commandBuffer.VkHandle, "vkEndCommandBuffer");
        if (cb is null)
            return VkResult.ErrorOutOfHostMemory;
        if (cb.InRenderPass && cb.Level == VkCommandBufferLevel.Primary)
            IcdLog.Error($"vkEndCommandBuffer: {cb} ends inside a render pass");
        cb.Recording = false;
        return VkResult.Success;
    }

    public static VkResult vkResetCommandBuffer(VkCommandBuffer commandBuffer, VkCommandBufferResetFlags flags)
    {
        if (HandleTable.Lookup<CommandBufferObject>(commandBuffer.VkHandle, "vkResetCommandBuffer") is { } cb)
        {
            cb.ResetRecording();
            cb.Recording = false;
        }
        return VkResult.Success;
    }

    private static CommandBufferObject? Recording(VkCommandBuffer commandBuffer, string fn) =>
        HandleTable.Lookup<CommandBufferObject>(commandBuffer.VkHandle, fn);

    // ---- state -----------------------------------------------------------------------------------------------------------

    public static void vkCmdBindPipeline(VkCommandBuffer commandBuffer, VkPipelineBindPoint pipelineBindPoint, VkPipeline pipeline)
    {
        const string fn = "vkCmdBindPipeline";
        var cb = Recording(commandBuffer, fn);
        var p = HandleTable.Lookup<PipelineObject>(pipeline.VkHandle, fn);
        if (cb is null || p is null)
            return;
        if (pipelineBindPoint == VkPipelineBindPoint.Compute)
            cb.ComputePipeline = p;
        else
        {
            cb.GraphicsPipeline = p;
            cb.EmittedStripVariant = 0;
        }
        WriteBindPipeline(cb, p, p.Gpu);
        if (p.HasStaticViewport)
            WriteViewport(cb, p.StaticViewport, fn);
        if (p.HasStaticScissor)
            cb.Stream.Op4(Op.SetScissor, (uint)Math.Max(0, p.StaticScissor.Offset.X), (uint)Math.Max(0, p.StaticScissor.Offset.Y),
                (uint)p.StaticScissor.Extent.Width, (uint)p.StaticScissor.Extent.Height);
    }

    private static void WriteBindPipeline(CommandBufferObject cb, PipelineObject p, int gpu) =>
        cb.Stream.Op5(Op.BindPipeline, (uint)gpu, p.BindPoint, p.PushMode, p.FillerMask, p.GroupCount);

    public static void vkCmdBindDescriptorSets(VkCommandBuffer commandBuffer, VkPipelineBindPoint pipelineBindPoint, VkPipelineLayout layout,
        int firstSet, int descriptorSetCount, VkDescriptorSet* pDescriptorSets, int dynamicOffsetCount, int* pDynamicOffsets)
    {
        const string fn = "vkCmdBindDescriptorSets";
        var cb = Recording(commandBuffer, fn);
        if (cb is null)
            return;
        if (firstSet + descriptorSetCount > 4)
        {
            WebProfileLint.Fail(fn, "at most 4 bind groups", $"sets {firstSet}..{firstSet + descriptorSetCount - 1}");
            descriptorSetCount = Math.Max(0, 4 - firstSet);
        }
        // payload: bindPoint, firstSet, count, then per set: bindGroup, dynCount, dyn[dynCount]
        var words = 3 + 2 * descriptorSetCount;
        var totalDynamic = 0;
        for (var i = 0; i < descriptorSetCount; i++)
            if (HandleTable.Get<DescriptorSetObject>(pDescriptorSets[i].VkHandle) is { } s)
                totalDynamic += s.Layout.DynamicCount;
        if (totalDynamic != dynamicOffsetCount && IcdLog.First(fn, "dynamic offset count", ((long)totalDynamic << 32) | (uint)dynamicOffsetCount))
            IcdLog.Error($"{fn}: {dynamicOffsetCount} dynamic offsets for sets with {totalDynamic} dynamic descriptors");
        var p = cb.Stream.Record(Op.BindDescriptorSets, words + totalDynamic);
        p[0] = pipelineBindPoint == VkPipelineBindPoint.Compute ? StreamCodes.BindCompute : StreamCodes.BindGraphics;
        p[1] = (uint)firstSet;
        p[2] = (uint)descriptorSetCount;
        var q = p + 3;
        var d = 0;
        for (var i = 0; i < descriptorSetCount; i++)
        {
            var set = HandleTable.Lookup<DescriptorSetObject>(pDescriptorSets[i].VkHandle, fn);
            q[0] = set is null ? 0 : (uint)EnsureBindGroup(set, fn);
            if (set is not null)
                RequestStorageReadbacks(cb, set);
            var n = set?.Layout.DynamicCount ?? 0;
            q[1] = (uint)n;
            for (var k = 0; k < n; k++)
            {
                var offset = d < dynamicOffsetCount ? (uint)pDynamicOffsets[d] : 0;
                d++;
                WebProfileLint.CheckDynamicOffset(fn, offset);
                q[2 + k] = offset;
            }
            q += 2 + n;
        }
    }

    /// <summary>
    /// Writable storage buffers bound to host-visible memory are written by shaders: read them back after the submit (and keep
    /// the stale shadow from being uploaded over the results meanwhile).
    /// </summary>
    private static void RequestStorageReadbacks(CommandBufferObject cb, DescriptorSetObject set)
    {
        var bindings = set.Layout.Bindings;
        for (var b = 0; b < bindings.Length; b++)
        {
            if (bindings[b].Type is not (VkDescriptorType.StorageBuffer or VkDescriptorType.StorageBufferDynamic))
                continue;
            ref var slot = ref set.Slots[b];
            if (slot.Buffer is not { HostVisible: true } buffer)
                continue;
            if (set.Layout.Effective.TryGetValue(bindings[b].Binding, out var e) && e.Kind == "read-only-storage")
                continue;
            var size = Mappings.IsWholeSize(slot.Range) ? buffer.GpuSize - Math.Min(slot.Offset, buffer.GpuSize) : (slot.Range + 3) & ~3UL;
            RequestReadback(cb, buffer, slot.Offset, Math.Min(size, buffer.GpuSize - Math.Min(slot.Offset, buffer.GpuSize)));
        }
    }

    public static void vkCmdBindVertexBuffers(VkCommandBuffer commandBuffer, int firstBinding, int bindingCount, VkBuffer* pBuffers, ByteSize64* pOffsets) =>
        BindVertexBuffers(commandBuffer, firstBinding, bindingCount, pBuffers, pOffsets, null, null, "vkCmdBindVertexBuffers");

    public static void vkCmdBindVertexBuffers2(VkCommandBuffer commandBuffer, int firstBinding, int bindingCount, VkBuffer* pBuffers, ByteSize64* pOffsets,
        ByteSize64* pSizes, ByteSize64* pStrides) =>
        BindVertexBuffers(commandBuffer, firstBinding, bindingCount, pBuffers, pOffsets, pSizes, pStrides, "vkCmdBindVertexBuffers2");

    private static void BindVertexBuffers(VkCommandBuffer commandBuffer, int firstBinding, int bindingCount, VkBuffer* pBuffers, ByteSize64* pOffsets,
        ByteSize64* pSizes, ByteSize64* pStrides, string fn)
    {
        var cb = Recording(commandBuffer, fn);
        if (cb is null)
            return;
        if (pStrides != null)
            IcdLog.Ignored(fn, "dynamic vertex strides (the pipeline's strides are used)");
        if (firstBinding + bindingCount > Profile.Limits.MaxVertexBuffers)
        {
            WebProfileLint.Fail(fn, $"at most {Profile.Limits.MaxVertexBuffers} vertex buffers", $"bindings {firstBinding}..{firstBinding + bindingCount - 1}");
            return;
        }
        var p = cb.Stream.Record(Op.BindVertexBuffers, 2 + 3 * bindingCount);
        p[0] = (uint)firstBinding;
        p[1] = (uint)bindingCount;
        for (var i = 0; i < bindingCount; i++)
        {
            var b = HandleTable.Lookup<BufferObject>(pBuffers[i].VkHandle, fn);
            var offset = pOffsets[i].Value;
            WebProfileLint.CheckAligned4(fn, "vertex buffer offset", offset);
            var size = pSizes == null || Mappings.IsWholeSize(pSizes[i].Value) ? 0UL : pSizes[i].Value;
            p[2 + 3 * i] = (uint)(b?.Gpu ?? 0);
            p[3 + 3 * i] = (uint)offset;
            p[4 + 3 * i] = (uint)size;
        }
    }

    public static void vkCmdBindIndexBuffer(VkCommandBuffer commandBuffer, VkBuffer buffer, ByteSize64 offset, VkIndexType indexType) =>
        BindIndexBuffer(commandBuffer, buffer, offset.Value, 0, indexType, "vkCmdBindIndexBuffer");

    public static void vkCmdBindIndexBuffer2(VkCommandBuffer commandBuffer, VkBuffer buffer, ByteSize64 offset, ByteSize64 size, VkIndexType indexType) =>
        BindIndexBuffer(commandBuffer, buffer, offset.Value, Mappings.IsWholeSize(size.Value) ? 0 : size.Value, indexType, "vkCmdBindIndexBuffer2");

    private static void BindIndexBuffer(VkCommandBuffer commandBuffer, VkBuffer buffer, ulong offset, ulong size, VkIndexType indexType, string fn)
    {
        var cb = Recording(commandBuffer, fn);
        var b = HandleTable.Lookup<BufferObject>(buffer.VkHandle, fn);
        if (cb is null || b is null)
            return;
        if (indexType is not (VkIndexType.UInt16 or VkIndexType.UInt32))
        {
            IcdLog.Unsupported(fn, "index types other than UINT16 and UINT32");
            return;
        }
        var format = indexType == VkIndexType.UInt32 ? StreamCodes.IndexUint32 : StreamCodes.IndexUint16;
        cb.IndexFormat = format;
        cb.IndexFormatSet = true;
        cb.Stream.Op4(Op.BindIndexBuffer, (uint)b.Gpu, (uint)offset, format, (uint)size);
    }

    public static void vkCmdSetViewport(VkCommandBuffer commandBuffer, int firstViewport, int viewportCount, VkViewport* pViewports)
    {
        const string fn = "vkCmdSetViewport";
        var cb = Recording(commandBuffer, fn);
        if (cb is null || viewportCount == 0)
            return;
        if (firstViewport != 0 || viewportCount > 1)
            IcdLog.Ignored(fn, "viewports other than viewport 0");
        if (firstViewport == 0)
            WriteViewport(cb, pViewports[0], fn);
    }

    public static void vkCmdSetViewportWithCount(VkCommandBuffer commandBuffer, int viewportCount, VkViewport* pViewports) =>
        vkCmdSetViewport(commandBuffer, 0, viewportCount, pViewports);

    private static void WriteViewport(CommandBufferObject cb, in VkViewport v, string fn)
    {
        if (v.Height < 0)
        {
            WebProfileLint.Fail(fn, "positive viewport height", $"height {v.Height} (negative-height viewports are not supported; the viewport is dropped)");
            return;
        }
        if (v.MinDepth > v.MaxDepth)
        {
            WebProfileLint.Fail(fn, "minDepth <= maxDepth", $"depth range {v.MinDepth}..{v.MaxDepth} (reversed ranges are not supported; the viewport is dropped)");
            return;
        }
        var p = cb.Stream.Record(Op.SetViewport, 6);
        p[0] = CommandStream.F(v.X);
        p[1] = CommandStream.F(v.Y);
        p[2] = CommandStream.F(v.Width);
        p[3] = CommandStream.F(v.Height);
        p[4] = CommandStream.F(Math.Clamp(v.MinDepth, 0f, 1f));
        p[5] = CommandStream.F(Math.Clamp(v.MaxDepth, 0f, 1f));
    }

    public static void vkCmdSetScissor(VkCommandBuffer commandBuffer, int firstScissor, int scissorCount, VkRect2D* pScissors)
    {
        const string fn = "vkCmdSetScissor";
        var cb = Recording(commandBuffer, fn);
        if (cb is null || scissorCount == 0 || firstScissor != 0)
            return;
        ref var r = ref pScissors[0];
        cb.Stream.Op4(Op.SetScissor, (uint)Math.Max(0, r.Offset.X), (uint)Math.Max(0, r.Offset.Y), (uint)r.Extent.Width, (uint)r.Extent.Height);
    }

    public static void vkCmdSetScissorWithCount(VkCommandBuffer commandBuffer, int scissorCount, VkRect2D* pScissors) =>
        vkCmdSetScissor(commandBuffer, 0, scissorCount, pScissors);

    public static void vkCmdSetBlendConstants(VkCommandBuffer commandBuffer, float* blendConstants) =>
        Recording(commandBuffer, "vkCmdSetBlendConstants")?.Stream.Op4(Op.SetBlendConstants,
            CommandStream.F(blendConstants[0]), CommandStream.F(blendConstants[1]), CommandStream.F(blendConstants[2]), CommandStream.F(blendConstants[3]));

    public static void vkCmdSetStencilReference(VkCommandBuffer commandBuffer, VkStencilFaceFlags faceMask, int reference) =>
        Recording(commandBuffer, "vkCmdSetStencilReference")?.Stream.Op1(Op.SetStencilReference, (uint)reference);

    public static void vkCmdSetLineWidth(VkCommandBuffer commandBuffer, float lineWidth)
    {
        if (lineWidth != 1f)
            IcdLog.Ignored("vkCmdSetLineWidth", "line widths other than 1.0");
    }

    public static void vkCmdSetDepthBias(VkCommandBuffer commandBuffer, float constantFactor, float clamp, float slopeFactor) =>
        IcdLog.Ignored("vkCmdSetDepthBias", "dynamic depth bias (set it in the pipeline)");

    public static void vkCmdSetDepthBounds(VkCommandBuffer commandBuffer, float minDepthBounds, float maxDepthBounds) =>
        IcdLog.Ignored("vkCmdSetDepthBounds", "depth bounds");

    public static void vkCmdSetStencilCompareMask(VkCommandBuffer commandBuffer, VkStencilFaceFlags faceMask, int compareMask) =>
        IcdLog.Ignored("vkCmdSetStencilCompareMask", "dynamic stencil masks (set them in the pipeline)");

    public static void vkCmdSetStencilWriteMask(VkCommandBuffer commandBuffer, VkStencilFaceFlags faceMask, int writeMask) =>
        IcdLog.Ignored("vkCmdSetStencilWriteMask", "dynamic stencil masks (set them in the pipeline)");

    public static void vkCmdPushConstants(VkCommandBuffer commandBuffer, VkPipelineLayout layout, VkShaderStageFlags stageFlags, int offset, int size,
        void* pValues)
    {
        const string fn = "vkCmdPushConstants";
        var cb = Recording(commandBuffer, fn);
        if (cb is null || size <= 0)
            return;
        if (offset + size > Profile.MaxPushConstantsSize)
        {
            WebProfileLint.Fail(fn, $"push constants are at most {Profile.MaxPushConstantsSize} bytes", $"offset {offset} + size {size}");
            return;
        }
        var p = cb.Stream.RecordWithData(Op.PushConstants, 2, pValues, size);
        p[0] = (uint)offset;
        p[1] = (uint)size;
    }

    // ---- draws and dispatches --------------------------------------------------------------------------------------------

    public static void vkCmdDraw(VkCommandBuffer commandBuffer, int vertexCount, int instanceCount, int firstVertex, int firstInstance) =>
        Recording(commandBuffer, "vkCmdDraw")?.Stream.Op4(Op.Draw, (uint)vertexCount, (uint)instanceCount, (uint)firstVertex, (uint)firstInstance);

    public static void vkCmdDrawIndexed(VkCommandBuffer commandBuffer, int indexCount, int instanceCount, int firstIndex, int vertexOffset, int firstInstance)
    {
        var cb = Recording(commandBuffer, "vkCmdDrawIndexed");
        if (cb is null)
            return;
        EnsureStripVariant(cb);
        cb.Stream.Op5(Op.DrawIndexed, (uint)indexCount, (uint)instanceCount, (uint)firstIndex, (uint)vertexOffset, (uint)firstInstance);
    }

    /// <summary>Strip pipelines need the index format baked in for indexed draws: bind the matching variant.</summary>
    private static void EnsureStripVariant(CommandBufferObject cb)
    {
        var p = cb.GraphicsPipeline;
        if (p is null || !p.IsStrip || !cb.IndexFormatSet)
            return;
        var key = cb.IndexFormat + 1;
        if (cb.EmittedStripVariant == key)
            return;
        WriteBindPipeline(cb, p, StripVariant(p, cb.IndexFormat));
        cb.EmittedStripVariant = key;
    }

    public static void vkCmdDrawIndirect(VkCommandBuffer commandBuffer, VkBuffer buffer, ByteSize64 offset, int drawCount, int stride) =>
        DrawIndirect(commandBuffer, buffer, offset.Value, drawCount, stride, Op.DrawIndirect, "vkCmdDrawIndirect");

    public static void vkCmdDrawIndexedIndirect(VkCommandBuffer commandBuffer, VkBuffer buffer, ByteSize64 offset, int drawCount, int stride) =>
        DrawIndirect(commandBuffer, buffer, offset.Value, drawCount, stride, Op.DrawIndexedIndirect, "vkCmdDrawIndexedIndirect");

    private static void DrawIndirect(VkCommandBuffer commandBuffer, VkBuffer buffer, ulong offset, int drawCount, int stride, Op op, string fn)
    {
        var cb = Recording(commandBuffer, fn);
        var b = HandleTable.Lookup<BufferObject>(buffer.VkHandle, fn);
        if (cb is null || b is null || !WebProfileLint.CheckAligned4(fn, "indirect offset", offset))
            return;
        if (drawCount > 1)
            WebProfileLint.Report(fn, "multi-draw indirect is a CPU loop", $"drawCount {drawCount} becomes {drawCount} WebGPU draws");
        if (op == Op.DrawIndexedIndirect)
            EnsureStripVariant(cb);
        cb.Stream.Op4(op, (uint)b.Gpu, (uint)offset, (uint)drawCount, (uint)stride);
    }

    public static void vkCmdDispatch(VkCommandBuffer commandBuffer, int groupCountX, int groupCountY, int groupCountZ) =>
        Recording(commandBuffer, "vkCmdDispatch")?.Stream.Op3(Op.Dispatch, (uint)groupCountX, (uint)groupCountY, (uint)groupCountZ);

    public static void vkCmdDispatchIndirect(VkCommandBuffer commandBuffer, VkBuffer buffer, ByteSize64 offset)
    {
        const string fn = "vkCmdDispatchIndirect";
        var cb = Recording(commandBuffer, fn);
        var b = HandleTable.Lookup<BufferObject>(buffer.VkHandle, fn);
        if (cb is null || b is null || !WebProfileLint.CheckAligned4(fn, "indirect offset", offset.Value))
            return;
        var p = cb.Stream.Record(Op.DispatchIndirect, 2);
        p[0] = (uint)b.Gpu;
        p[1] = (uint)offset.Value;
    }

    // ---- synchronization (no-ops on WebGPU) ------------------------------------------------------------------------------

    public static void vkCmdPipelineBarrier(VkCommandBuffer commandBuffer, VkPipelineStageFlags srcStageMask, VkPipelineStageFlags dstStageMask,
        VkDependencyFlags dependencyFlags, int memoryBarrierCount, VkMemoryBarrier* pMemoryBarriers, int bufferMemoryBarrierCount,
        VkBufferMemoryBarrier* pBufferMemoryBarriers, int imageMemoryBarrierCount, VkImageMemoryBarrier* pImageMemoryBarriers) =>
        Recording(commandBuffer, "vkCmdPipelineBarrier")?.Stream.Op0(Op.Barrier);

    public static void vkCmdPipelineBarrier2(VkCommandBuffer commandBuffer, VkDependencyInfo* pDependencyInfo) =>
        Recording(commandBuffer, "vkCmdPipelineBarrier2")?.Stream.Op0(Op.Barrier);

    public static void vkCmdSetEvent(VkCommandBuffer commandBuffer, VkEvent @event, VkPipelineStageFlags stageMask) { }

    public static void vkCmdResetEvent(VkCommandBuffer commandBuffer, VkEvent @event, VkPipelineStageFlags stageMask) { }

    public static void vkCmdWaitEvents(VkCommandBuffer commandBuffer, int eventCount, VkEvent* pEvents, VkPipelineStageFlags srcStageMask,
        VkPipelineStageFlags dstStageMask, int memoryBarrierCount, VkMemoryBarrier* pMemoryBarriers, int bufferMemoryBarrierCount,
        VkBufferMemoryBarrier* pBufferMemoryBarriers, int imageMemoryBarrierCount, VkImageMemoryBarrier* pImageMemoryBarriers) =>
        Recording(commandBuffer, "vkCmdWaitEvents")?.Stream.Op0(Op.Barrier);

    public static void vkCmdSetEvent2(VkCommandBuffer commandBuffer, VkEvent @event, VkDependencyInfo* pDependencyInfo) { }

    public static void vkCmdResetEvent2(VkCommandBuffer commandBuffer, VkEvent @event, VkPipelineStageFlags2 stageMask) { }

    public static void vkCmdWaitEvents2(VkCommandBuffer commandBuffer, int eventCount, VkEvent* pEvents, VkDependencyInfo* pDependencyInfos) =>
        Recording(commandBuffer, "vkCmdWaitEvents2")?.Stream.Op0(Op.Barrier);

    // ---- secondary command buffers ---------------------------------------------------------------------------------------

    public static void vkCmdExecuteCommands(VkCommandBuffer commandBuffer, int commandBufferCount, VkCommandBuffer* pCommandBuffers)
    {
        const string fn = "vkCmdExecuteCommands";
        var cb = Recording(commandBuffer, fn);
        if (cb is null)
            return;
        for (var i = 0; i < commandBufferCount; i++)
        {
            var secondary = HandleTable.Lookup<CommandBufferObject>(pCommandBuffers[i].VkHandle, fn);
            if (secondary is null)
                continue;
            cb.Stream.Append(secondary.Stream); // inlined (render bundles would forbid viewport/scissor changes)
            cb.Readbacks.AddRange(secondary.Readbacks);
        }
    }
}
