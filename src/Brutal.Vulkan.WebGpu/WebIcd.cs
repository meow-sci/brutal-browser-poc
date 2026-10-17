using Brutal.Browser;
using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// The web ICD: a Vulkan subset (profile <c>VP_BRUTAL_web_poc_2026</c>) implemented on the browser's WebGPU.
/// Call <see cref="Install"/> before the first use of <c>Brutal.VulkanApi.Vulkan</c>; it registers the ICD's
/// <c>vkGetInstanceProcAddr</c> under the loader key (docs/architecture.md section 2) and the canvas-surface hook.
/// </summary>
public static class WebIcd
{
    /// <summary>The AppContext key read by the patched Brutal.Vulkan loader.</summary>
    public const string LoaderKey = "BRUTAL_VULKAN_GET_INSTANCE_PROC_ADDR";

    private static WebGpuBridge? s_bridge;
    private static bool s_installed;

    /// <summary>Behaviour switches (logging, profile validation). Read at the time of each call.</summary>
    public static WebIcdOptions Options { get; } = new();

    /// <summary>
    /// The WebGPU bridge. <see cref="Install"/> creates the JavaScript bridge (<c>webgpu-glue.js</c>, module <c>webgpu</c>) on
    /// browser-wasm; the desktop unit tests set a recording bridge before <see cref="Install"/> (see <see cref="IcdTestHooks"/>).
    /// </summary>
    internal static WebGpuBridge Bridge
    {
        get => s_bridge ?? throw new InvalidOperationException("No WebGPU bridge: call WebIcd.Install() (browser only) before using the ICD.");
        set => s_bridge = value ?? throw new ArgumentNullException(nameof(value));
    }

    internal static bool HasBridge => s_bridge is not null;

    /// <summary>
    /// Registers the ICD (loader key, <see cref="BrowserSurfaces.CreateSurface"/> hook) and reads the adapter limits through
    /// the bridge. Idempotent.
    /// </summary>
    public static unsafe void Install()
    {
        if (s_installed)
            return;
        if (s_bridge is null)
        {
            if (!OperatingSystem.IsBrowser())
                throw new InvalidOperationException("WebIcd.Install() runs in the browser only (the ICD needs webgpu-glue.js and a WebGPU device).");
            s_bridge = new JsWebGpuBridge();
        }

        Registry.EnsureBuilt();
        AppContext.SetData(LoaderKey, (nint)(delegate*<VkInstance, byte*, PfnVkVoidFunction>)&Icd.vkGetInstanceProcAddr);
        BrowserSurfaces.CreateSurface = Icd.CreateSurfaceForCanvas;
        Profile.TryInitialize(s_bridge);
        s_installed = true;
    }

    /// <summary>
    /// Loads the shader manifest (docs/architecture.md section 6) that maps SHA-256 hashes of SPIR-V modules to WGSL.
    /// Must be called before the first <c>vkCreateShaderModule</c>; may be called again to add or replace entries.
    /// </summary>
    public static void LoadShaderManifest(ReadOnlySpan<byte> json) => ShaderManifest.Load(json);
}

/// <summary>Options of the web ICD (see <see cref="WebIcd.Options"/>).</summary>
public sealed class WebIcdOptions
{
    /// <summary>Log each unsupported Vulkan feature/command path once (default <c>true</c>).</summary>
    public bool LogUnsupported { get; set; } = true;

    /// <summary>Run the WebGPU-only profile checks (<c>WebProfileLint</c>) on every call that can violate them (default <c>true</c>).</summary>
    public bool ValidateProfile { get; set; } = true;

    /// <summary>Reject instance/device extensions and layers outside the profile instead of warning (default <c>false</c>).</summary>
    public bool StrictExtensions { get; set; }

    /// <summary>Wrap every replay in WebGPU validation error scopes (slower; errors are attributed to the submit) (default <c>false</c>).</summary>
    public bool DebugErrorScopes { get; set; }
}

/// <summary>Thrown by Vulkan commands that the web ICD does not implement (generated stubs) or cannot implement on WebGPU.</summary>
public sealed class VulkanUnsupportedException : NotSupportedException
{
    /// <summary>Creates the exception for <paramref name="command"/>.</summary>
    public VulkanUnsupportedException(string command, string? reason = null)
        : base(reason is null
            ? $"{command} is not supported by the web ICD (Brutal.Vulkan.WebGpu, profile VP_BRUTAL_web_poc_2026)."
            : $"{command} is not supported by the web ICD: {reason}")
    {
        Command = command;
    }

    /// <summary>The Vulkan command name, e.g. <c>vkCmdDrawMeshTasksEXT</c>.</summary>
    public string Command { get; }
}
