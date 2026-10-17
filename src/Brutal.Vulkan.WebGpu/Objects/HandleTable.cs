using System.Runtime.CompilerServices;

namespace Brutal.Vulkan.WebGpu;

/// <summary>Base class of every object behind a Vulkan handle the ICD hands out.</summary>
internal abstract class IcdObject
{
    /// <summary>The Vulkan handle (an index + generation into <see cref="HandleTable"/>).</summary>
    public nint Handle;

    /// <summary>Name from <c>vkSetDebugUtilsObjectNameEXT</c>, if any.</summary>
    public string? Name;

    /// <summary>The WebGPU object id in the JS handle table, or 0.</summary>
    public virtual int GpuHandle => 0;

    /// <summary>Vulkan type name used in diagnostics, e.g. <c>VkBuffer</c>.</summary>
    public abstract string VkTypeName { get; }

    public override string ToString() => Name is null ? $"{VkTypeName} 0x{Handle:x}" : $"{VkTypeName} 0x{Handle:x} \"{Name}\"";
}

/// <summary>
/// One table for all ICD objects. A handle packs a 20-bit slot index and an 11-bit generation into a positive 32-bit value
/// (wasm32 <c>nint</c>), so handles are never 0 and stale handles (use after destroy) are detected on lookup.
/// </summary>
internal static class HandleTable
{
    private const int IndexBits = 20;
    private const int IndexMask = (1 << IndexBits) - 1;
    private const int GenerationMask = 0x7FF;

    private static IcdObject?[] s_objects = new IcdObject?[1024];
    private static ushort[] s_generations = new ushort[1024];
    private static int[] s_free = new int[256];
    private static int s_freeCount;
    private static int s_next = 1; // slot 0 is never used: handle 0 is VK_NULL_HANDLE

    /// <summary>Number of live objects (tests, leak checks).</summary>
    public static int LiveCount { get; private set; }

    public static nint Add(IcdObject obj)
    {
        int index;
        if (s_freeCount > 0)
            index = s_free[--s_freeCount];
        else
        {
            index = s_next++;
            if (index > IndexMask)
                throw new InvalidOperationException("web ICD handle table exhausted (more than 1M live objects)");
            if (index >= s_objects.Length)
            {
                Array.Resize(ref s_objects, s_objects.Length * 2);
                Array.Resize(ref s_generations, s_generations.Length * 2);
            }
        }

        var generation = (ushort)((s_generations[index] + 1) & GenerationMask);
        if (generation == 0)
            generation = 1;
        s_generations[index] = generation;
        s_objects[index] = obj;
        var handle = (nint)((generation << IndexBits) | index);
        obj.Handle = handle;
        LiveCount++;
        return handle;
    }

    /// <summary>The object behind <paramref name="handle"/> if it is live and of type <typeparamref name="T"/>; null otherwise.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? Get<T>(nint handle) where T : IcdObject
    {
        var index = (int)handle & IndexMask;
        if (handle == 0 || index >= s_next)
            return null;
        if (s_generations[index] != (((int)handle >> IndexBits) & GenerationMask))
            return null;
        return s_objects[index] as T;
    }

    /// <summary>Like <see cref="Get{T}"/> but logs an error naming <paramref name="function"/> for a non-null invalid handle.</summary>
    public static T? Lookup<T>(nint handle, string function) where T : IcdObject
    {
        var obj = Get<T>(handle);
        if (obj is null && handle != 0)
            IcdLog.Error($"{function}: invalid or destroyed {typeof(T).Name.Replace("Object", "")} handle 0x{handle:x} (use after destroy?)");
        return obj;
    }

    /// <summary>All live objects (diagnostics and tests; allocates).</summary>
    internal static IEnumerable<IcdObject> LiveObjects()
    {
        for (var i = 1; i < s_next; i++)
            if (s_objects[i] is { } o)
                yield return o;
    }

    public static void Remove(nint handle)
    {
        var index = (int)handle & IndexMask;
        if (handle == 0 || index >= s_next || s_objects[index] is null)
            return;
        if (s_generations[index] != (((int)handle >> IndexBits) & GenerationMask))
            return;
        s_objects[index] = null;
        if (s_freeCount == s_free.Length)
            Array.Resize(ref s_free, s_free.Length * 2);
        s_free[s_freeCount++] = index;
        LiveCount--;
    }
}
