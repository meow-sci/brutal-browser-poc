using Brutal.Browser;

namespace Brutal.GlfwApi;

// Browser implementation of the Glfw static class. Member names and signatures follow BRUTAL's Brutal.GlfwApi; what the
// browser cannot do throws NotSupportedException (see NotAvailable). Verbatim BRUTAL parts live in Glfw.cs, Callbacks.cs
// and Generated/.
public static unsafe partial class Glfw
{
    internal static bool Initialized { get; private set; }

    private static double s_timeBase;
    private static GlfwWindow? s_window;
    private static byte** s_instanceExtensions;
    private static readonly Dictionary<GlfwWindowHint, int> s_hints = [];

    internal static NotSupportedException NotAvailable(string member) =>
        new($"{member} is not available in the browser");

    internal static void RegisterWindow(GlfwWindow window)
    {
        if (s_window is not null)
            throw NotAvailable("Creating a second window (single canvas)");
        s_window = window;
    }

    internal static void UnregisterWindow(GlfwWindow window)
    {
        if (ReferenceEquals(s_window, window))
            s_window = null;
    }

    // ---- initialization ----

    /// <summary>Initializes the browser host (<see cref="BrowserHost.Initialize"/>) and the GLFW clock.</summary>
    /// <returns>Always <c>true</c>.</returns>
    public static bool Init()
    {
        if (Initialized)
            return true;

        BrowserHost.Initialize();
        s_timeBase = BrowserHost.Now;
        Initialized = true;
        return true;
    }

    /// <summary>Destroys the window (if any) and marks GLFW as terminated. Has no effect if not initialized.</summary>
    public static void Terminate()
    {
        if (!Initialized)
            return;

        s_window?.Dispose();
        Initialized = false;
    }

    /// <summary>Not available: the browser version is not selectable.</summary>
    public static void InitAllocator(
        GlfwAllocateUnmanagedCallback allocate,
        GlfwReallocateUnmanagedCallback reallocate,
        GlfwDeallocateUnmanagedCallback deallocate,
        void* user = null
    ) => throw NotAvailable(nameof(InitAllocator));

    /// <summary>Accepted and ignored: there is no platform or Cocoa/Wayland behaviour to configure in the browser.</summary>
    public static void SetInitHint(GlfwInitHint hint, int value) { }

    /// <summary>Raised for errors reported by this implementation (currently unused: failures throw).</summary>
    public static event GlfwErrorCallback? ErrorCallback;

    /// <summary>Always <see cref="GlfwErrorCode.NoError"/>.</summary>
    public static GlfwErrorCode GetError(out string? description)
    {
        description = null;
        return GlfwErrorCode.NoError;
    }

    /// <summary>A GLFW version number.</summary>
    public struct VersionInfo
    {
        /// <summary>Major version.</summary>
        public int Major;
        /// <summary>Minor version.</summary>
        public int Minor;
        /// <summary>Revision.</summary>
        public int Revision;
    }

    /// <summary>The GLFW version whose API this subset mirrors (3.5.1).</summary>
    public static VersionInfo Version => new()
    {
        Major = GLFW_VERSION_MAJOR,
        Minor = GLFW_VERSION_MINOR,
        Revision = GLFW_VERSION_REVISION,
    };

    /// <summary>Version string identifying the browser implementation.</summary>
    public static string VersionString => "3.5.1 Browser (Brutal.Browser)";

    // ---- window creation and events ----

    /// <summary>Accepted and remembered; window hints do not change anything in the browser (the page owns the canvas).</summary>
    public static void WindowHint(GlfwWindowHint hint, int value) => s_hints[hint] = value;

    /// <summary>Accepted and ignored.</summary>
    public static void WindowHint(GlfwWindowHint hint, string value) { }

    /// <summary>Resets the remembered hints.</summary>
    public static void DefaultWindowHints() => s_hints.Clear();

    /// <summary>
    /// Wraps the page's canvas in a <see cref="GlfwWindow"/>. Only one window exists; <see cref="GlfwWindow.CreateInfo.Size"/>
    /// is ignored (the canvas size comes from CSS; use <see cref="GlfwWindow.Size"/> to set it) and the title is applied to the document.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="Init"/> was not called.</exception>
    /// <exception cref="NotSupportedException">A window already exists, or a monitor/share window was requested.</exception>
    public static GlfwWindow CreateWindow(in GlfwWindow.CreateInfo info)
    {
        if (!Initialized)
            throw new InvalidOperationException($"Failed to create window: {GlfwErrorCode.NotInitialized} (GLFW is not initialized).");
        if (info.Monitor.IsNotNull())
            throw NotAvailable("CreateWindow with a monitor (fullscreen)");
        if (info.Share is not null)
            throw NotAvailable("CreateWindow with a shared window");
        return new GlfwWindow(info);
    }

