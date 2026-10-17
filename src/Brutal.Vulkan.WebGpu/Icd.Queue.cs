using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// Submission and synchronization (docs/research/webgpu/synchronization-and-async-shape.md). A vkQueueSubmit builds one stream
/// (coherent uploads + the command buffers + readback requests) and makes exactly one <c>replayCommandStream</c> call.
/// Semaphores are inert (one queue, total order). Fences of submits without readback signal at submit; with readback they
/// signal when JS has copied the data into the shadow. Nothing ever blocks: vkWaitForFences returns VK_TIMEOUT instead.
/// After the WebGPU device is lost, submits, acquires, presents and fence waits return VK_ERROR_DEVICE_LOST.
/// </summary>
internal static unsafe partial class Icd
{
    private static bool s_deviceLost;

    /// <summary>
    /// Records that the WebGPU device is gone (the bridge reported it): logs one ERROR, which also reaches the debug messengers, and
    /// returns VK_ERROR_DEVICE_LOST. The page created the GPUDevice, so the ICD cannot recover; the state lasts for the process.
    /// </summary>
    internal static VkResult DeviceLost(string fn)
    {
        if (!s_deviceLost)
        {
            s_deviceLost = true;
            ReportGpuErrors(fn); // the glue's "device lost (<reason>): <message>"
            IcdLog.Error($"{fn}: VK_ERROR_DEVICE_LOST: the WebGPU device was lost; every later submit, acquire and present fails (reload the page)");
        }
        return VkResult.ErrorDeviceLost;
    }

    /// <summary><see cref="IcdTestHooks.Reset"/>: a new bridge has a live device.</summary>
    internal static void ResetDeviceLost() => s_deviceLost = false;

    public static VkResult vkQueueSubmit(VkQueue queue, int submitCount, VkSubmitInfo* pSubmits, VkFence fence)
    {
        const string fn = "vkQueueSubmit";
        if (s_deviceLost)
            return VkResult.ErrorDeviceLost;
        var q = HandleTable.Lookup<QueueObject>(queue.VkHandle, fn);
        if (q is null)
            return VkResult.ErrorDeviceLost;
        var stream = BeginSubmit(q);
        for (var s = 0; s < submitCount; s++)
            for (var i = 0; i < pSubmits[s].CommandBufferCount; i++)
                AppendCommandBuffer(stream, pSubmits[s].CommandBuffers[i], fn);
        return FinishSubmit(stream, fence, fn);
    }

    public static VkResult vkQueueSubmit2(VkQueue queue, int submitCount, VkSubmitInfo2* pSubmits, VkFence fence)
    {
        const string fn = "vkQueueSubmit2";
        if (s_deviceLost)
            return VkResult.ErrorDeviceLost;
        var q = HandleTable.Lookup<QueueObject>(queue.VkHandle, fn);
        if (q is null)
            return VkResult.ErrorDeviceLost;
        var stream = BeginSubmit(q);
        for (var s = 0; s < submitCount; s++)
            for (var i = 0; i < pSubmits[s].CommandBufferInfoCount; i++)
                AppendCommandBuffer(stream, pSubmits[s].CommandBufferInfos[i].CommandBuffer, fn);
        return FinishSubmit(stream, fence, fn);
    }

    private static readonly List<ReadbackRequest> s_submitReadbacks = new();
    private static bool s_errorScopes;
    private static bool s_submitHadCommandBuffers;

    private static CommandStream BeginSubmit(QueueObject q)
    {
        var stream = q.SubmitStream;
        stream.Reset();
        s_submitReadbacks.Clear();
        s_submitHadCommandBuffers = false;
        AppendCoherentUploads(stream);
        return stream;
    }

    private static void AppendCommandBuffer(CommandStream stream, VkCommandBuffer commandBuffer, string fn)
    {
        var cb = HandleTable.Lookup<CommandBufferObject>(commandBuffer.VkHandle, fn);
        if (cb is null)
            return;
        if (cb.Recording)
            IcdLog.Error($"{fn}: {cb} is still recording (vkEndCommandBuffer was not called)");
        stream.Append(cb.Stream);
        s_submitReadbacks.AddRange(cb.Readbacks);
        s_submitHadCommandBuffers = true;
    }

    private static readonly List<ReadbackTicket> s_submitTickets = new();

