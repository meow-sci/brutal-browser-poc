using Brutal.Browser;

namespace Brutal.GlfwApi;

/// <summary>
/// The page's canvas as a GLFW window (single window). <see cref="Size"/> is the canvas CSS size, <see cref="FramebufferSize"/>
/// its backing-store size in device pixels and <see cref="ContentScale"/> the device pixel ratio. Events are raised from
/// <see cref="Glfw.PollEvents"/>; state setters update the cached value when the matching event arrives, like native GLFW.
/// </summary>
public sealed unsafe class GlfwWindow : IDisposable, INullable
{
    /// <summary>Details for creating a <see cref="GlfwWindow" />.</summary>
    public struct CreateInfo
    {
        /// <summary>The title of the window.</summary>
        public required string Title;

        /// <summary>The size of the window in pixels. Ignored in the browser: the canvas is sized by CSS.</summary>
        public required int2 Size;

        /// <summary>The monitor to use for full screen windows. Must be <see cref="GlfwMonitor.Null"/> in the browser.</summary>
        public GlfwMonitor Monitor;

        /// <summary>A window to share resources with. Must be <c>null</c> in the browser.</summary>
        public GlfwWindow? Share;
    }

    private string _title;
    private int2 _size;
    private int2 _framebufferSize;
    private float2 _contentScale;
    private double2 _cursorPos;
    private bool _shouldClose;
    private bool _isFocused;
    private void* _userPointer;
    private bool _disposed;
    private int _cursorMode = GLFW_CURSOR_NORMAL;
    private bool _rawMouseMotion;
    private int _lockKeyMods;
    private int _unlimitedMouseButtons;

    internal GlfwWindow(CreateInfo info)
    {
        Glfw.RegisterWindow(this);

        _title = info.Title;
        BrowserHost.SetTitle(info.Title);
        _size = BrowserCanvas.CssSize;
        _framebufferSize = BrowserCanvas.PixelSize;
        var dpr = BrowserCanvas.DevicePixelRatio;
        _contentScale = new float2(dpr, dpr);
        _cursorPos = BrowserInput.CursorPosition;
        _isFocused = BrowserInput.IsFocused;

        BrowserInput.Key += HandleKey;
        BrowserInput.CharInput += HandleChar;
        BrowserInput.MouseButton += HandleMouseButton;
        BrowserInput.CursorPos += HandleCursorPos;
        BrowserInput.CursorEnter += HandleCursorEnter;
        BrowserInput.Scroll += HandleScroll;
        BrowserInput.Focus += HandleFocus;
        BrowserInput.PointerLockChanged += HandlePointerLockChanged;
        BrowserInput.CloseRequested += HandleClose;
        BrowserCanvas.Resized += HandleResized;
    }

    /// <summary>The canvas surface handle (<see cref="BrowserCanvas.SurfaceHandle"/>), or 0 once disposed.</summary>
    public nint NativeHandle => _disposed ? 0 : BrowserCanvas.SurfaceHandle;

