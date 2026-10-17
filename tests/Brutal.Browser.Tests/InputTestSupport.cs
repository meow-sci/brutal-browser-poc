using static Brutal.Browser.BrowserInput;

namespace Brutal.Browser.Tests;

/// <summary>Builders for the binary queue records the JS side produces.</summary>
internal static class Rec
{
    public static EventRecord Key(int glfwKey, int action, int mods = 0, int scancode = 0) =>
        new() { Type = (byte)EventType.Key, Action = (byte)action, Mods = (ushort)mods, A = glfwKey, B = scancode };

    public static EventRecord Char(int codepoint, int mods = 0) =>
        new() { Type = (byte)EventType.Char, Mods = (ushort)mods, A = codepoint };

    public static EventRecord Button(int button, int action, int mods = 0) =>
        new() { Type = (byte)EventType.MouseButton, Action = (byte)action, Mods = (ushort)mods, A = button };

    public static EventRecord Cursor(double x, double y) => new() { Type = (byte)EventType.CursorPos, X = x, Y = y };

    public static EventRecord Scroll(double dx, double dy, int flags = 0) =>
        new() { Type = (byte)EventType.Scroll, Action = (byte)flags, X = dx, Y = dy };

    public static EventRecord Focus(bool focused) => new() { Type = (byte)EventType.Focus, Action = (byte)(focused ? 1 : 0) };

    public static EventRecord Size(int cssW, int cssH, int fbW, int fbH, double dpr) =>
        new() { Type = (byte)EventType.Size, A = cssW, B = cssH, C = fbW, D = fbH, X = dpr };

    public static EventRecord PointerLock(bool locked) => new() { Type = (byte)EventType.PointerLock, Action = (byte)(locked ? 1 : 0) };

    public static EventRecord Close() => new() { Type = (byte)EventType.Close };

    public static void Dispatch(params EventRecord[] records) => DispatchForTest(records);
}
