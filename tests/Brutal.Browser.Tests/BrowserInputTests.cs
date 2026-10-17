using Brutal.Numerics;

namespace Brutal.Browser.Tests;

[Collection(BrowserCollection.Name)]
public sealed class BrowserInputTests : IDisposable
{
    private const int KeyA = 65, KeyLeft = 263, KeyLeftShift = 340;

    private readonly List<string> _log = [];

    public BrowserInputTests()
    {
        BrowserInput.ResetForTest();
        BrowserInput.Key += OnKey;
        BrowserInput.CharInput += OnChar;
        BrowserInput.MouseButton += OnButton;
        BrowserInput.CursorPos += OnCursor;
        BrowserInput.Scroll += OnScroll;
        BrowserInput.Focus += OnFocus;
        BrowserInput.PointerLockChanged += OnLock;
    }

    public void Dispose()
    {
        BrowserInput.Key -= OnKey;
        BrowserInput.CharInput -= OnChar;
        BrowserInput.MouseButton -= OnButton;
        BrowserInput.CursorPos -= OnCursor;
        BrowserInput.Scroll -= OnScroll;
        BrowserInput.Focus -= OnFocus;
        BrowserInput.PointerLockChanged -= OnLock;
        BrowserInput.ResetForTest();
    }

    private void OnKey(int key, int scancode, int action, int mods) => _log.Add($"key {key} {scancode} {action} {mods}");
    private void OnChar(uint cp, int mods) => _log.Add($"char {cp} {mods}");
    private void OnButton(int button, int action, int mods) => _log.Add($"button {button} {action} {mods}");
    private void OnCursor(double x, double y) => _log.Add($"cursor {x} {y}");
    private void OnScroll(double dx, double dy, int flags) => _log.Add($"scroll {dx} {dy} {flags}");
    private void OnFocus(bool focused) => _log.Add($"focus {focused}");
    private void OnLock(bool locked) => _log.Add($"lock {locked}");

    [Fact]
    public void Key_press_repeat_release_update_polled_state_and_raise_in_order()
    {
        Rec.Dispatch(Rec.Key(KeyLeft, 1, scancode: 0xE04B), Rec.Key(KeyLeft, 2), Rec.Key(KeyLeft, 0));

        Assert.Equal(["key 263 57419 1 0", "key 263 0 2 0", "key 263 0 0 0"], _log);
        Assert.False(BrowserInput.GetKey(KeyLeft));
    }

    [Fact]
    public void Polled_state_is_visible_to_listeners_and_after_dispatch()
    {
        bool downInsideListener = false;
        void Probe(int key, int scancode, int action, int mods) => downInsideListener = BrowserInput.GetKey(key);
        BrowserInput.Key += Probe;
        try
        {
            Rec.Dispatch(Rec.Key(KeyA, 1));
        }
        finally
        {
            BrowserInput.Key -= Probe;
        }

        Assert.True(downInsideListener); // state is updated before the event, like glfw
        Assert.True(BrowserInput.GetKey(KeyA));
        Assert.False(BrowserInput.GetKey(KeyLeft));
    }

    [Fact]
    public void Second_press_of_a_down_key_becomes_a_repeat()
    {
        Rec.Dispatch(Rec.Key(KeyA, 1), Rec.Key(KeyA, 1));
        Assert.Equal(["key 65 0 1 0", "key 65 0 2 0"], _log);
    }

    [Fact]
    public void Release_of_a_key_that_is_not_down_is_ignored()
    {
        Rec.Dispatch(Rec.Key(KeyA, 0));
        Assert.Empty(_log);
    }

    [Fact]
    public void Unknown_keys_are_forwarded_but_not_tracked()
    {
        Rec.Dispatch(Rec.Key(-1, 1, scancode: 99));
        Assert.Equal(["key -1 99 1 0"], _log);
        Assert.False(BrowserInput.GetKey(-1));
    }

    [Fact]
    public void Mouse_buttons_track_state_and_ignore_duplicates()
    {
        Rec.Dispatch(Rec.Button(0, 1), Rec.Button(0, 1), Rec.Button(0, 0), Rec.Button(0, 0));
        Assert.Equal(["button 0 1 0", "button 0 0 0"], _log);
        Assert.False(BrowserInput.GetMouseButton(0));
    }

