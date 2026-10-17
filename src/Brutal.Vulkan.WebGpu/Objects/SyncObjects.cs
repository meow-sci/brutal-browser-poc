using System.Runtime.InteropServices;

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// A VkFence. Submits signal it at once unless GPU→CPU readbacks are in flight: then it holds every readback submitted up to
/// and including its submit (a fence covers all earlier work in submission order) and signals when all of them have landed.
/// </summary>
internal sealed class FenceObject : IcdObject
{
    public override string VkTypeName => "VkFence";
    public bool Signaled;
    public readonly List<ReadbackTicket> Tickets = new();
    public bool WarnedBlockingWait;

    /// <summary>Polls the readbacks (status words written by JS; no JS call); signals when all completed.</summary>
    public bool Poll()
    {
        if (Signaled)
            return true;
        if (Tickets.Count == 0)
            return false; // not submitted (or reset) and nothing in flight
        foreach (var t in Tickets)
            if (!t.Poll())
                return false;
        foreach (var t in Tickets)
            if (t.Failed)
                IcdLog.ErrorOnce("readback-failed|" + Handle, $"vkGetFenceStatus: a readback guarded by {this} failed in WebGPU; the host memory was not updated");
        Tickets.Clear();
        Signaled = true;
        return true;
    }
}

internal sealed class SemaphoreObject : IcdObject
{
    public override string VkTypeName => "VkSemaphore";
}

internal sealed class EventObject : IcdObject
{
    public override string VkTypeName => "VkEvent";
    public bool Signaled;
}

/// <summary>
/// One GPU→CPU copy into a host-visible buffer's shadow. JS sets the 4-byte status word in the wasm heap to 1 (or -1) when the
/// mapAsync copy completed, so polling needs no JS call. While a ticket is pending its buffer is excluded from coherent uploads
/// and its memory's shadow is kept alive even if the app frees the memory.
/// </summary>
internal sealed unsafe class ReadbackTicket
{
    public nint Status;
    public BufferObject Buffer = null!;
    public DeviceMemoryObject Memory = null!;
    public bool Completed, Failed;

    public bool Poll()
    {
        if (Completed)
            return true;
        var status = *(int*)Status;
        if (status == 0)
            return false;
        Readbacks.Retire(this, status < 0, releaseStatus: true);
        return true;
    }
}

/// <summary>All readbacks in flight (submission order).</summary>
internal static unsafe class Readbacks
{
    private static readonly List<ReadbackTicket> s_pending = new();
    private static readonly Stack<nint> s_freeStatus = new();

    public static List<ReadbackTicket> Pending => s_pending;

    public static ReadbackTicket Create(BufferObject buffer)
    {
        var status = s_freeStatus.Count > 0 ? s_freeStatus.Pop() : (nint)NativeMemory.Alloc(4);
        *(int*)status = 0;
        var t = new ReadbackTicket { Status = status, Buffer = buffer, Memory = buffer.Memory! };
        buffer.PendingReadbacks++;
        t.Memory.PendingReadbacks++;
        s_pending.Add(t);
        return t;
    }

    /// <summary>Forgets every readback in flight (they are marked failed; status words are not reused). <see cref="IcdTestHooks.Reset"/>.</summary>
    internal static void ResetForTests()
    {
        for (var i = s_pending.Count - 1; i >= 0; i--)
            Retire(s_pending[i], failed: true, releaseStatus: false);
    }

    public static void PollAll()
    {
        for (var i = s_pending.Count - 1; i >= 0; i--)
            if (i < s_pending.Count)
                s_pending[i].Poll();
    }

    /// <summary>
    /// Completes a ticket. <paramref name="releaseStatus"/> is false when JS may still write the status word later (failed
    /// replay call): the 4 bytes are then leaked rather than reused.
    /// </summary>
    public static void Retire(ReadbackTicket t, bool failed, bool releaseStatus)
    {
        if (t.Completed)
            return;
        t.Completed = true;
        t.Failed = failed;
        s_pending.Remove(t);
        if (releaseStatus)
            s_freeStatus.Push(t.Status);
        t.Buffer.PendingReadbacks--;
        if (--t.Memory.PendingReadbacks == 0 && t.Memory.Freed)
            t.Memory.FreeShadow();
    }
}