    /// <summary>Gets/sets the document title.</summary>
    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            BrowserHost.SetTitle(value);
        }
    }

    /// <summary>Always (0, 0): the canvas has no window position. Setting a different value throws.</summary>
    public int2 Position
    {
        get => default;
        set
        {
            if (value != default)
                throw Glfw.NotAvailable($"{nameof(GlfwWindow)}.{nameof(Position)} setter");
        }
    }

    /// <summary>Always 0 (the canvas has no screen position). Setting another value throws.</summary>
    public int X
    {
        get => 0;
        set => Position = new int2(value, Y);
    }

    /// <summary>Always 0 (the canvas has no screen position). Setting another value throws.</summary>
    public int Y
    {
        get => 0;
        set => Position = new int2(X, value);
    }

    /// <summary>
    /// Canvas size in CSS pixels. The setter sets an explicit CSS size on the canvas; the cached value (and
    /// <see cref="OnSize"/>) update at the next <see cref="Glfw.PollEvents"/>.
    /// </summary>
    public int2 Size
    {
        get => _size;
        set
        {
            if (value == _size)
                return;
            BrowserCanvas.SetCssSize(value.X, value.Y);
        }
    }

    /// <summary><see cref="Size"/>.X.</summary>
    public int Width
    {
        get => _size.X;
        set => Size = new int2(value, Height);
    }

    /// <summary><see cref="Size"/>.Y.</summary>
    public int Height
    {
        get => _size.Y;
        set => Size = new int2(Width, value);
    }

    /// <summary>Canvas backing-store size in device pixels (the swapchain extent).</summary>
    public int2 FramebufferSize => _framebufferSize;

    /// <summary>Width / height of <see cref="FramebufferSize"/>. Setting a fixed ratio throws.</summary>
    public float AspectRatio
    {
        get => _framebufferSize.Y == 0 ? 0f : _framebufferSize.X / (float)_framebufferSize.Y;
        set => throw Glfw.NotAvailable($"{nameof(GlfwWindow)}.{nameof(AspectRatio)} setter");
    }

    /// <summary><c>window.devicePixelRatio</c> on both axes.</summary>
    public float2 ContentScale => _contentScale;

    /// <summary>
    /// Cursor position in CSS pixels relative to the canvas (a virtual unbounded position while the pointer is locked).
    /// The setter moves only the virtual position; browsers cannot warp the real pointer.
    /// </summary>
    public double2 CursorPos
    {
        get => _cursorPos;
        set
        {
            if (value == _cursorPos)
                return;
            BrowserCanvas.SetCursorPosition(value.X, value.Y);
        }
    }

    /// <summary><see cref="CursorPos"/>.X.</summary>
    public double CursorX
    {
        get => _cursorPos.X;
        set => CursorPos = new double2(value, CursorY);
    }

    /// <summary><see cref="CursorPos"/>.Y.</summary>
    public double CursorY
    {
        get => _cursorPos.Y;
        set => CursorPos = new double2(CursorX, value);
    }

    /// <summary>Always 1. Setting a different value throws.</summary>
    public float Opacity
    {
        get => 1f;
        set
        {
            if (Math.Abs(value - 1f) > 0.00001f)
                throw Glfw.NotAvailable($"{nameof(GlfwWindow)}.{nameof(Opacity)} setter");
        }
    }

    /// <summary>Always <see cref="GlfwMonitor.Null"/> (windowed).</summary>
    public GlfwMonitor Monitor
    {
        get => GlfwMonitor.Null;
        set
        {
            if (value.IsNotNull())
                throw Glfw.NotAvailable($"{nameof(GlfwWindow)}.{nameof(Monitor)} setter");
        }
    }

    /// <summary>Always false.</summary>
    public bool IsMaximised => false;

    /// <summary>Always false.</summary>
    public bool IsIconified => false;

    /// <summary>True while the page has focus (from focus/blur events).</summary>
    public bool IsFocused => _isFocused;

    /// <summary>Always false (windows are never fullscreen on a monitor).</summary>
    public bool IsFullscreen => Monitor.IsNotNull();

    /// <summary>Always true.</summary>
    public bool IsWindowed => !IsFullscreen;

    /// <summary>Close flag. Set by the page unloading (<see cref="OnClose"/>) or by the game to stop its loop.</summary>
    public bool ShouldClose
    {
        get => _shouldClose;
        set => _shouldClose = value;
    }

    /// <summary>Opaque pointer stored for the game.</summary>
    public void* UserPointer
    {
        get => _userPointer;
        set => _userPointer = value;
    }

    /// <summary>Always <c>true</c>. Hiding the canvas is not supported.</summary>
    public bool Visible
    {
        get => true;
        set
        {
            if (!value)
                throw Glfw.NotAvailable($"{nameof(GlfwWindow)}.{nameof(Visible)} setter");
        }
    }

    /// <summary>Detaches from the input events and releases the pointer lock; the canvas stays in the page.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        BrowserInput.Key -= HandleKey;
        BrowserInput.CharInput -= HandleChar;
        BrowserInput.MouseButton -= HandleMouseButton;
        BrowserInput.CursorPos -= HandleCursorPos;
        BrowserInput.CursorEnter -= HandleCursorEnter;
        BrowserInput.Scroll -= HandleScroll;
        BrowserInput.Focus -= HandleFocus;
        BrowserInput.PointerLockChanged -= HandlePointerLockChanged;
        BrowserInput.CloseRequested -= HandleClose;
        BrowserCanvas.Resized -= HandleResized;
        Glfw.UnregisterWindow(this);

        if (_cursorMode != GLFW_CURSOR_NORMAL)
            ApplyCursorMode(GLFW_CURSOR_NORMAL);
    }

    /// <summary>True after <see cref="Dispose"/>.</summary>
    public bool IsNull() => _disposed;

    // ---- input ----

    /// <summary>Polled key state after the last <see cref="Glfw.PollEvents"/>: <c>Press</c> or <c>Release</c>.</summary>
    public GlfwKeyAction GetKey(GlfwKey key) => BrowserInput.GetKey((int)key) ? GlfwKeyAction.Press : GlfwKeyAction.Release;

    /// <summary>Polled mouse button state after the last <see cref="Glfw.PollEvents"/>.</summary>
    public GlfwButtonAction GetMouseButton(GlfwMouseButton button) =>
        BrowserInput.GetMouseButton((int)button) ? GlfwButtonAction.Press : GlfwButtonAction.Release;

    /// <summary>The value set with <see cref="SetInputMode"/> (the cursor mode follows the pointer-lock state).</summary>
    public int GetInputMode(GlfwInputMode mode) => mode switch
    {
        GlfwInputMode.Cursor => _cursorMode,
        GlfwInputMode.RawMouseMotion => _rawMouseMotion ? GLFW_TRUE : GLFW_FALSE,
        GlfwInputMode.StickyKeys or GlfwInputMode.StickyMouseButtons => GLFW_FALSE,
        GlfwInputMode.LockKeyMods => _lockKeyMods,
        GlfwInputMode.UnlimitedMouseButtons => _unlimitedMouseButtons,
        _ => throw Glfw.NotAvailable($"{nameof(GetInputMode)}({mode})"),
    };

    /// <summary>
    /// <c>Cursor</c>: <c>Normal</c>, <c>Hidden</c> (CSS <c>cursor: none</c>) and <c>Disabled</c> (pointer lock; needs a user
    /// gesture and is retried on the next click/keydown; Escape releases it and flips the mode back to <c>Normal</c>).
    /// <c>RawMouseMotion</c> requests unadjusted movement for the pointer lock. Sticky keys/buttons and <c>Captured</c> are not available.
    /// </summary>
    public void SetInputMode(GlfwInputMode mode, int value)
    {
        switch (mode)
        {
            case GlfwInputMode.Cursor:
                if (value is not (GLFW_CURSOR_NORMAL or GLFW_CURSOR_HIDDEN or GLFW_CURSOR_DISABLED))
                    throw Glfw.NotAvailable($"{nameof(SetInputMode)}(Cursor, {(GlfwCursorMode)value})");
                ApplyCursorMode(value);
                break;
            case GlfwInputMode.RawMouseMotion:
                _rawMouseMotion = value != GLFW_FALSE;
                if (_cursorMode == GLFW_CURSOR_DISABLED)
                    BrowserCanvas.RequestPointerLock(_rawMouseMotion);
                break;
            case GlfwInputMode.LockKeyMods:
                _lockKeyMods = value;
                break;
            case GlfwInputMode.UnlimitedMouseButtons:
                _unlimitedMouseButtons = value;
                break;
            default:
                throw Glfw.NotAvailable($"{nameof(SetInputMode)}({mode})");
        }
    }

    private void ApplyCursorMode(int mode)
    {
        _cursorMode = mode;
        switch (mode)
        {
            case GLFW_CURSOR_DISABLED:
                BrowserCanvas.RequestPointerLock(_rawMouseMotion);
                break;
            case GLFW_CURSOR_HIDDEN:
                BrowserCanvas.ExitPointerLock();
                BrowserCanvas.SetCursorVisible(false);
                break;
            default:
                BrowserCanvas.ExitPointerLock();
                BrowserCanvas.SetCursorVisible(true);
                break;
        }
    }

    /// <summary>Sets the CSS cursor of the canvas; a null cursor restores the default.</summary>
    public void SetCursor(GlfwCursor cursor) => BrowserCanvas.SetCursorStyle(cursor.CssCursor ?? string.Empty);

    /// <summary>Gives keyboard focus to the canvas.</summary>
    public void Focus() => BrowserCanvas.Focus();

    // ---- attributes and unsupported window management ----

    /// <summary>Supports <c>Focused</c>, <c>Hovered</c>, <c>Visible</c>, <c>Iconified</c>, <c>Maximized</c>, <c>Resizable</c>, <c>Decorated</c> and <c>TransparentFramebuffer</c>.</summary>
    public int GetAttribute(GlfwWindowAttribute attribute) => attribute switch
    {
        GlfwWindowAttribute.Focused => _isFocused ? GLFW_TRUE : GLFW_FALSE,
        GlfwWindowAttribute.Hovered => BrowserInput.IsCursorInside ? GLFW_TRUE : GLFW_FALSE,
        GlfwWindowAttribute.Visible => GLFW_TRUE,
        GlfwWindowAttribute.Iconified or GlfwWindowAttribute.Maximized => GLFW_FALSE,
        GlfwWindowAttribute.Resizable => GLFW_TRUE,
        GlfwWindowAttribute.Decorated or GlfwWindowAttribute.TransparentFramebuffer => GLFW_FALSE,
        _ => throw Glfw.NotAvailable($"{nameof(GetAttribute)}({attribute})"),
    };

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>. (there is no OpenGL context).</summary>
    public int GetAttribute(GlfwContextAttribute attribute) => throw Glfw.NotAvailable($"{nameof(GetAttribute)}({attribute})");

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public void SetAttribute(GlfwWindowAttribute attrib, int value) => throw Glfw.NotAvailable(nameof(SetAttribute));

    /// <summary>Returns immediately: the canvas always has a non-zero framebuffer.</summary>
    public void WaitForWindow() { }

    /// <summary>Re-reads the cached size state from the canvas.</summary>
    public void Refresh()
    {
        _size = BrowserCanvas.CssSize;
        _framebufferSize = BrowserCanvas.PixelSize;
        var dpr = BrowserCanvas.DevicePixelRatio;
        _contentScale = new float2(dpr, dpr);
        _cursorPos = BrowserInput.CursorPosition;
        _isFocused = BrowserInput.IsFocused;
    }

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public void SetIcon(int count, GlfwImage image) => throw Glfw.NotAvailable(nameof(SetIcon));

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public void SetSizeLimits(int2 minSize, int2 maxSize) => throw Glfw.NotAvailable(nameof(SetSizeLimits));

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public void SetFullscreen(GlfwMonitor monitor, int2 size, int refreshRate) => throw Glfw.NotAvailable(nameof(SetFullscreen));

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public void SetWindowed(int2 pos, int2 size) => throw Glfw.NotAvailable(nameof(SetWindowed));

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public void CenterToMonitor(GlfwMonitor monitor) => throw Glfw.NotAvailable(nameof(CenterToMonitor));

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public void GetWindowFrameSize(out int left, out int top, out int right, out int bottom) =>
        throw Glfw.NotAvailable(nameof(GetWindowFrameSize));

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public void Iconify() => throw Glfw.NotAvailable(nameof(Iconify));

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public void Restore() => throw Glfw.NotAvailable(nameof(Restore));

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public void Maximize() => throw Glfw.NotAvailable(nameof(Maximize));

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public void RequestAttention() => throw Glfw.NotAvailable(nameof(RequestAttention));

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>. (there is no OpenGL context).</summary>
    public void MakeContextCurrent() => throw Glfw.NotAvailable(nameof(MakeContextCurrent));

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>. (presentation goes through the Vulkan swapchain).</summary>
    public void SwapBuffers() => throw Glfw.NotAvailable(nameof(SwapBuffers));

    // ---- events (raised from Glfw.PollEvents; handler exceptions are rethrown there) ----

    /// <summary>A character was typed (from the <c>key</c> of a <c>keydown</c> without Ctrl/Cmd).</summary>
    public event GlfwCharCallback? OnChar;
    /// <summary>A character was typed, with the modifier keys.</summary>
    public event GlfwCharModsCallback? OnCharMods;
    /// <summary>The pointer entered or left the canvas.</summary>
    public event GlfwCursorEnterCallback? OnCursorEnter;
    /// <summary>The cursor moved (CSS pixels relative to the canvas; virtual while pointer-locked).</summary>
    public event GlfwCursorPosCallback? OnCursorPos;
    /// <summary>Never raised in the browser.</summary>
    public event GlfwDropCallback? OnDrop;
    /// <summary>The canvas backing-store size changed (recreate the swapchain).</summary>
    public event GlfwFramebufferSizeCallback? OnFramebufferSize;
    /// <summary>A key was pressed, repeated or released (GLFW key codes from <c>KeyboardEvent.code</c>).</summary>
    public event GlfwKeyCallback? OnKey;
    /// <summary>A mouse button was pressed or released over the canvas.</summary>
    public event GlfwMouseButtonCallback? OnMouseButton;
    /// <summary>Wheel input (one notch = 1).</summary>
    public event GlfwScrollCallback? OnScroll;
    /// <summary>Wheel input with its details.</summary>
    public event GlfwScrollDetailCallback? OnScrollDetail;
    /// <summary>Never raised in the browser.</summary>
    public event GlfwTrackpadZoomCallback? OnTrackpadZoom;
    /// <summary>Never raised in the browser.</summary>
    public event GlfwTrackpadRotateCallback? OnTrackpadRotate;
    /// <summary>The page is being hidden or unloaded (<c>pagehide</c>); <see cref="ShouldClose"/> becomes true.</summary>
    public event GlfwWindowCloseCallback? OnClose;
    /// <summary><c>devicePixelRatio</c> changed.</summary>
    public event GlfwWindowContentScaleCallback? OnContentScale;
    /// <summary>The page gained or lost focus.</summary>
    public event GlfwWindowFocusCallback? OnFocus;
    /// <summary>Never raised in the browser.</summary>
    public event GlfwWindowIconifyCallback? OnIconify;
    /// <summary>Never raised in the browser.</summary>
    public event GlfwWindowMaximizeCallback? OnMaximize;
    /// <summary>Never raised in the browser.</summary>
    public event GlfwWindowPosCallback? OnPosition;
    /// <summary>Never raised in the browser.</summary>
    public event GlfwWindowRefreshCallback? OnRefresh;
    /// <summary>The canvas CSS size changed.</summary>
    public event GlfwWindowSizeCallback? OnSize;

    private void HandleKey(int key, int scancode, int action, int mods)
    {
        try
        {
            OnKey?.Invoke(this, (GlfwKey)key, scancode, (GlfwKeyAction)action, (GlfwModifier)mods);
        }
        catch (Exception ex)
        {
            Glfw.ExceptionAggregator.Push("Error in Window.OnKey callback", ex);
        }
    }

    private void HandleChar(uint codepoint, int mods)
    {
        // same filter as glfw's _glfwInputChar: control characters are not text
        if (codepoint < 32 || (codepoint > 126 && codepoint < 160))
            return;

        try
        {
            OnCharMods?.Invoke(this, codepoint, (GlfwModifier)mods);
        }
        catch (Exception ex)
        {
            Glfw.ExceptionAggregator.Push("Error in Window.OnCharMods callback", ex);
        }

        try
        {
            OnChar?.Invoke(this, codepoint);
        }
        catch (Exception ex)
        {
            Glfw.ExceptionAggregator.Push("Error in Window.OnChar callback", ex);
        }
    }

    private void HandleMouseButton(int button, int action, int mods)
    {
        try
        {
            OnMouseButton?.Invoke(this, (GlfwMouseButton)button, (GlfwButtonAction)action, (GlfwModifier)mods);
        }
        catch (Exception ex)
        {
            Glfw.ExceptionAggregator.Push("Error in Window.OnMouseButton callback", ex);
        }
    }

    private void HandleCursorPos(double x, double y)
    {
        try
        {
            _cursorPos = new double2(x, y);
            OnCursorPos?.Invoke(this, _cursorPos);
        }
        catch (Exception ex)
        {
            Glfw.ExceptionAggregator.Push("Error in Window.OnCursorPos callback", ex);
        }
    }

    private void HandleCursorEnter(bool entered)
    {
        try
        {
            OnCursorEnter?.Invoke(this, entered);
        }
        catch (Exception ex)
        {
            Glfw.ExceptionAggregator.Push("Error in Window.OnCursorEnter callback", ex);
        }
    }

    private void HandleScroll(double dx, double dy, int flags)
    {
        var offset = new double2(dx, dy);
        try
        {
            OnScroll?.Invoke(this, offset);
        }
        catch (Exception ex)
        {
            Glfw.ExceptionAggregator.Push("Error in Window.OnScroll callback", ex);
        }

        try
        {
            OnScrollDetail?.Invoke(this, offset, (GlfwScrollFlags)flags);
        }
        catch (Exception ex)
        {
            Glfw.ExceptionAggregator.Push("Error in Window.OnScrollDetail callback", ex);
        }
    }

    private void HandleFocus(bool focused)
    {
        try
        {
            _isFocused = focused;
            OnFocus?.Invoke(this, focused ? GlfwFocusEvent.GainFocus : GlfwFocusEvent.LostFocus);
        }
        catch (Exception ex)
        {
            Glfw.ExceptionAggregator.Push("Error in Window.OnFocus callback", ex);
        }
    }

    private void HandlePointerLockChanged(bool locked)
    {
        // Escape (or the browser) released the lock: the cursor is visible again, so report Normal like a game toggling the mode
        if (!locked && _cursorMode == GLFW_CURSOR_DISABLED)
        {
            _cursorMode = GLFW_CURSOR_NORMAL;
            BrowserCanvas.SetCursorVisible(true);
        }
    }

    private void HandleClose()
    {
        try
        {
            _shouldClose = true;
            OnClose?.Invoke(this);
        }
        catch (Exception ex)
        {
            Glfw.ExceptionAggregator.Push("Error in Window.OnClose callback", ex);
        }
    }

    private void HandleResized()
    {
        // update the whole cache first so every handler sees a consistent window; then raise like GLFW (size, framebuffer, scale)
        var sizeChanged = BrowserCanvas.CssSize != _size;
        var framebufferChanged = BrowserCanvas.PixelSize != _framebufferSize;
        var dpr = BrowserCanvas.DevicePixelRatio;
        var scale = new float2(dpr, dpr);
        var scaleChanged = scale != _contentScale;

        _size = BrowserCanvas.CssSize;
        _framebufferSize = BrowserCanvas.PixelSize;
        _contentScale = scale;

        if (sizeChanged)
        {
            try
            {
                OnSize?.Invoke(this, _size);
            }
            catch (Exception ex)
            {
                Glfw.ExceptionAggregator.Push("Error in Window.OnSize callback", ex);
            }
        }

        if (framebufferChanged)
        {
            try
            {
                OnFramebufferSize?.Invoke(this, _framebufferSize);
            }
            catch (Exception ex)
            {
                Glfw.ExceptionAggregator.Push("Error in Window.OnFramebufferSize callback", ex);
            }
        }

        if (scaleChanged)
        {
            try
            {
                OnContentScale?.Invoke(this, _contentScale);
            }
            catch (Exception ex)
            {
                Glfw.ExceptionAggregator.Push("Error in Window.OnContentScale callback", ex);
            }
        }
    }
}
