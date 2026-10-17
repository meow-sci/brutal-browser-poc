namespace Brutal.Browser;

/// <summary>
/// Hook through which the Vulkan-on-WebGPU layer creates a <c>VkSurfaceKHR</c> for the page's canvas. Defined here so that
/// <c>Brutal.Glfw</c> (which implements <c>Glfw.CreateWindowSurface</c>) does not depend on the Vulkan assemblies.
/// </summary>
public static class BrowserSurfaces
{
    /// <summary>Creates a surface; returns a <c>VkResult</c> (0 = <c>VK_SUCCESS</c>).</summary>
    /// <param name="instance">The <c>VkInstance</c> handle.</param>
    /// <param name="canvasHandle">Always <see cref="BrowserCanvas.SurfaceHandle"/> (one canvas is supported).</param>
    /// <param name="surface">The created <c>VkSurfaceKHR</c>.</param>
    public delegate int CreateSurfaceHandler(nint instance, nint canvasHandle, out ulong surface);

    /// <summary>Set by <c>WebIcd.Install()</c>; <c>null</c> until then.</summary>
    public static CreateSurfaceHandler? CreateSurface { get; set; }
}