    [Fact]
    public void Losing_focus_releases_every_key_and_button_before_the_focus_event()
    {
        Rec.Dispatch(Rec.Key(KeyA, 1), Rec.Key(KeyLeftShift, 1), Rec.Button(1, 1));
        _log.Clear();

        Rec.Dispatch(Rec.Focus(false));

        Assert.Equal(["key 65 0 0 0", "key 340 0 0 0", "button 1 0 0", "focus False"], _log);
        Assert.False(BrowserInput.GetKey(KeyA));
        Assert.False(BrowserInput.GetKey(KeyLeftShift));
        Assert.False(BrowserInput.GetMouseButton(1));
        Assert.False(BrowserInput.IsFocused);

        Rec.Dispatch(Rec.Focus(true));
        Assert.True(BrowserInput.IsFocused);
    }

    [Fact]
    public void Cursor_scroll_char_and_lock_events_are_forwarded()
    {
        Rec.Dispatch(Rec.Cursor(12.5, 40), Rec.Scroll(0, -1, flags: 1), Rec.Char(97, 1), Rec.PointerLock(true), Rec.PointerLock(false));

        Assert.Equal(["cursor 12.5 40", "scroll 0 -1 1", "char 97 1", "lock True", "lock False"], _log);
        Assert.Equal(new double2(12.5, 40), BrowserInput.CursorPosition);
        Assert.False(BrowserInput.IsPointerLocked);
    }

    [Fact]
    public void Size_record_updates_the_canvas_and_raises_Resized()
    {
        int raised = 0;
        void OnResized() => raised++;
        BrowserCanvas.Resized += OnResized;
        try
        {
            Rec.Dispatch(Rec.Size(640, 480, 1280, 960, 2.0));
        }
        finally
        {
            BrowserCanvas.Resized -= OnResized;
        }

        Assert.Equal(1, raised);
        Assert.Equal(new int2(640, 480), BrowserCanvas.CssSize);
        Assert.Equal(new int2(1280, 960), BrowserCanvas.PixelSize);
        Assert.Equal(2f, BrowserCanvas.DevicePixelRatio);
        Assert.Equal(1, (int)BrowserCanvas.SurfaceHandle);
    }

    [Fact]
    public void A_throwing_listener_does_not_lose_later_events_and_is_rethrown_after_the_batch()
    {
        void Thrower(int key, int scancode, int action, int mods) => throw new InvalidOperationException("boom");
        BrowserInput.Key += Thrower;
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Rec.Dispatch(Rec.Key(KeyA, 1), Rec.Cursor(1, 2)));
            Assert.Equal("boom", ex.Message);
        }
        finally
        {
            BrowserInput.Key -= Thrower;
        }

        Assert.Contains("cursor 1 2", _log); // events after the failing one were still dispatched
        Assert.True(BrowserInput.GetKey(KeyA));
    }

    [Fact]
    public void Several_failures_are_aggregated()
    {
        void Thrower(int key, int scancode, int action, int mods) => throw new InvalidOperationException("boom");
        BrowserInput.Key += Thrower;
        try
        {
            var ex = Assert.Throws<AggregateException>(() => Rec.Dispatch(Rec.Key(KeyA, 1), Rec.Key(KeyLeft, 1)));
            Assert.Equal(2, ex.InnerExceptions.Count);
        }
        finally
        {
            BrowserInput.Key -= Thrower;
        }
    }

    [Fact]
    public void Record_layout_matches_the_javascript_writer()
    {
        Assert.Equal(40, System.Runtime.InteropServices.Marshal.SizeOf<BrowserInput.EventRecord>());
        Assert.Equal(BrowserInput.RecordSize, System.Runtime.InteropServices.Marshal.SizeOf<BrowserInput.EventRecord>());
        string js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "brutal-browser.js"));
        Assert.Contains("const REC = 40;", js);
        Assert.Contains("KEY: 1, CHAR: 2, MOUSE_BUTTON: 3, CURSOR_POS: 4, CURSOR_ENTER: 5, SCROLL: 6", js);
        Assert.Contains("FOCUS: 7, SIZE: 8, POINTER_LOCK: 9, VISIBILITY: 10, CLOSE: 11", js);
    }
}
