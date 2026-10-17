// Managed callback delegate types, copied verbatim from BRUTAL Brutal.Glfw (commit a25cdf8):
// Manual/Libraries/Glfw.Window.cs, Glfw.Input.cs, Glfw.Initialization.cs, Glfw.Monitor.cs.

namespace Brutal.GlfwApi;

/// <summary>The function pointer type for window position callbacks</summary>
/// <param name="window">The window that was moved.</param>
/// <param name="pos">
///     The new coordinates, in screen coordinates, of the upper-left corner of the client area of the
///     window.
/// </param>
/// <seealso cref="GlfwWindow.OnPosition" />
/// <seealso href="https://www.glfw.org/docs/latest/window_guide.html#window_pos">Window position</seealso>
public delegate void GlfwWindowPosCallback(GlfwWindow window, int2 pos);

/// <summary>The function pointer type for window size callbacks</summary>
/// <param name="window">The window that was resized.</param>
/// <param name="size">The new size, in screen coordinates, of the window.</param>
/// <seealso cref="GlfwWindow.OnSize" />
/// <seealso href="https://www.glfw.org/docs/latest/window_guide.html#window_size">Window size</seealso>
public delegate void GlfwWindowSizeCallback(GlfwWindow window, int2 size);

/// <summary>The function pointer type for window close callbacks</summary>
/// <param name="window">The window that the user attempted to close.</param>
/// <seealso cref="GlfwWindow.OnClose" />
/// <seealso href="https://www.glfw.org/docs/latest/window_guide.html#window_close">Window closing and close flag</seealso>
public delegate void GlfwWindowCloseCallback(GlfwWindow window);

/// <summary>The function pointer type for window content refresh callbacks</summary>
/// <param name="window">The window whose content needs to be refreshed.</param>
/// <seealso cref="GlfwWindow.OnRefresh" />
/// <seealso href="https://www.glfw.org/docs/latest/window_guide.html#window_refresh">Window damage and refresh</seealso>
public delegate void GlfwWindowRefreshCallback(GlfwWindow window);

/// <summary>The function pointer type for window focus callbacks</summary>
/// <param name="window">The window that gained or lost input focus.</param>
/// <param name="focusEvent">Enumerator describing the focus event.</param>
/// <seealso cref="GlfwWindow.OnFocus" />
/// <seealso href="https://www.glfw.org/docs/latest/window_guide.html#window_focus">Window input focus</seealso>
public delegate void GlfwWindowFocusCallback(GlfwWindow window, GlfwFocusEvent focusEvent);

/// <summary>The function pointer type for window iconify callbacks</summary>
/// <param name="window">The window that was iconified or restored.</param>
/// <param name="iconifyEvent">Enumerator describing the iconify event.</param>
/// <seealso cref="GlfwWindow.OnIconify" />
/// <seealso href="https://www.glfw.org/docs/latest/window_guide.html#window_iconify">Window iconification</seealso>
public delegate void GlfwWindowIconifyCallback(GlfwWindow window, GlfwIconifyEvent iconifyEvent);

/// <summary>The function pointer type for window maximize callbacks</summary>
/// <param name="window">The window that was maximized or restored.</param>
/// <param name="maximizeEvent">Enumerator describing the maximize event.</param>
/// <seealso cref="GlfwWindow.OnMaximize" />
/// <seealso href="https://www.glfw.org/docs/latest/window_guide.html#window_maximize">Window maximization</seealso>
public delegate void GlfwWindowMaximizeCallback(GlfwWindow window, GlfwMaximizeEvent maximizeEvent);

/// <summary>The function pointer type for framebuffer size callbacks</summary>
/// <param name="window">The window whose framebuffer was resized.</param>
/// <param name="size">The new size, in pixels, of the framebuffer.</param>
/// <seealso cref="GlfwWindow.OnFramebufferSize" />
/// <seealso href="https://www.glfw.org/docs/latest/window_guide.html#window_fbsize">Framebuffer size</seealso>
public delegate void GlfwFramebufferSizeCallback(GlfwWindow window, int2 size);

