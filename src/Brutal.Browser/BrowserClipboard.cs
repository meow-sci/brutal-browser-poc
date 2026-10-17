namespace Brutal.Browser;

/// <summary>
/// Synchronous clipboard facade. The browser clipboard is asynchronous and permission gated, so reads return a managed-side
/// cache that is refreshed by the user's paste gestures (Ctrl/Cmd+V is never blocked) and by <see cref="Text"/> writes;
/// writes are also forwarded to <c>navigator.clipboard.writeText</c> (secure context, user activation).
/// </summary>
public static class BrowserClipboard
{
    private static string? s_desktopText;

    /// <summary>Last text pasted by the user or set by the application; <c>null</c> when empty.</summary>
    public static string? Text
    {
        get
        {
            string? text = OperatingSystem.IsBrowser() ? BrowserInterop.GetClipboard() : s_desktopText;
            return string.IsNullOrEmpty(text) ? null : text;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (OperatingSystem.IsBrowser())
                BrowserInterop.SetClipboard(value);
            else
                s_desktopText = value;
        }
    }
}
