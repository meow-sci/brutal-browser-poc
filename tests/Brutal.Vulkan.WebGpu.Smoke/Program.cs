using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Brutal.Vulkan.WebGpu;

namespace WebGpuIcdSmoke;

/// <summary>
/// Browser smoke app of the web ICD (docs/architecture.md sections 5 and 12): Main installs the ICD, loads the shader manifest,
/// builds the <see cref="TriangleScene"/> and prints READY; main.js then calls <see cref="Tick"/> from requestAnimationFrame.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class SmokeApp
{
    private static TriangleScene? s_scene;
    private static ComputeCheck? s_compute;
    private static bool s_computeReported;
    private static bool s_failed;

    /// <summary>Frame 60 draws the front triangle 5000 more times, each with a push-constant update: the ring must grow past 4096 slots.</summary>
    private const int PushStressFrame = 60, PushStressDraws = 5000;

    public static void Main()
    {
        try
        {
            WebIcd.Install();
            var assembly = typeof(SmokeApp).Assembly;
            WebIcd.LoadShaderManifest(Resource("Shaders/manifest.json"));
            s_scene = new TriangleScene(Resource("Shaders/tri.vert.spv"), Resource("Shaders/tri.frag.spv")) { ReadbackAtFrame = 30 };
            s_compute = new ComputeCheck(s_scene.Device, s_scene.Queue, Resource("Shaders/fill.comp.spv"));
            Console.WriteLine("READY");
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    /// <summary>One frame per animation frame; false stops the loop.</summary>
    [JSExport]
    public static bool Tick(double timestampMs)
    {
        if (s_failed || s_scene is null)
            return false;
        try
        {
            var hadPixel = s_scene.ReadbackPixel is not null;
            s_scene.ExtraFrontDraws = s_scene.FramesRendered == PushStressFrame ? PushStressDraws : 0;
            s_scene.Frame();
            if (s_scene.ExtraFrontDraws > 0)
                Console.WriteLine($"PUSHSTRESS draws={PushStressDraws + 2} in one submit");
            if (!hadPixel && s_scene.ReadbackPixel is { } p)
                Console.WriteLine($"READBACK {string.Join(',', p)} format={s_scene.ColorFormat} latency={s_scene.ReadbackLatency}");
            if (!s_computeReported && s_compute!.Poll())
            {
                s_computeReported = true;
                Console.WriteLine($"COMPUTE ok={s_compute.IsCorrect} values={string.Join(',', s_compute.Result!.Take(4))}...");
            }
            if (s_scene.FramesRendered % 60 == 0)
                Console.WriteLine($"FRAME {s_scene.FramesRendered}");
            return true;
        }
        catch (Exception e)
        {
            Fail(e);
            return false;
        }
    }

    private static void Fail(Exception e)
    {
        s_failed = true;
        Console.WriteLine($"ERROR {e.GetType().Name}: {e.Message}");
        Console.WriteLine(e.ToString());
    }

    private static byte[] Resource(string name)
    {
        using var stream = typeof(SmokeApp).Assembly.GetManifestResourceStream(name)
            ?? throw new FileNotFoundException($"embedded resource {name} not found");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
