using System.Runtime.InteropServices;
using Brutal.Numerics;

namespace Brutal.Browser;

/// <summary>Handler for key events. Values are GLFW's: <paramref name="key"/> = <c>GLFW_KEY_*</c> (-1 unknown), <paramref name="action"/> 0 release / 1 press / 2 repeat, <paramref name="mods"/> = <c>GLFW_MOD_*</c>.</summary>
public delegate void BrowserKeyHandler(int key, int scancode, int action, int mods);

/// <summary>Handler for mouse button events. <paramref name="button"/> = <c>GLFW_MOUSE_BUTTON_*</c> (0 left, 1 right, 2 middle), <paramref name="action"/> 0 release / 1 press.</summary>
public delegate void BrowserMouseButtonHandler(int button, int action, int mods);

/// <summary>Handler for scroll events; offsets are normalised to GLFW units (one wheel notch = 1, +Y = away from the user). <paramref name="flags"/> = <c>GLFW_SCROLL_*</c>.</summary>
public delegate void BrowserScrollHandler(double dx, double dy, int flags);

/// <summary>
/// Input queue consumer. JS (<c>brutal-browser.js</c>) normalises DOM events to GLFW semantics and queues compact
/// binary records; <see cref="PollEvents"/> drains them with one JS call per <see cref="BufferRecords"/> records, updates the
/// polled state (<see cref="GetKey"/>, <see cref="GetMouseButton"/>, cursor, focus) and raises the typed events in
/// order, like <c>glfwPollEvents</c>. Key and button numbers are GLFW's integer constants, so this assembly has no GLFW dependency.
/// </summary>
public static unsafe class BrowserInput
{
    /// <summary>Size of one queue record in bytes (shared with the JS side).</summary>
    public const int RecordSize = 40;

    /// <summary>Records fetched per JS call.</summary>
    public const int BufferRecords = 256;

    /// <summary>Highest GLFW key code (<c>GLFW_KEY_LAST</c>) + 1.</summary>
    public const int KeyCount = 349;

    /// <summary>Number of mouse buttons tracked (<c>GLFW_MOUSE_BUTTON_LAST</c> + 1).</summary>
    public const int MouseButtonCount = 8;

    internal enum EventType : byte
    {
        Key = 1, Char = 2, MouseButton = 3, CursorPos = 4, CursorEnter = 5, Scroll = 6,
        Focus = 7, Size = 8, PointerLock = 9, Visibility = 10, Close = 11,
    }

    [StructLayout(LayoutKind.Explicit, Size = RecordSize)]
    internal struct EventRecord
    {
        [FieldOffset(0)] public byte Type;
        [FieldOffset(1)] public byte Action;
        [FieldOffset(2)] public ushort Mods;
        [FieldOffset(4)] public int A;
        [FieldOffset(8)] public int B;
        [FieldOffset(12)] public int C;
        [FieldOffset(16)] public int D;
        [FieldOffset(24)] public double X;
        [FieldOffset(32)] public double Y;
    }

    private static EventRecord* s_buffer;
    private static readonly bool[] s_keys = new bool[KeyCount];
    private static readonly bool[] s_buttons = new bool[MouseButtonCount];
    private static double s_cursorX, s_cursorY;
    private static bool s_focused = true;
    private static bool s_pointerLocked;
    private static bool s_cursorInside;
    private static bool s_visible = true;
    private static List<Exception>? s_errors;

    /// <summary>A key was pressed, repeated or released.</summary>
    public static event BrowserKeyHandler? Key;

    /// <summary>A Unicode code point was typed (code point, mods).</summary>
    public static event Action<uint, int>? CharInput;

    /// <summary>A mouse button was pressed or released.</summary>
    public static event BrowserMouseButtonHandler? MouseButton;

    /// <summary>The cursor moved: position in CSS pixels relative to the canvas (a virtual unbounded position while pointer-locked).</summary>
    public static event Action<double, double>? CursorPos;

    /// <summary>The cursor entered (true) or left (false) the canvas.</summary>
    public static event Action<bool>? CursorEnter;

    /// <summary>The wheel / trackpad scrolled over the canvas.</summary>
    public static event BrowserScrollHandler? Scroll;

    /// <summary>The page gained (true) or lost (false) focus. On loss all pressed keys and buttons are released first.</summary>
    public static event Action<bool>? Focus;