    private static VkResult FinishSubmit(CommandStream stream, VkFence fence, string fn)
    {
        var f = fence.VkHandle != 0 ? HandleTable.Lookup<FenceObject>(fence.VkHandle, fn) : null;
        Readbacks.PollAll();
        s_submitTickets.Clear();
        foreach (var r in s_submitReadbacks)
        {
            if (r.Buffer.Memory is null)
                continue;
            var ticket = Readbacks.Create(r.Buffer);
            s_submitTickets.Add(ticket);
            var p = stream.Record(Op.Readback, 5);
            p[0] = (uint)r.Buffer.Gpu;
            p[1] = (uint)r.Offset;
            p[2] = (uint)r.Size;
            p[3] = (uint)(nint)r.Buffer.ShadowAddress(r.Offset);
            p[4] = (uint)ticket.Status;
        }

        if (WebIcd.Options.DebugErrorScopes != s_errorScopes)
        {
            s_errorScopes = WebIcd.Options.DebugErrorScopes;
            WebIcd.Bridge.SetDebugMode(s_errorScopes);
        }
        var result = VkResult.Success;
        if (stream.WordLength > 0)
        {
            stream.Op0(Op.End);
            try
            {
                var status = WebIcd.Bridge.ReplayCommandStream(stream.Pointer, stream.ByteLength);
                if ((status & BridgeStatus.ReplayErrorCountMask) > 0)
                    ReportGpuErrors(fn);
                if ((status & BridgeStatus.ReplayOutOfMemory) != 0)
                {
                    IcdLog.Error($"{fn}: VK_ERROR_OUT_OF_DEVICE_MEMORY: the push-constant ring cannot hold this submit's push-constant updates; the submit was dropped (split it into several submits)");
                    result = VkResult.ErrorOutOfDeviceMemory;
                }
                if ((status & BridgeStatus.ReplayDeviceLost) != 0)
                    result = DeviceLost(fn);
            }
            catch (Exception e) when (e is not VulkanUnsupportedException)
            {
                IcdLog.Error($"{fn}: replay failed: {e.GetType().Name}: {e.Message}");
                foreach (var t in s_submitTickets)
                    Readbacks.Retire(t, failed: true, releaseStatus: false);
                result = VkResult.ErrorDeviceLost;
            }
        }

        // The fence of a submit signals at once unless that submit reads back (docs/architecture.md section 5). A submit
        // without command buffers is the "fence after earlier work" idiom: it waits for every readback still in flight.
        if (f is not null)
        {
            f.Tickets.Clear();
            f.Tickets.AddRange(s_submitHadCommandBuffers ? s_submitTickets : Readbacks.Pending);
            f.Signaled = f.Tickets.Count == 0;
        }
        return result;
    }

    /// <summary>Forwards WebGPU errors (uncaptured errors and error scopes) to the log and the debug messengers.</summary>
    internal static void ReportGpuErrors(string fn)
    {
        var text = WebIcd.Bridge.PollErrors();
        if (string.IsNullOrEmpty(text))
            return;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            IcdLog.Error($"WebGPU ({fn}): {line}");
    }

    public static VkResult vkQueueWaitIdle(VkQueue queue) => vkDeviceWaitIdle(default);

    public static VkResult vkQueueBindSparse(VkQueue queue, int bindInfoCount, VkBindSparseInfo* pBindInfo, VkFence fence)
    {
        IcdLog.Unsupported("vkQueueBindSparse", "sparse resources");
        return VkResult.ErrorFeatureNotPresent;
    }

    // ---- fences ----------------------------------------------------------------------------------------------------------

    public static VkResult vkCreateFence(VkDevice device, VkFenceCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator, VkFence* pFence)
    {
        var f = new FenceObject { Signaled = (pCreateInfo->Flags & VkFenceCreateFlags.SignaledBit) != 0 };
        HandleTable.Add(f);
        *pFence = new VkFence(f.Handle);
        return VkResult.Success;
    }

    public static void vkDestroyFence(VkDevice device, VkFence fence, VkAllocationCallbacks* pAllocator)
    {
        // readbacks the fence waited for stay tracked by Readbacks and complete on their own
        if (HandleTable.Get<FenceObject>(fence.VkHandle) is { } f)
            HandleTable.Remove(f.Handle);
    }

