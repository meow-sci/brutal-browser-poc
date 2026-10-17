using System.Runtime.InteropServices.JavaScript;

namespace Brutal.Browser;

/// <summary>[JSImport] declarations of <c>wwwroot/brutal-browser.js</c>. Only call these when <see cref="OperatingSystem.IsBrowser"/>.</summary>
internal static partial class BrowserInterop
{
    private const string Module = "brutal-browser";

    [JSImport("now", Module)]
    internal static partial double Now();

    [JSImport("getCanvasSize", Module)]
    internal static partial void GetCanvasSize(nint int4Ptr);

    [JSImport("getDevicePixelRatio", Module)]
    internal static partial double GetDevicePixelRatio();

    [JSImport("pollEvents", Module)]
    internal static partial int PollEvents(nint ptr, int maxBytes);

    [JSImport("isVisible", Module)]
    internal static partial bool IsVisible();

    [JSImport("hasFocus", Module)]
    internal static partial bool HasFocus();

    [JSImport("requestPointerLock", Module)]
    internal static partial void RequestPointerLock(bool unadjustedMovement);

    [JSImport("exitPointerLock", Module)]
    internal static partial void ExitPointerLock();

    [JSImport("isPointerLocked", Module)]
    internal static partial bool IsPointerLocked();

    [JSImport("setCursorVisible", Module)]
    internal static partial void SetCursorVisible(bool visible);

    [JSImport("setCursorStyle", Module)]
    internal static partial void SetCursorStyle(string css);

    [JSImport("setCursorPos", Module)]
    internal static partial void SetCursorPos(double x, double y);

    [JSImport("focusCanvas", Module)]
    internal static partial void FocusCanvas();

    [JSImport("setTitle", Module)]
    internal static partial void SetTitle(string title);

    [JSImport("setCanvasCssSize", Module)]
    internal static partial void SetCanvasCssSize(int width, int height);

    [JSImport("requestFullscreen", Module)]
    internal static partial void RequestFullscreen();

    [JSImport("exitFullscreen", Module)]
    internal static partial void ExitFullscreen();

    [JSImport("getClipboard", Module)]
    internal static partial string GetClipboard();

    [JSImport("setClipboard", Module)]
    internal static partial void SetClipboard(string text);
}