    /// <summary>
    /// Drains the JS input queue (one interop call), raises the window events in order and rethrows exceptions thrown by
    /// event handlers, like BRUTAL's <c>Glfw.PollEvents</c>.
    /// </summary>
    public static void PollEvents()
    {
        BrowserInput.PollEvents();
        ExceptionAggregator.CheckExceptions();
    }

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>. The browser drives the loop (requestAnimationFrame); use <see cref="PollEvents"/>.</summary>
    public static void WaitEvents() => throw NotAvailable(nameof(WaitEvents));

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public static void WaitEventsTimeout(double timeout) => throw NotAvailable(nameof(WaitEventsTimeout));

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public static void PostEmptyEvent() => throw NotAvailable(nameof(PostEmptyEvent));

    // ---- time ----

    /// <summary>Seconds since <see cref="Init"/> (or since the last <see cref="SetTime"/>), from <c>performance.now()</c>.</summary>
    public static double Time
    {
        get => Initialized ? BrowserHost.Now - s_timeBase : 0.0;
        set => SetTime(value);
    }

    /// <summary>Sets <see cref="Time"/> to <paramref name="time"/> seconds.</summary>
    public static void SetTime(double time) => s_timeBase = BrowserHost.Now - time;

    /// <summary>Microseconds of the browser's monotonic clock.</summary>
    public static ulong GetTimerValue() => (ulong)(BrowserHost.Now * 1_000_000.0);

    /// <summary>Always 1,000,000 (microseconds).</summary>
    public static ulong GetTimerFrequency() => 1_000_000UL;

    // ---- clipboard ----

