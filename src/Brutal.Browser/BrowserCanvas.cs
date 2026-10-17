using Brutal.Numerics;

namespace Brutal.Browser;

/// <summary>
/// The page's canvas: size in CSS and device pixels, pointer lock and cursor control. Sizes are refreshed by
/// <see cref="BrowserInput.PollEvents"/> (the canvas backing store is resized there, never from a DOM callback) and by
/// <see cref="BrowserHost.Initialize"/>. The canvas must be sized by CSS (e.g. <c>width:100%; height:100%</c>).
/// </summary>
public static class BrowserCanvas
{
    private static int2 s_cssSize = new(1, 1);
    private static int2 s_pixelSize = new(1, 1);
    private static float s_dpr = 1f;

    /// <summary>Canvas size in CSS pixels (GLFW screen coordinates).</summary>
    public static int2 CssSize => s_cssSize;

    /// <summary>Canvas backing-store size in device pixels (the swapchain extent; GLFW framebuffer size).</summary>
    public static int2 PixelSize => s_pixelSize;

    /// <summary><c>window.devicePixelRatio</c> (GLFW content scale).</summary>
    public static float DevicePixelRatio => s_dpr;

    /// <summary>Handle the Vulkan layer binds a <c>VkSurfaceKHR</c> to. The PoC supports exactly one canvas: always 1.</summary>
    public static nint SurfaceHandle => 1;

    /// <summary><c>true</c> while the canvas holds the pointer lock (GLFW <c>CURSOR_DISABLED</c>).</summary>
    public static bool IsPointerLocked => BrowserInput.IsPointerLocked;

    /// <summary>Raised from <see cref="BrowserInput.PollEvents"/> after the canvas size or pixel ratio changed.</summary>
    public static event Action? Resized;

    /// <summary>Raised from <see cref="BrowserInput.PollEvents"/> when the pointer lock is acquired (true) or lost (false, e.g. Escape).</summary>
    public static event Action<bool>? PointerLockChanged
    {
        add => BrowserInput.PointerLockChanged += value;
        remove => BrowserInput.PointerLockChanged -= value;
    }

    /// <summary>
    /// Requests pointer lock. Needs a user gesture; if it fails now it is retried on the next click/keydown while still wanted.
    /// Escape releases the lock (the request is then forgotten: ask again).
    /// </summary>
    /// <param name="unadjustedMovement">Ask for raw (unaccelerated) movement; falls back to a plain request if unsupported.</param>
    public static void RequestPointerLock(bool unadjustedMovement = false)
    {
        if (OperatingSystem.IsBrowser())
            BrowserInterop.RequestPointerLock(unadjustedMovement);
    }

    /// <summary>Releases the pointer lock and cancels any pending request.</summary>
    public static void ExitPointerLock()
    {
        if (OperatingSystem.IsBrowser())
            BrowserInterop.ExitPointerLock();
    }

    /// <summary>Shows or hides the CSS cursor over the canvas (GLFW <c>CURSOR_HIDDEN</c>).</summary>
    public static void SetCursorVisible(bool visible)
    {
        if (OperatingSystem.IsBrowser())
            BrowserInterop.SetCursorVisible(visible);
    }

    /// <summary>Sets the CSS <c>cursor</c> of the canvas (empty = default).</summary>
    public static void SetCursorStyle(string css)
    {
        if (OperatingSystem.IsBrowser())
            BrowserInterop.SetCursorStyle(css);
    }

    /// <summary>
    /// Sets the cursor position reported by the next poll. Browsers cannot warp the real pointer, so this only moves the
    /// virtual position (which is what matters while the pointer is locked).
    /// </summary>
    public static void SetCursorPosition(double x, double y)
    {
        if (OperatingSystem.IsBrowser())
            BrowserInterop.SetCursorPos(x, y);
    }

    /// <summary>Gives keyboard focus to the canvas.</summary>
    public static void Focus()
    {
        if (OperatingSystem.IsBrowser())
            BrowserInterop.FocusCanvas();
    }

    /// <summary>Sets an explicit CSS pixel size on the canvas element; the change is reported by the next poll.</summary>
    public static void SetCssSize(int width, int height)
    {
        if (OperatingSystem.IsBrowser())
            BrowserInterop.SetCanvasCssSize(width, height);
    }

    /// <summary>Requests fullscreen for the canvas (needs a user gesture).</summary>
    public static void RequestFullscreen()
    {
        if (OperatingSystem.IsBrowser())
            BrowserInterop.RequestFullscreen();
    }

    /// <summary>Leaves fullscreen.</summary>
    public static void ExitFullscreen()
    {
        if (OperatingSystem.IsBrowser())
            BrowserInterop.ExitFullscreen();
    }

    /// <summary>Reads the current size from JS (used once by <see cref="BrowserHost.Initialize"/>).</summary>
    internal static unsafe void Refresh()
    {
        if (!OperatingSystem.IsBrowser())
            return;

        int* sizes = stackalloc int[4];
        BrowserInterop.GetCanvasSize((nint)sizes);
        Set(new int2(sizes[0], sizes[1]), new int2(sizes[2], sizes[3]), (float)BrowserInterop.GetDevicePixelRatio());
    }

    internal static void Set(int2 css, int2 pixels, float dpr)
    {
        s_cssSize = css;
        s_pixelSize = pixels;
        s_dpr = dpr;
    }

    internal static void RaiseResized() => Resized?.Invoke();
}
