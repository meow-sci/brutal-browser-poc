namespace Brutal.GlfwApi;

/// <summary>
/// Monitor handle. The browser exposes no monitors: only <see cref="Null"/> exists (windows are never fullscreen on a
/// monitor); the other members throw <see cref="NotSupportedException"/>.
/// </summary>
public readonly unsafe partial struct GlfwMonitor : INullable
{
    /// <summary>The null monitor (the only one that exists in the browser).</summary>
    public static readonly GlfwMonitor Null = default;

    /// <summary>Always 0.</summary>
    public nint NativeHandle => 0;

    /// <summary>Always true.</summary>
    public bool IsNull() => true;

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public string? Name => throw Glfw.NotAvailable("GlfwMonitor.Name");

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public void* UserPointer
    {
        get => throw Glfw.NotAvailable("GlfwMonitor.UserPointer");
        set => throw Glfw.NotAvailable("GlfwMonitor.UserPointer");
    }

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public int2 Position => throw Glfw.NotAvailable("GlfwMonitor.Position");

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public int2 PhysicalSize => throw Glfw.NotAvailable("GlfwMonitor.PhysicalSize");

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public float2 ContentScale => throw Glfw.NotAvailable("GlfwMonitor.ContentScale");

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public ReadOnlySpan<GlfwVidMode> VideoModes => throw Glfw.NotAvailable("GlfwMonitor.VideoModes");

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public void SetGamma(float gamma) => throw Glfw.NotAvailable("GlfwMonitor.SetGamma");

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public void GetMonitorWorkArea(out int2 pos, out int2 size) => throw Glfw.NotAvailable("GlfwMonitor.GetMonitorWorkArea");

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public int2 Size => throw Glfw.NotAvailable("GlfwMonitor.Size");

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public GlfwVidMode VideoMode => throw Glfw.NotAvailable("GlfwMonitor.VideoMode");
}