    /// <summary>The pointer lock was acquired (true) or lost (false).</summary>
    public static event Action<bool>? PointerLockChanged;

    /// <summary>The page became visible (true) or hidden (false).</summary>
    public static event Action<bool>? VisibilityChanged;

    /// <summary>The page is being closed or unloaded.</summary>
    public static event Action? CloseRequested;

    /// <summary>Cursor position in CSS pixels as of the last polled event.</summary>
    public static double2 CursorPosition => new(s_cursorX, s_cursorY);

    /// <summary><c>true</c> if the page has focus.</summary>
    public static bool IsFocused => s_focused;

    /// <summary><c>true</c> while the canvas holds the pointer lock.</summary>
    public static bool IsPointerLocked => s_pointerLocked;

    /// <summary><c>true</c> while the cursor is over the canvas.</summary>
    public static bool IsCursorInside => s_cursorInside;

    /// <summary>Visibility as of the last polled event (see also <see cref="BrowserHost.IsVisible"/>).</summary>
    public static bool IsPageVisible => s_visible;

    /// <summary>Whether the key (<c>GLFW_KEY_*</c>) is down, as of the last poll.</summary>
    public static bool GetKey(int glfwKey) => (uint)glfwKey < KeyCount && s_keys[glfwKey];

    /// <summary>Whether the mouse button (<c>GLFW_MOUSE_BUTTON_*</c>) is down, as of the last poll.</summary>
    public static bool GetMouseButton(int glfwButton) => (uint)glfwButton < MouseButtonCount && s_buttons[glfwButton];

    /// <summary>
    /// Applies a pending canvas resize, drains the JS event queue and raises the events in order. Listener exceptions do
    /// not lose events: the rest of the queue is still dispatched and the exceptions are rethrown together afterwards.
    /// Allocation-free once warmed up; one JS call per <see cref="BufferRecords"/> records.
    /// </summary>
    public static void PollEvents()
    {
        if (!OperatingSystem.IsBrowser())
            return;

        EnsureBuffer();
        int bytes;
        do
        {
            bytes = BrowserInterop.PollEvents((nint)s_buffer, BufferRecords * RecordSize);
            DispatchBuffer(bytes / RecordSize);
        } while (bytes == BufferRecords * RecordSize);

        ThrowCollectedErrors();
    }

    /// <summary>Initial state sync (focus, visibility), called by <see cref="BrowserHost.Initialize"/>.</summary>
    internal static void Initialize()
    {
        if (!OperatingSystem.IsBrowser())
            return;

        EnsureBuffer();
        s_focused = BrowserInterop.HasFocus();
        s_visible = BrowserInterop.IsVisible();
        s_pointerLocked = BrowserInterop.IsPointerLocked();
    }

    private static void EnsureBuffer()
    {
        // pinned, never moves, JS writes into it during PollEvents
        if (s_buffer == null)
            s_buffer = (EventRecord*)NativeMemory.AlignedAlloc((nuint)(BufferRecords * RecordSize), 8);
    }

    /// <summary>Dispatches records from a managed span (unit-test entry point; same path as <see cref="PollEvents"/>).</summary>
    internal static void DispatchForTest(ReadOnlySpan<EventRecord> records)
    {
        for (int i = 0; i < records.Length; i++)
            Dispatch(in records[i]);
        ThrowCollectedErrors();
    }

    /// <summary>Clears polled state (unit-test helper).</summary>
    internal static void ResetForTest()
    {
        Array.Clear(s_keys);
        Array.Clear(s_buttons);
        s_cursorX = s_cursorY = 0;
        s_focused = true;
        s_pointerLocked = false;
        s_cursorInside = false;
        s_visible = true;
        s_errors = null;
    }

    private static void DispatchBuffer(int records)
    {
        for (int i = 0; i < records; i++)
            Dispatch(in s_buffer[i]);
    }