    /// <summary>Synchronous cache of the clipboard text, see <see cref="BrowserClipboard"/>.</summary>
    public static string? Clipboard
    {
        get => BrowserClipboard.Text;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            BrowserClipboard.Text = value;
        }
    }

    /// <summary>Sets the clipboard text (see <see cref="Clipboard"/>).</summary>
    public static void SetClipboard(string @string) => BrowserClipboard.Text = @string;

    // ---- Vulkan ----

    /// <summary><c>true</c>: the Vulkan-on-WebGPU layer provides the API (it must be installed before use).</summary>
    public static bool VulkanSupported() => true;

    /// <summary>
    /// Returns the instance extensions the web surface needs: <c>VK_KHR_surface</c> and <c>VK_BRUTAL_web_surface</c>.
    /// The array and strings are allocated once and never freed.
    /// </summary>
    public static byte** GetRequiredInstanceExtensions(out uint count)
    {
        if (s_instanceExtensions == null)
        {
            string[] names = ["VK_KHR_surface", "VK_BRUTAL_web_surface"];
            var array = (byte**)NativeMemory.Alloc((nuint)(names.Length * sizeof(byte*)));
            for (int i = 0; i < names.Length; i++)
                array[i] = (byte*)Marshal.StringToCoTaskMemUTF8(names[i]);
            s_instanceExtensions = array;
        }

        count = 2;
        return s_instanceExtensions;
    }

    /// <summary>Not available: the web ICD exposes its own <c>vkGetInstanceProcAddr</c> (see <c>WebIcd.Install()</c>).</summary>
    public static nint GetInstanceProcAddress(nint instance, string procName) =>
        throw NotAvailable(nameof(GetInstanceProcAddress));

    /// <summary>Always <c>true</c>: the single canvas can present from any queue family.</summary>
    public static bool GetPhysicalDevicePresentationSupport(nint instance, nint physicalDevice, int queueFamilyIndex) => true;

    /// <summary>
    /// Creates the surface for the page's canvas through <see cref="BrowserSurfaces.CreateSurface"/> (set by the web ICD).
    /// </summary>
    /// <returns>A <c>VkResult</c>.</returns>
    /// <exception cref="InvalidOperationException"><see cref="BrowserSurfaces.CreateSurface"/> is not set.</exception>
    public static int CreateWindowSurface(nint instance, GlfwWindow window, out ulong surface)
    {
        var create = BrowserSurfaces.CreateSurface
            ?? throw new InvalidOperationException(
                "BrowserSurfaces.CreateSurface is not set: install the web ICD (WebIcd.Install()) before creating a window surface.");
        surface = 0;
        return create(instance, BrowserCanvas.SurfaceHandle, out surface);
    }

    // ---- input helpers ----

    /// <summary>Raw mouse motion is requested as <c>unadjustedMovement</c> with a fallback, so it is reported as supported.</summary>
    public static bool RawMotionSupported() => true;

    /// <summary>Always <c>null</c>: the browser exposes no layout-dependent key names synchronously.</summary>
    public static string? GetKeyName(GlfwKey key, int scanCode) => null;

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public static int GetKeyScanCode(int key) => throw NotAvailable(nameof(GetKeyScanCode));

    // Joysticks: gamepad support is not implemented yet; the API answers "no joystick is connected" instead of throwing,
    // so per-frame gamepad polling in game code keeps working.
    /// <summary>Never raised: no joystick is ever connected.</summary>
    public static event GlfwJoystickCallback? JoystickCallback;

    /// <summary>Always false: gamepads are not implemented yet.</summary>
    public static bool JoystickPresent(int joystickId) => false;

    /// <summary>Always empty (no joystick connected).</summary>
    public static ReadOnlySpan<float> GetJoystickAxes(int joystickId) => default;

    /// <summary>Always empty (no joystick connected).</summary>
    public static ReadOnlySpan<byte> GetJoystickButtons(int joystickId) => default;

    /// <summary>Always empty (no joystick connected).</summary>
    public static ReadOnlySpan<GlfwJoystickHat> GetJoystickHats(int joystickId) => default;

    /// <summary>Always null (no joystick connected).</summary>
    public static string? GetJoystickName(int joystickId) => null;

    /// <summary>Always null (no joystick connected).</summary>
    public static string? GetJoystickGuid(int joystickId) => null;

    /// <summary>Ignored (no joystick connected).</summary>
    public static void SetJoystickUserPointer(int joystickId, void* handle) { }

    /// <summary>Always null (no joystick connected).</summary>
    public static void* GetJoystickUserPointer(int joystickId) => null;

    /// <summary>Always false (no joystick connected).</summary>
    public static bool JoystickIsGamepad(int joystickId) => false;

    /// <summary>Ignored; returns false.</summary>
    public static bool UpdateGamepadMappings(string mappings) => false;

    /// <summary>Always null (no gamepad connected).</summary>
    public static string? GetGamepadName(int gamepadId) => null;

    /// <summary>Always false with a default state (no gamepad connected).</summary>
    public static bool GetGamepadState(int gamepadId, out GlfwGamepadState state)
    {
        state = default;
        return false;
    }

    // ---- monitors and cursors ----

    /// <summary>Never raised: the browser exposes no monitors.</summary>
    public static event GlfwMonitorCallback? MonitorCallback;

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public static GlfwMonitor PrimaryMonitor => throw NotAvailable(nameof(PrimaryMonitor));

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>.</summary>
    public static ReadOnlySpan<GlfwMonitor> GetMonitors() => throw NotAvailable(nameof(GetMonitors));

    /// <summary>Creates a cursor that maps the standard shape to a CSS <c>cursor</c> value.</summary>
    public static GlfwCursor CreateCursor(GlfwStandardCursor shape) => new(shape switch
    {
        GlfwStandardCursor.Arrow => "default",
        GlfwStandardCursor.IBeam => "text",
        GlfwStandardCursor.Crosshair => "crosshair",
        GlfwStandardCursor.PointingHand or GlfwStandardCursor.Hand => "pointer",
        GlfwStandardCursor.ResizeEw or GlfwStandardCursor.HResize => "ew-resize",
        GlfwStandardCursor.ResizeNs or GlfwStandardCursor.VResize => "ns-resize",
        GlfwStandardCursor.ResizeNwse => "nwse-resize",
        GlfwStandardCursor.ResizeNesw => "nesw-resize",
        GlfwStandardCursor.ResizeAll => "move",
        GlfwStandardCursor.NotAllowed => "not-allowed",
        _ => "default",
    });

    /// <summary>Not available in the browser: throws <see cref="NotSupportedException"/>. Use the standard shapes.</summary>
    public static GlfwCursor CreateCursor(in GlfwCursor.CreateInfo info) => throw NotAvailable("CreateCursor(image)");
}