/// <summary>The function pointer type for window content scale callbacks</summary>
/// <param name="window">The window whose content scale changed.</param>
/// <param name="scale">The new content scale of the window.</param>
/// <seealso cref="GlfwWindow.OnContentScale" />
/// <seealso href="https://www.glfw.org/docs/latest/window_guide.html#window_scale">Window content scale</seealso>
public delegate void GlfwWindowContentScaleCallback(GlfwWindow window, float2 scale);

/// <summary>The function pointer type for mouse button callbacks.</summary>
/// <seealso cref="GlfwWindow.OnMouseButton">GlfwWindow.OnMouseButton</seealso>
/// <seealso href="https://www.glfw.org/docs/latest/input_guide.html#input_mouse_button">Mouse button input</seealso>
/// <param name="window">The window that received the event.</param>
/// <param name="button">The mouse button that was pressed or released.</param>
/// <param name="action">One of <see cref="GlfwButtonAction.Press" /> or <see cref="GlfwButtonAction.Release" />.</param>
/// <param name="mods">Bit field describing which modifier keys were held down.</param>
public delegate void GlfwMouseButtonCallback(
    GlfwWindow window,
    GlfwMouseButton button,
    GlfwButtonAction action,
    GlfwModifier mods
);

/// <summary>The function pointer type for cursor position callbacks.</summary>
/// <seealso cref="GlfwWindow.OnCursorPos">GlfwWindow.OnCursorPos</seealso>
/// <seealso href="https://www.glfw.org/docs/latest/input_guide.html#cursor_pos">Cursor position</seealso>
/// <param name="window">The window that received the event.</param>
/// <param name="pos">The new cursor coordinates, relative to the left edge of the content area.</param>
public delegate void GlfwCursorPosCallback(GlfwWindow window, double2 pos);

/// <summary>The function pointer type for cursor enter/leave callbacks.</summary>
/// <seealso cref="GlfwWindow.OnCursorEnter">GlfwWindow.OnCursorEnter</seealso>
/// <seealso href="https://www.glfw.org/docs/latest/input_guide.html#cursor_enter">Cursor enter/leave events</seealso>
/// <param name="window">The window that received the event.</param>
/// <param name="entered">
///     <see cref="Glfw.Constants.GLFW_TRUE" /> if the cursor entered the window's content area, or
///     <see cref="Glfw.Constants.GLFW_FALSE" /> if it left it.
/// </param>
public delegate void GlfwCursorEnterCallback(GlfwWindow window, bool entered);

/// <summary>The function pointer type for scroll callbacks.</summary>
/// <seealso cref="GlfwWindow.OnScroll">GlfwWindow.OnScroll</seealso>
/// <seealso href="https://www.glfw.org/docs/latest/input_guide.html#scrolling">Scroll input</seealso>
/// <param name="window">The window that received the event.</param>
/// <param name="offset">The scroll offset.</param>
public delegate void GlfwScrollCallback(GlfwWindow window, double2 offset);

/// <summary>The function pointer type for scroll detail callbacks.</summary>
/// <seealso cref="GlfwWindow.OnScrollDetail">GlfwWindow.OnScrollDetail</seealso>
/// <seealso href="https://www.glfw.org/docs/latest/input_guide.html#scrolling">Scroll input</seealso>
/// <param name="window">The window that received the event.</param>
/// <param name="offset">The scroll offset, zero when the event only marks a gesture boundary.</param>
/// <param name="flags">Where the offset came from and which gesture phase it belongs to.</param>
public delegate void GlfwScrollDetailCallback(GlfwWindow window, double2 offset, GlfwScrollFlags flags);

/// <summary>The function pointer type for trackpad zoom callbacks.</summary>
/// <seealso cref="GlfwWindow.OnTrackpadZoom">GlfwWindow.OnTrackpadZoom</seealso>
/// <param name="window">The window that received the event.</param>
/// <param name="scale">The zoom since the previous event, as a factor to multiply the current scale by.</param>
public delegate void GlfwTrackpadZoomCallback(GlfwWindow window, double scale);

/// <summary>The function pointer type for trackpad rotate callbacks.</summary>
/// <seealso cref="GlfwWindow.OnTrackpadRotate">GlfwWindow.OnTrackpadRotate</seealso>
/// <param name="window">The window that received the event.</param>
/// <param name="angle">The rotation since the previous event, in degrees, positive counter-clockwise.</param>
public delegate void GlfwTrackpadRotateCallback(GlfwWindow window, double angle);