    public static VkResult vkResetFences(VkDevice device, int fenceCount, VkFence* pFences)
    {
        for (var i = 0; i < fenceCount; i++)
        {
            var f = HandleTable.Lookup<FenceObject>(pFences[i].VkHandle, "vkResetFences");
            if (f is null)
                continue;
            if (!f.Poll() && IcdLog.First("vkResetFences", "pending", f.Handle))
                IcdLog.Warn($"vkResetFences: {f} is still pending (a readback has not landed yet); it is reset anyway");
            f.Tickets.Clear();
            f.Signaled = false;
        }
        return VkResult.Success;
    }

    public static VkResult vkGetFenceStatus(VkDevice device, VkFence fence)
    {
        var f = HandleTable.Lookup<FenceObject>(fence.VkHandle, "vkGetFenceStatus");
        return f is null ? VkResult.ErrorDeviceLost : f.Poll() ? VkResult.Success : VkResult.NotReady;
    }

    /// <summary>Never blocks (the JS event loop must run for GPU work to complete): VK_SUCCESS or VK_TIMEOUT.</summary>
    public static VkResult vkWaitForFences(VkDevice device, int fenceCount, VkFence* pFences, Bool32 waitAll, nint timeout)
    {
        const string fn = "vkWaitForFences";
        if (s_deviceLost)
            return VkResult.ErrorDeviceLost;
        var signaled = 0;
        for (var i = 0; i < fenceCount; i++)
        {
            var f = HandleTable.Lookup<FenceObject>(pFences[i].VkHandle, fn);
            if (f is null)
                return VkResult.ErrorDeviceLost;
            if (f.Poll())
            {
                signaled++;
                continue;
            }
            if (timeout != 0 && !f.WarnedBlockingWait)
            {
                f.WarnedBlockingWait = true;
                WebProfileLint.Report(fn, "no blocking waits on readback fences",
                    $"{f} guards a GPU→CPU readback that lands on a later frame; the wait returns VK_TIMEOUT instead of blocking (poll across frames)");
            }
        }
        return (waitAll ? signaled == fenceCount : signaled > 0 || fenceCount == 0) ? VkResult.Success : VkResult.Timeout;
    }

    // ---- semaphores and events -----------------------------------------------------------------------------------------

    public static VkResult vkCreateSemaphore(VkDevice device, VkSemaphoreCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator, VkSemaphore* pSemaphore)
    {
        var s = new SemaphoreObject();
        HandleTable.Add(s);
        *pSemaphore = new VkSemaphore(s.Handle);
        return VkResult.Success;
    }

    public static void vkDestroySemaphore(VkDevice device, VkSemaphore semaphore, VkAllocationCallbacks* pAllocator)
    {
        if (HandleTable.Get<SemaphoreObject>(semaphore.VkHandle) is { } s)
            HandleTable.Remove(s.Handle);
    }

    public static VkResult vkCreateEvent(VkDevice device, VkEventCreateInfo* pCreateInfo, VkAllocationCallbacks* pAllocator, VkEvent* pEvent)
    {
        var e = new EventObject();
        HandleTable.Add(e);
        *pEvent = new VkEvent(e.Handle);
        return VkResult.Success;
    }

    public static void vkDestroyEvent(VkDevice device, VkEvent @event, VkAllocationCallbacks* pAllocator)
    {
        if (HandleTable.Get<EventObject>(@event.VkHandle) is { } e)
            HandleTable.Remove(e.Handle);
    }

    public static VkResult vkGetEventStatus(VkDevice device, VkEvent @event) =>
        HandleTable.Lookup<EventObject>(@event.VkHandle, "vkGetEventStatus") is { Signaled: true } ? VkResult.EventSet : VkResult.EventReset;

    public static VkResult vkSetEvent(VkDevice device, VkEvent @event)
    {
        if (HandleTable.Lookup<EventObject>(@event.VkHandle, "vkSetEvent") is { } e)
            e.Signaled = true;
        return VkResult.Success;
    }

    public static VkResult vkResetEvent(VkDevice device, VkEvent @event)
    {
        if (HandleTable.Lookup<EventObject>(@event.VkHandle, "vkResetEvent") is { } e)
            e.Signaled = false;
        return VkResult.Success;
    }
}
