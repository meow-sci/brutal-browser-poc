using Brutal.GlfwApi;
using HelloMesh.Assets;
using HelloMesh.Audio;
using HelloMesh.Rendering;
using HelloMesh.Scene;

namespace HelloMesh;

/// <summary>
/// The composition root both hosts construct: a <see cref="RenderHost"/> (Vulkan, swapchain, frame loop), the
/// <see cref="SunglassesScene"/> (the game) and the sound, plus the console markers of docs/architecture.md section 12
/// (<c>READY</c>, <c>FRAME n</c>, <c>AUDIO ...</c>; the scene prints <c>CAMERA</c>). The host owns the window and the loop: it calls
/// <c>Glfw.PollEvents()</c> and then <see cref="Frame"/> once per frame.
/// </summary>
public sealed class HelloMeshApp : IDisposable
{
    private const string SoundResource = "Assets/874222__samrad20__generic-explosion.ogg";
    private const int FrameMarkerInterval = 60;

    private readonly GlfwWindow _window;
    private readonly IAudio _audio;
    private readonly RenderHost _host;
    private readonly SunglassesScene _scene;
    private ScreenshotCapture? _screenshot;
    private volatile bool _soundReady;

    /// <summary>Creates the renderer and the scene, starts loading the sound and prints <c>READY</c>.</summary>
    public HelloMeshApp(GlfwWindow window, IAudio audio)
    {
        _window = window;
        _audio = audio;
        _host = new RenderHost(window);
        _scene = new SunglassesScene(_host, window, PlaySound);
        _host.FrameRendered += count =>
        {
            if (count % FrameMarkerInterval == 0)
                Console.WriteLine($"FRAME {count}");
        };

        _ = LoadSoundAsync();
        Console.WriteLine("READY");
    }

    /// <summary>True once the window was asked to close.</summary>
    public bool ShouldClose => _window.ShouldClose;

    /// <summary>Updates the scene by <paramref name="dt"/> seconds and renders one frame (acquire, record, submit, present).</summary>
    public void Frame(double dt)
    {
        _scene.Update(dt);
        _host.RenderFrame(_scene);
    }

    /// <summary>Plays the sound (Space key). Reports <c>AUDIO play</c> or <c>AUDIO error</c>.</summary>
    public void PlaySound()
    {
        if (!_soundReady)
        {
            Console.WriteLine("AUDIO error sound is not loaded");
            return;
        }
        try
        {
            _audio.PlayOneShot();
            Console.WriteLine("AUDIO play");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"AUDIO error {exception.Message}");
        }
    }

    /// <summary>Captures the next rendered frame for <see cref="TakeScreenshot"/> (desktop host only, see <see cref="ScreenshotCapture"/>).</summary>
    public void RequestScreenshot()
    {
        if (!_host.SupportsCapture)
            throw new NotSupportedException("The swapchain images cannot be copied out on this surface.");
        _screenshot?.Dispose();
        _screenshot = new ScreenshotCapture(_host.Context.Device);
        _host.Capture = _screenshot;
    }

    /// <summary>Returns the frame captured after <see cref="RequestScreenshot"/>, once; null if it has not been rendered yet.</summary>
    public Screenshot? TakeScreenshot()
    {
        var screenshot = _screenshot?.Result;
        if (screenshot is not null)
            _screenshot = null;
        return screenshot;
    }

    private async Task LoadSoundAsync()
    {
        try
        {
            await _audio.LoadAsync(EmbeddedAssets.Read(SoundResource));
            _soundReady = true;
            Console.WriteLine("AUDIO loaded");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"AUDIO error {exception.Message}");
        }
    }

    /// <summary>Waits for the GPU, then destroys the scene and the renderer.</summary>
    public void Dispose()
    {
        _host.WaitIdle();
        _scene.Dispose();
        _host.Dispose();
    }
}
