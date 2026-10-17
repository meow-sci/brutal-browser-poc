using Brutal.Browser;
using Brutal.GlfwApi;
using Brutal.Vulkan.WebGpu;
using HelloMesh;
using HelloMesh.Assets;

// Browser host of HelloMesh (docs/architecture.md section 5). Main sets everything up, registers the frame delegate and returns;
// the page (samples/HelloMesh.Web/src/main.ts) then calls BrowserHost.Tick from requestAnimationFrame.
try
{
    WebIcd.Install(); // before anything touches Brutal.VulkanApi.Vulkan: loader key + canvas surface hook
    WebIcd.LoadShaderManifest(EmbeddedAssets.Read("Shaders/manifest.json"));
    BrowserHost.Initialize();

    if (!Glfw.Init())
        throw new InvalidOperationException("Glfw.Init failed.");
    Glfw.WindowHint(GlfwWindowHint.ClientApi, 0);
    // The page's CSS decides the canvas size; the swapchain follows the canvas backing store (BrowserCanvas.PixelSize).
    var window = Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title = "HelloMesh (BRUTAL, browser)", Size = BrowserCanvas.PixelSize });
    var app = new HelloMeshApp(window, new BrowserAudio());
    var stats = new FrameStats();

    // Page to game commands (docs/architecture.md section 5): the page's "Play sound" button calls BrowserHost.Dispatch("play-sound")
    // from its click handler, so the sound starts inside that user gesture.
    BrowserHost.RegisterCommand("play-sound", app.PlaySound);

    BrowserHost.RunLoop(dt =>
    {
        var start = BrowserHost.Now;
        Glfw.PollEvents();
        app.Frame(dt);
        stats.Add((BrowserHost.Now - start) * 1000.0, dt * 1000.0);
        return !app.ShouldClose;
    });
}
catch (Exception exception)
{
    Console.WriteLine($"ERROR {exception.Message}");
    Console.Error.WriteLine(exception);
}
