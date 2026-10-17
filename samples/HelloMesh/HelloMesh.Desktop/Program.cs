using System.Globalization;
using Brutal.GlfwApi;
using Brutal.Numerics;
using HelloMesh;
using HelloMesh.Rendering;

// Desktop host of HelloMesh: GLFW window, OpenAL audio and a plain loop. The game itself is HelloMeshApp (shared with the browser host).
try
{
    var options = Options.Parse(args);
    GraphicsConfig.EnableValidation = options.Validation;
    return Run(options);
}
catch (Exception exception)
{
    Console.WriteLine($"ERROR {exception.Message}");
    Console.Error.WriteLine(exception);
    return 1;
}

static int Run(Options options)
{
    const int AudioTestFrame = 10;
    if (!Glfw.Init())
        throw new InvalidOperationException("glfwInit failed.");
    try
    {
        Glfw.WindowHint(GlfwWindowHint.ClientApi, 0);
        using var window = Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title = "HelloMesh (BRUTAL)", Size = new int2(1280, 720) });
        using var audio = new OpenAlAudio();
        using var app = new HelloMeshApp(window, audio);

        var frame = 0;
        var previous = Glfw.Time;
        while (!app.ShouldClose && (options.Frames is not { } frames || frame < frames))
        {
            Glfw.PollEvents();
            var now = Glfw.Time;
            if (options.Frames.HasValue && frame == AudioTestFrame)
                app.PlaySound();
            if (options.ScreenshotPath != null && frame == options.Frames - 1)
                app.RequestScreenshot();
            app.Frame(now - previous);
            previous = now;
            frame++;
        }

        if (options.ScreenshotPath != null)
            WritePpm(options.ScreenshotPath, app.TakeScreenshot() ?? throw new InvalidOperationException("No frame was captured."));
        return 0;
    }
    finally
    {
        Glfw.Terminate();
    }
}

static void WritePpm(string path, Screenshot screenshot)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    using var file = File.Create(path);
    file.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{screenshot.Width} {screenshot.Height}\n255\n"));
    file.Write(screenshot.Rgb);
}

/// <summary>Command line of the desktop host.</summary>
internal sealed record Options(int? Frames, string? ScreenshotPath, bool Validation)
{
    public static Options Parse(string[] args)
    {
        int? frames = null;
        string? screenshot = null;
        var validation = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--frames": frames = int.Parse(Value(args, ref i), CultureInfo.InvariantCulture); break;
                case "--screenshot": screenshot = Value(args, ref i); break;
                case "--validation": validation = true; break;
                default: throw new ArgumentException($"Unknown argument '{args[i]}'. Use --frames N, --screenshot FILE.ppm, --validation.");
            }
        }
        return new Options(screenshot != null ? frames ?? 120 : frames, screenshot, validation);
    }

    private static string Value(string[] args, ref int index) =>
        ++index < args.Length ? args[index] : throw new ArgumentException($"Missing value for {args[index - 1]}.");
}
