namespace HelloMesh.Rendering;

/// <summary>Process-wide switches that the host sets before constructing the game.</summary>
public static class GraphicsConfig
{
    /// <summary>Enables <c>VK_LAYER_KHRONOS_validation</c> when the loader reports it (desktop <c>--validation</c>).</summary>
    public static bool EnableValidation { get; set; }
}
