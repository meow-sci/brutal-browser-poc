using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using Brutal.Browser.Logging;

namespace Brutal.Browser;

/// <summary>
/// Browser host: initialisation, the frame loop, time and visibility.
/// <para>
/// Boot protocol: the page creates the runtime (<c>dotnet.create()</c>), registers the JS modules, runs <c>Main</c>, which
/// calls <see cref="Initialize"/> and <see cref="RunLoop"/> and returns; the page then calls <see cref="Tick"/> from
/// <c>requestAnimationFrame</c> until it returns <c>false</c>.
/// </para>
/// <para>
/// Page to game commands: the game registers named handlers with <see cref="RegisterCommand"/>; the page (a button, a menu, a test)
/// runs one with <c>exports.Brutal.Browser.BrowserHost.Dispatch(name)</c> from a DOM event handler, so the handler executes inside
/// the user gesture that caused it (that is how the page can start audio from a button, not only from a key the game polls a frame later).
/// </para>
/// </summary>
public static partial class BrowserHost
{
    /// <summary>Largest delta time passed to the frame delegate (guards against hidden-tab / breakpoint gaps).</summary>
    public const double MaxDeltaSeconds = 0.1;

    private static bool s_initialized;
    private static Func<double, bool>? s_frame;
    private static double s_lastTimestampMs = double.NaN;
    private static bool s_exitRequested;
    private static readonly Stopwatch s_clock = Stopwatch.StartNew();
    private static readonly ConcurrentDictionary<string, Action> s_commands = new(StringComparer.Ordinal);

    /// <summary>Number of frames started by <see cref="Tick"/> (1-based inside the frame delegate).</summary>
    public static long FrameCount { get; private set; }

    /// <summary><c>true</c> while a frame delegate is registered and has not asked to stop.</summary>
    public static bool IsRunning => s_frame is not null && !s_exitRequested;

    /// <summary>Monotonic time in seconds (<c>performance.now() / 1000</c> in the browser).</summary>
    public static double Now => OperatingSystem.IsBrowser() ? BrowserInterop.Now() / 1000.0 : s_clock.Elapsed.TotalSeconds;

    /// <summary><c>false</c> while the tab is hidden (<c>document.visibilityState</c>). One scalar JS call.</summary>
    public static bool IsVisible => !OperatingSystem.IsBrowser() || BrowserInterop.IsVisible();

    /// <summary>
    /// Idempotent. Reads the canvas size, syncs focus/visibility and, unless the application already initialised BRUTAL's
    /// <c>LogSystem</c>, installs <see cref="BrowserConsoleLoggerProvider"/> so BRUTAL logs reach the browser console.
    /// </summary>
    public static void Initialize()
    {
        if (s_initialized)
            return;
        s_initialized = true;

        BrowserLogging.InstallIfUnconfigured();
        BrowserCanvas.Refresh();
        BrowserInput.Initialize();
    }

    /// <summary>
    /// Registers the per-frame delegate. Browser: stores it and returns; the page's <c>requestAnimationFrame</c> loop calls
    /// <see cref="Tick"/>. Elsewhere (desktop tests): runs a blocking loop until the delegate returns <c>false</c>.
    /// </summary>
    /// <param name="frame">Called once per frame with the delta time in seconds; return <c>false</c> to stop.</param>
    public static void RunLoop(Func<double, bool> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Initialize();
        s_frame = frame;
        s_exitRequested = false;
        s_lastTimestampMs = double.NaN;

        if (OperatingSystem.IsBrowser())
            return;

        while (Tick(s_clock.Elapsed.TotalMilliseconds))
            Thread.Sleep(1);
    }

    /// <summary>
    /// Called by the page's <c>requestAnimationFrame</c> callback (<c>exports.Brutal.Browser.BrowserHost.Tick(timestamp)</c>).
    /// Computes the delta time (0 on the first frame, clamped to <see cref="MaxDeltaSeconds"/>), runs the frame delegate and
    /// returns whether the loop should continue. An exception from the frame prints <c>ERROR &lt;message&gt;</c> (stdout) and
    /// the stack (stderr), stops the loop and returns <c>false</c>.
    /// </summary>
#pragma warning disable CA1416 // JSExport is browser-only by design; the class still compiles everywhere
    [JSExport]
#pragma warning restore CA1416
    public static bool Tick(double timestampMs)
    {
        var frame = s_frame;
        if (frame is null || s_exitRequested)
            return false;

        double dt = double.IsNaN(s_lastTimestampMs) ? 0.0 : (timestampMs - s_lastTimestampMs) / 1000.0;
        s_lastTimestampMs = timestampMs;
        dt = Math.Clamp(dt, 0.0, MaxDeltaSeconds);

        try
        {
            FrameCount++;
            if (frame(dt))
                return !s_exitRequested;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR {ex.Message}");
            Console.Error.WriteLine(ex.ToString());
        }

        s_exitRequested = true;
        return false;
    }

    /// <summary>
    /// Registers (or replaces) the handler of a page command. Names are case-sensitive; use short kebab-case names such as
    /// <c>play-sound</c>. The handler runs synchronously inside <see cref="Dispatch"/>, on the thread that called it (the browser's only
    /// thread), so keep it short and do not block.
    /// </summary>
    /// <param name="name">Command name the page passes to <see cref="Dispatch"/>.</param>
    /// <param name="handler">Invoked for every dispatch of <paramref name="name"/>.</param>
    public static void RegisterCommand(string name, Action handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(handler);
        s_commands[name] = handler;
    }

    /// <summary>Removes a command registered with <see cref="RegisterCommand"/>; returns whether it existed.</summary>
    public static bool UnregisterCommand(string name) => name is not null && s_commands.TryRemove(name, out _);

    /// <summary>
    /// Runs the handler registered for <paramref name="command"/> (page to game, <c>exports.Brutal.Browser.BrowserHost.Dispatch(name)</c>).
    /// An unknown name prints <c>WARN unknown command &lt;name&gt;</c>; an exception from the handler is caught and prints
    /// <c>ERROR &lt;message&gt;</c> (stdout) and the stack (stderr) without stopping the frame loop.
    /// </summary>
    /// <param name="command">Name given to <see cref="RegisterCommand"/>.</param>
#pragma warning disable CA1416 // JSExport is browser-only by design; the class still compiles everywhere
    [JSExport]
#pragma warning restore CA1416
    public static void Dispatch(string command)
    {
        if (command is null || !s_commands.TryGetValue(command, out var handler))
        {
            Console.WriteLine($"WARN unknown command {command}");
            return;
        }

        try
        {
            handler();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR {ex.Message}");
            Console.Error.WriteLine(ex.ToString());
        }
    }

    /// <summary>Sets <c>document.title</c>.</summary>
    public static void SetTitle(string title)
    {
        if (OperatingSystem.IsBrowser())
            BrowserInterop.SetTitle(title);
    }

    /// <summary>Stops the loop: the next <see cref="Tick"/> (or the current one, on return) yields <c>false</c>.</summary>
    public static void Exit() => s_exitRequested = true;
}
