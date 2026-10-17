using System.Runtime.InteropServices;
using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// <c>VkWebSurfaceCreateInfoBRUTAL</c> of the instance extension <c>VK_BRUTAL_web_surface</c>, consumed by
/// <c>vkCreateWebSurfaceBRUTAL(VkInstance, const VkWebSurfaceCreateInfoBRUTAL*, const VkAllocationCallbacks*, VkSurfaceKHR*)</c>
/// (reached through <c>vkGetInstanceProcAddr</c>). Explicit layout with BRUTAL's 64-bit conventions so it is identical on
/// desktop and wasm32: sType @0, pNext @8, flags @16, canvasHandle @24, size 32.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 32)]
public unsafe struct VkWebSurfaceCreateInfoBRUTAL
{
    /// <summary>The structure type value of this (unregistered, ICD-private) extension struct.</summary>
    public const VkStructureType StructureType = (VkStructureType)1000999000;

    /// <summary>Must be <see cref="StructureType"/>.</summary>
    [FieldOffset(0)] public VkStructureType SType;
    /// <summary>Extension chain; no extension structs are defined for this one, so <c>null</c>.</summary>
    [FieldOffset(8)] public void* Next;
    /// <summary>Reserved, 0.</summary>
    [FieldOffset(16)] public uint Flags;
    /// <summary>The canvas; the PoC supports one, <c>Brutal.Browser.BrowserCanvas.SurfaceHandle</c> = 1.</summary>
    [FieldOffset(24)] public nint CanvasHandle;

    /// <summary>Creates the struct for <paramref name="canvasHandle"/>.</summary>
    public VkWebSurfaceCreateInfoBRUTAL(nint canvasHandle)
    {
        SType = StructureType;
        Next = null;
        Flags = 0;
        CanvasHandle = canvasHandle;
    }
}