    private static void Dispatch(in EventRecord r)
    {
        switch ((EventType)r.Type)
        {
            case EventType.Key:
                OnKey(r.A, r.B, r.Action, r.Mods);
                break;
            case EventType.Char:
                Raise(CharInput, (uint)r.A, (int)r.Mods);
                break;
            case EventType.MouseButton:
                OnMouseButton(r.A, r.Action, r.Mods);
                break;
            case EventType.CursorPos:
                s_cursorX = r.X;
                s_cursorY = r.Y;
                Raise(CursorPos, r.X, r.Y);
                break;
            case EventType.CursorEnter:
                s_cursorInside = r.Action != 0;
                Raise(CursorEnter, s_cursorInside);
                break;
            case EventType.Scroll:
                Raise(Scroll, r.X, r.Y, (int)r.Action);
                break;
            case EventType.Focus:
                OnFocus(r.Action != 0);
                break;
            case EventType.Size:
                BrowserCanvas.Set(new int2(r.A, r.B), new int2(r.C, r.D), (float)r.X);
                RaiseResized();
                break;
            case EventType.PointerLock:
                s_pointerLocked = r.Action != 0;
                Raise(PointerLockChanged, s_pointerLocked);
                break;
            case EventType.Visibility:
                s_visible = r.Action != 0;
                Raise(VisibilityChanged, s_visible);
                break;
            case EventType.Close:
                Raise(CloseRequested);
                break;
        }
    }

    private static void OnKey(int key, int scancode, int action, int mods)
    {
        if (key >= 0 && key < KeyCount)
        {
            bool down = s_keys[key];
            if (action == 0)
            {
                if (!down)
                    return; // GLFW ignores the release of a key that is not pressed
                s_keys[key] = false;
            }
            else
            {
                if (action == 1 && down)
                    action = 2; // a press of an already-down key is a repeat
                s_keys[key] = true;
            }
        }

        Raise(Key, key, scancode, action, mods);
    }

    private static void OnMouseButton(int button, int action, int mods)
    {
        if (button >= 0 && button < MouseButtonCount)
        {
            if (s_buttons[button] == (action != 0))
                return; // duplicate press/release
            s_buttons[button] = action != 0;
        }

        Raise(MouseButton, button, action, mods);
    }

    private static void OnFocus(bool focused)
    {
        if (!focused)
        {
            // like GLFW: release everything that is down so no key or button stays stuck
            for (int key = 0; key < KeyCount; key++)
            {
                if (s_keys[key])
                {
                    s_keys[key] = false;
                    Raise(Key, key, 0, 0, 0);
                }
            }

            for (int button = 0; button < MouseButtonCount; button++)
            {
                if (s_buttons[button])
                {
                    s_buttons[button] = false;
                    Raise(MouseButton, button, 0, 0);
                }
            }
        }

        s_focused = focused;
        Raise(Focus, focused);
    }

    private static void RaiseResized()
    {
        try { BrowserCanvas.RaiseResized(); }
        catch (Exception ex) { Collect(ex); }
    }

    // ---- guarded raise helpers (no allocation: delegates are invoked directly) ----

    private static void Raise(BrowserKeyHandler? h, int a, int b, int c, int d)
    {
        if (h is null) return;
        try { h(a, b, c, d); } catch (Exception ex) { Collect(ex); }
    }

    private static void Raise(BrowserMouseButtonHandler? h, int a, int b, int c)
    {
        if (h is null) return;
        try { h(a, b, c); } catch (Exception ex) { Collect(ex); }
    }

    private static void Raise(BrowserScrollHandler? h, double a, double b, int c)
    {
        if (h is null) return;
        try { h(a, b, c); } catch (Exception ex) { Collect(ex); }
    }

    private static void Raise(Action<uint, int>? h, uint a, int b)
    {
        if (h is null) return;
        try { h(a, b); } catch (Exception ex) { Collect(ex); }
    }

    private static void Raise(Action<double, double>? h, double a, double b)
    {
        if (h is null) return;
        try { h(a, b); } catch (Exception ex) { Collect(ex); }
    }

    private static void Raise(Action<bool>? h, bool a)
    {
        if (h is null) return;
        try { h(a); } catch (Exception ex) { Collect(ex); }
    }

    private static void Raise(Action? h)
    {
        if (h is null) return;
        try { h(); } catch (Exception ex) { Collect(ex); }
    }

    private static void Collect(Exception ex) => (s_errors ??= []).Add(ex);

    private static void ThrowCollectedErrors()
    {
        var errors = s_errors;
        if (errors is null || errors.Count == 0)
            return;
        s_errors = null;
        if (errors.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        throw new AggregateException(errors);
    }
}