/// <summary>The function pointer type for keyboard key callbacks.</summary>
/// <seealso cref="GlfwWindow.OnKey">GlfwWindow.OnKey</seealso>
/// <seealso href="https://www.glfw.org/docs/latest/input_guide.html#input_key">Key input</seealso>
/// <param name="window">The window that received the event.</param>
/// <param name="key">The keyboard key that was pressed or released.</param>
/// <param name="scancode">The system-specific scancode of the key.</param>
/// <param name="action">
///     <see cref="GlfwKeyAction.Press" />, <see cref="GlfwKeyAction.Release" /> or
///     <see cref="GlfwKeyAction.Repeat" />. Future releases may add more actions.
/// </param>
/// <param name="mods">Bit field describing which modifier keys were held down.</param>
public delegate void GlfwKeyCallback(
    GlfwWindow window,
    GlfwKey key,
    int scancode,
    GlfwKeyAction action,
    GlfwModifier mods
);

/// <summary>The function pointer type for Unicode character callbacks.</summary>
/// <seealso cref="GlfwWindow.OnChar">GlfwWindow.OnChar</seealso>
/// <seealso href="https://www.glfw.org/docs/latest/input_guide.html#input_char">Text input</seealso>
/// <param name="window">The window that received the event.</param>
/// <param name="codepoint">The Unicode code point of the character.</param>
public delegate void GlfwCharCallback(GlfwWindow window, uint codepoint);

/// <summary>The function pointer type for Unicode character with modifiers callbacks.</summary>
/// <seealso cref="GlfwWindow.OnCharMods">GlfwWindow.OnCharMods</seealso>
/// <seealso href="https://www.glfw.org/docs/latest/input_guide.html#input_char">Text input</seealso>
/// <param name="window">The window that received the event.</param>
/// <param name="codepoint">The Unicode code point of the character.</param>
/// <param name="mods">Bit field describing which modifier keys were held down.</param>
public delegate void GlfwCharModsCallback(GlfwWindow window, uint codepoint, GlfwModifier mods);

/// <summary>The function pointer type for path drop callbacks.</summary>
/// <seealso cref="GlfwWindow.OnDrop">GlfwWindow.OnDrop</seealso>
/// <seealso href="https://www.glfw.org/docs/latest/input_guide.html#path_drop">Path drop input</seealso>
/// <param name="window">The window that received the event.</param>
/// <param name="count">The number of dropped paths.</param>
/// <param name="paths">An array of UTF-8 encoded file and directory path names.</param>
public delegate void GlfwDropCallback(GlfwWindow window, int count, string[] paths);

/// <summary>The function pointer type for joystick configuration callbacks.</summary>
/// <seealso cref="Glfw.JoystickCallback">Glfw.JoystickCallback</seealso>
/// <seealso href="https://www.glfw.org/docs/latest/input_guide.html#joystick_event">Joystick configuration changes</seealso>
/// <param name="inJoystickId">The joystick that was connected or disconnected.</param>
/// <param name="inEvent">
///     One of <see cref="GlfwJoystickEvent.Connected" /> or
///     <see cref="GlfwJoystickEvent.Disconnected" />. Future releases may add more events.
/// </param>
public delegate void GlfwJoystickCallback(int inJoystickId, GlfwJoystickEvent inEvent);

/// <summary>The function pointer type for error callbacks.</summary>
public delegate void GlfwErrorCallback(GlfwErrorCode inError, string inDescription);

/// <summary>The function pointer type for monitor configuration callbacks.</summary>
/// <param name="monitor">The monitor that was connected or disconnected.</param>
/// <param name="eventType">
///     One of <see cref="GlfwMonitorEvent.Connected" /> or
///     <see cref="GlfwMonitorEvent.Disconnected" />.
/// </param>
/// <seealso cref="Glfw.MonitorCallback" />
/// <seealso href="https://www.glfw.org/docs/latest/monitor_guide.html#monitor_event">Monitor configuration changes</seealso>
public delegate void GlfwMonitorCallback(GlfwMonitor monitor, GlfwMonitorEvent eventType);
