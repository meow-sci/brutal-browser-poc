using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using Brutal.Browser;
using Brutal.Browser.Audio;
using Brutal.GlfwApi;
using Brutal.Numerics;

namespace Brutal.Browser.Smoke;

/// <summary>
/// Smoke app: creates a GLFW window through the browser drop-in, prints one console line per input event (<c>EVT ...</c>),
/// prints <c>FRAME n</c> every 60 frames and, after the page's start click, decodes and plays the embedded Ogg
/// (<c>AUDIO loaded</c> / <c>AUDIO play</c>). Console contract: architecture.md section 12.
/// </summary>
public static partial class SmokeApp
{
    private static GlfwWindow s_window = null!;
    private static Sound? s_sound;
    private static Voice? s_voice;
    private static bool s_crashTest;

    public static void Main(string[] args)
    {
        s_crashTest = args.Contains("crash");
        BrowserHost.Initialize();
        Console.WriteLine($"HOST canvas css={Fmt(BrowserCanvas.CssSize)} pixels={Fmt(BrowserCanvas.PixelSize)} dpr={BrowserCanvas.DevicePixelRatio.ToString(CultureInfo.InvariantCulture)}");

        if (!Glfw.Init())
            throw new InvalidOperationException("Glfw.Init failed");
        Glfw.Log.Info($"browser GLFW shim initialised: {Glfw.VersionString}"); // BRUTAL LogSystem -> BrowserConsoleLoggerProvider
        Glfw.WindowHint(GlfwWindowHint.ClientApi, 0);
        s_window = Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title = "Brutal.Browser smoke", Size = new int2(1280, 720) });

        s_window.OnKey += (w, key, scancode, action, mods) =>
        {
            Console.WriteLine($"EVT key key={key} scancode={scancode} action={action} mods={(int)mods}");
            if (action == GlfwKeyAction.Press && key == GlfwKey.L)
                ToggleCursorMode(w);
        };
        s_window.OnChar += (w, cp) => Console.WriteLine($"EVT char codepoint={cp}");
        s_window.OnMouseButton += (w, button, action, mods) =>
            Console.WriteLine($"EVT mouse button={ButtonName(button)} action={action} mods={(int)mods}");
        s_window.OnCursorPos += (w, pos) => Console.WriteLine($"EVT cursor x={F(pos.X)} y={F(pos.Y)}");
        s_window.OnScroll += (w, offset) => Console.WriteLine($"EVT scroll dx={F(offset.X)} dy={F(offset.Y)}");
        s_window.OnSize += (w, size) => Console.WriteLine($"EVT resize size={Fmt(size)} fb={Fmt(w.FramebufferSize)} scale={F(w.ContentScale.X)}");
        s_window.OnFramebufferSize += (w, size) => Console.WriteLine($"EVT framebuffer size={Fmt(size)}");
        s_window.OnFocus += (w, focus) => Console.WriteLine($"EVT focus {focus}");
        s_window.OnClose += w => Console.WriteLine("EVT close");

        string[] extensions = RequiredExtensions();
        Console.WriteLine($"GLFW extensions=[{string.Join(",", extensions)}] time={F(Glfw.Time)}");
        try
        {
            Glfw.CreateWindowSurface(0, s_window, out _);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"GLFW surface hook unset (expected): {ex.Message.Split(':')[0]}");
        }

        Console.WriteLine("READY");
        BrowserHost.RunLoop(Frame);
    }

    private static bool Frame(double dt)
    {
        Glfw.PollEvents();
        long n = BrowserHost.FrameCount;
        if (s_crashTest && n == 3)
            throw new InvalidOperationException("smoke crash test");
        if (n % 60 == 0)
            Console.WriteLine($"FRAME {n}");
        return !s_window.ShouldClose;
    }

    /// <summary>Called by main.js from the start click: resumes audio (already done in the gesture), decodes and plays the Ogg.</summary>
#pragma warning disable CA1416 // JSExport is browser-only; this app only runs in the browser
    [JSExport]
#pragma warning restore CA1416
    public static async Task StartAudio(bool forceManagedDecoder)
    {
        try
        {
            AudioEngine.PreferManagedDecoder = forceManagedDecoder;
            var engine = AudioEngine.Create();
            bool running = await engine.ResumeAsync();
            Console.WriteLine($"AUDIO state={engine.State} running={running}");

            byte[] file = ReadEmbedded("Assets/874222__samrad20__generic-explosion.ogg");
            var sw = Stopwatch.StartNew();
            s_sound = await engine.DecodeAsync(file);
            Console.WriteLine($"AUDIO loaded decoder={s_sound.Decoder} channels={s_sound.Channels} sampleRate={s_sound.SampleRate} duration={s_sound.Duration.ToString("F3", CultureInfo.InvariantCulture)} decodeMs={sw.ElapsedMilliseconds}");

            s_voice = s_sound.Play();
            Console.WriteLine("AUDIO play");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AUDIO error {ex.Message}");
        }
    }

    private static void ToggleCursorMode(GlfwWindow window)
    {
        int next = window.GetInputMode(GlfwInputMode.Cursor) == (int)GlfwCursorMode.Disabled
            ? (int)GlfwCursorMode.Normal
            : (int)GlfwCursorMode.Disabled;
        window.SetInputMode(GlfwInputMode.Cursor, next);
        Console.WriteLine($"EVT cursormode {(GlfwCursorMode)window.GetInputMode(GlfwInputMode.Cursor)}");
    }

    private static unsafe string[] RequiredExtensions()
    {
        byte** names = Glfw.GetRequiredInstanceExtensions(out uint count);
        var result = new string[count];
        for (int i = 0; i < count; i++)
            result[i] = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)names[i]) ?? "";
        return result;
    }

    private static byte[] ReadEmbedded(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"embedded resource {name} not found");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    // GlfwMouseButton.Number1 == Left, so Enum.ToString would print "Number1"
    private static string ButtonName(GlfwMouseButton b) => (int)b switch { 0 => "Left", 1 => "Right", 2 => "Middle", var n => $"Number{n + 1}" };

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Fmt(int2 v) => $"{v.X}x{v.Y}";
}
