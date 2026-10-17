using Brutal.GlfwApi;
using Brutal.Numerics;

namespace Brutal.Browser.Tests;

[Collection(BrowserCollection.Name)]
public sealed class GlfwApiTests : IDisposable
{
    private readonly GlfwWindow _window;
    private readonly List<string> _log = [];

    public GlfwApiTests()
    {
        BrowserInput.ResetForTest();
        BrowserCanvas.Set(new int2(800, 600), new int2(800, 600), 1f);
        BrowserSurfaces.CreateSurface = null;
        Assert.True(Glfw.Init());
        Glfw.WindowHint(GlfwWindowHint.ClientApi, 0);
        _window = Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title = "test", Size = new int2(1280, 720) });

        _window.OnKey += (w, key, scancode, action, mods) => _log.Add($"key {key} {scancode} {action} {mods}");
        _window.OnChar += (w, cp) => _log.Add($"char {cp}");
        _window.OnCharMods += (w, cp, mods) => _log.Add($"charmods {cp} {mods}");
        _window.OnMouseButton += (w, b, a, m) => _log.Add($"button {(int)b} {a} {m}");
        _window.OnCursorPos += (w, p) => _log.Add($"cursor {p.X} {p.Y}");
        _window.OnScroll += (w, o) => _log.Add($"scroll {o.X} {o.Y}");
        _window.OnScrollDetail += (w, o, f) => _log.Add($"scrolldetail {o.X} {o.Y} {f}");
        _window.OnFocus += (w, f) => _log.Add($"focus {f}");
        _window.OnSize += (w, s) => _log.Add($"size {s.X}x{s.Y}");
        _window.OnFramebufferSize += (w, s) => _log.Add($"fb {s.X}x{s.Y}");
        _window.OnContentScale += (w, s) => _log.Add($"scale {s.X}");
        _window.OnClose += w => _log.Add("close");
    }

    public void Dispose()
    {
        Glfw.Terminate();
        BrowserSurfaces.CreateSurface = null;
        BrowserInput.ResetForTest();
    }

    [Fact]
    public void Window_reflects_the_canvas()
    {
        Assert.Equal(new int2(800, 600), _window.Size);
        Assert.Equal(new int2(800, 600), _window.FramebufferSize);
        Assert.Equal(new float2(1, 1), _window.ContentScale);
        Assert.Equal("test", _window.Title);
        Assert.Equal(1, (int)_window.NativeHandle);
        Assert.False(_window.ShouldClose);
        Assert.True(_window.IsWindowed);
        Assert.True(_window.Visible);
    }

    [Fact]
    public void Key_events_use_glfw_enums_and_polled_state()
    {
        Rec.Dispatch(Rec.Key(263, 1, mods: 2, scancode: 0xE04B));

        Assert.Equal([$"key {GlfwKey.Left} {0xE04B} {GlfwKeyAction.Press} {GlfwModifier.Control}"], _log);
        Assert.Equal(GlfwKeyAction.Press, _window.GetKey(GlfwKey.Left));
        Assert.Equal(GlfwKeyAction.Release, _window.GetKey(GlfwKey.Right));

        Rec.Dispatch(Rec.Key(263, 0));
        Assert.Equal(GlfwKeyAction.Release, _window.GetKey(GlfwKey.Left));
    }

    [Fact]
    public void Mouse_and_cursor_events()
    {
        Rec.Dispatch(Rec.Cursor(10.5, 20), Rec.Button((int)GlfwMouseButton.Right, 1));

        Assert.Equal(["cursor 10.5 20", "button 1 Press 0"], _log);
        Assert.Equal(new double2(10.5, 20), _window.CursorPos);
        Assert.Equal(10.5, _window.CursorX);
        Assert.Equal(GlfwButtonAction.Press, _window.GetMouseButton(GlfwMouseButton.Right));
    }

    [Fact]
    public void Scroll_raises_OnScroll_and_OnScrollDetail()
    {
        Rec.Dispatch(Rec.Scroll(0, -1, flags: (int)GlfwScrollFlags.Precise));
        Assert.Equal(["scroll 0 -1", "scrolldetail 0 -1 Precise"], _log);
    }

    [Fact]
    public void Chars_follow_glfw_filtering_and_order()
    {
        Rec.Dispatch(Rec.Char(97), Rec.Char(10), Rec.Char(127), Rec.Char(0x20AC));
        Assert.Equal(["charmods 97 0", "char 97", $"charmods {0x20AC} 0", $"char {0x20AC}"], _log);
    }

    [Fact]
    public void Resize_updates_the_cache_before_raising_size_framebuffer_and_scale()
    {
        GlfwWindow? seen = null;
        int2 fbAtSize = default;
        _window.OnSize += (w, s) => { seen = w; fbAtSize = w.FramebufferSize; };

        Rec.Dispatch(Rec.Size(700, 500, 1400, 1000, 2.0));

        Assert.Same(_window, seen);
        Assert.Equal(new int2(1400, 1000), fbAtSize); // consistent cache inside the OnSize handler
        Assert.Equal(["size 700x500", "fb 1400x1000", "scale 2"], _log);
        Assert.Equal(new int2(700, 500), _window.Size);
        Assert.Equal(new float2(2, 2), _window.ContentScale);
    }

    [Fact]
    public void Focus_loss_releases_keys_through_OnKey()
    {
        Rec.Dispatch(Rec.Key(65, 1));
        _log.Clear();

        Rec.Dispatch(Rec.Focus(false));

        Assert.Equal([$"key {GlfwKey.A} 0 {GlfwKeyAction.Release} 0", $"focus {GlfwFocusEvent.LostFocus}"], _log);
        Assert.False(_window.IsFocused);
        Assert.Equal(GlfwKeyAction.Release, _window.GetKey(GlfwKey.A));
    }

    [Fact]
    public void Close_sets_ShouldClose()
    {
        Rec.Dispatch(Rec.Close());
        Assert.Equal(["close"], _log);
        Assert.True(_window.ShouldClose);
    }

    [Fact]
    public void Handler_exceptions_are_rethrown_from_PollEvents()
    {
        _window.OnKey += (w, key, scancode, action, mods) => throw new InvalidOperationException("handler failed");

        Rec.Dispatch(Rec.Key(65, 1)); // swallowed into the aggregator, like BRUTAL's callbacks
        var ex = Assert.Throws<Exception>(() => Glfw.PollEvents());

        Assert.Contains("Window.OnKey", ex.Message);
        Assert.Equal("handler failed", ex.InnerException?.Message);
        Glfw.PollEvents(); // the aggregator was cleared
    }

    [Fact]
    public void Cursor_input_mode_tracks_the_mode_and_pointer_lock_loss_returns_to_normal()
    {
        Assert.Equal((int)GlfwCursorMode.Normal, _window.GetInputMode(GlfwInputMode.Cursor));

        _window.SetInputMode(GlfwInputMode.Cursor, (int)GlfwCursorMode.Disabled);
        Assert.Equal((int)GlfwCursorMode.Disabled, _window.GetInputMode(GlfwInputMode.Cursor));

        Rec.Dispatch(Rec.PointerLock(true), Rec.PointerLock(false)); // Escape released the lock
        Assert.Equal((int)GlfwCursorMode.Normal, _window.GetInputMode(GlfwInputMode.Cursor));

        _window.SetInputMode(GlfwInputMode.Cursor, (int)GlfwCursorMode.Hidden);
        Assert.Equal((int)GlfwCursorMode.Hidden, _window.GetInputMode(GlfwInputMode.Cursor));

        _window.SetInputMode(GlfwInputMode.RawMouseMotion, 1);
        Assert.Equal(1, _window.GetInputMode(GlfwInputMode.RawMouseMotion));
    }

    [Fact]
    public void Unsupported_input_modes_throw()
    {
        var ex = Assert.Throws<NotSupportedException>(() => _window.SetInputMode(GlfwInputMode.StickyKeys, 1));
        Assert.EndsWith("is not available in the browser", ex.Message);
        Assert.Throws<NotSupportedException>(() => _window.SetInputMode(GlfwInputMode.Cursor, (int)GlfwCursorMode.Captured));
    }

    [Fact]
    public void Unsupported_members_throw_NotSupportedException_naming_the_member()
    {
        var ex = Assert.Throws<NotSupportedException>(() => _window.Maximize());
        Assert.Equal("Maximize is not available in the browser", ex.Message);
        Assert.Throws<NotSupportedException>(() => _window.SetIcon(0, default));
        Assert.Throws<NotSupportedException>(() => _window.SetFullscreen(GlfwMonitor.Null, new int2(1, 1), 0));
        Assert.Throws<NotSupportedException>(() => _ = Glfw.PrimaryMonitor);
        Assert.Throws<NotSupportedException>(() => Glfw.GetMonitors());
        Assert.Throws<NotSupportedException>(() => Glfw.WaitEvents());
        Assert.Throws<NotSupportedException>(() => _window.Position = new int2(5, 5));
        _window.Position = default; // no-op set is fine
    }

    [Fact]
    public void Only_one_window_exists()
    {
        var ex = Assert.Throws<NotSupportedException>(() => Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title = "b", Size = new int2(1, 1) }));
        Assert.EndsWith("is not available in the browser", ex.Message);

        _window.Dispose();
        using var second = Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title = "c", Size = new int2(1, 1) });
        Assert.Equal("c", second.Title);
    }

    [Fact]
    public void CreateWindow_requires_Init()
    {
        Glfw.Terminate();
        Assert.Throws<InvalidOperationException>(() => Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title = "x", Size = new int2(1, 1) }));
        Assert.True(Glfw.Init());
    }

    [Fact]
    public unsafe void Required_instance_extensions_are_the_web_surface_pair()
    {
        byte** names = Glfw.GetRequiredInstanceExtensions(out uint count);
        Assert.Equal(2u, count);
        Assert.Equal("VK_KHR_surface", System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)names[0]));
        Assert.Equal("VK_BRUTAL_web_surface", System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)names[1]));
        Assert.True(Glfw.VulkanSupported());
    }

    [Fact]
    public void CreateWindowSurface_uses_the_hook_and_requires_it()
    {
        Assert.Throws<InvalidOperationException>(() => Glfw.CreateWindowSurface(0x1234, _window, out _));

        nint seenInstance = 0, seenCanvas = 0;
        BrowserSurfaces.CreateSurface = (nint instance, nint canvas, out ulong surface) =>
        {
            seenInstance = instance;
            seenCanvas = canvas;
            surface = 0xABCD;
            return 0;
        };

        int result = Glfw.CreateWindowSurface(0x1234, _window, out ulong created);

        Assert.Equal(0, result);
        Assert.Equal(0xABCDUL, created);
        Assert.Equal((nint)0x1234, seenInstance);
        Assert.Equal(BrowserCanvas.SurfaceHandle, seenCanvas);
    }

    [Fact]
    public void Time_counts_up_and_can_be_set()
    {
        double t0 = Glfw.Time;
        Thread.Sleep(20);
        Assert.True(Glfw.Time > t0);

        Glfw.Time = 100;
        Assert.InRange(Glfw.Time, 100, 101);
        Assert.Equal(1_000_000UL, Glfw.GetTimerFrequency());
    }

    [Fact]
    public unsafe void Gamepads_report_none_connected()
    {
        Assert.False(Glfw.JoystickPresent(0));
        Assert.False(Glfw.GetGamepadState(0, out var state));
        Assert.Equal(0, state.Buttons[0]);
        Assert.True(Glfw.GetJoystickAxes(0).IsEmpty);
    }

    [Fact]
    public void Clipboard_roundtrips()
    {
        Glfw.Clipboard = "hello";
        Assert.Equal("hello", Glfw.Clipboard);
        Glfw.SetClipboard("again");
        Assert.Equal("again", Glfw.Clipboard);
    }

    [Fact]
    public void Standard_cursors_can_be_created_and_custom_ones_throw()
    {
        using var cursor = Glfw.CreateCursor(GlfwStandardCursor.Crosshair);
        Assert.True(cursor.IsNotNull());
        _window.SetCursor(cursor);
        Assert.Throws<NotSupportedException>(() => Glfw.CreateCursor(new GlfwCursor.CreateInfo()));
    }
}
