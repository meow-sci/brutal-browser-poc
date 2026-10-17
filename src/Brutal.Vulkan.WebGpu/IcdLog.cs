using Brutal.VulkanApi;

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// ICD diagnostics: <c>Console</c> lines with the <c>ERROR</c>/<c>WARN</c>/<c>INFO [webgpu]</c> prefixes of docs/architecture.md
/// section 12, forwarded to every <c>VkDebugUtilsMessengerEXT</c>. The once-only helpers test their key before they format a
/// message, so a command that is called every frame (e.g. an ignored <c>vkCmdSetDepthBias</c>) does not allocate after the
/// first call.
/// </summary>
internal static class IcdLog
{
    private const long KindIgnored = 1, KindUnsupported = 2;

    private static readonly HashSet<(string A, string B, long N)> s_once = new();

    public static void Error(string message)
    {
        Write("ERROR [webgpu] " + message);
        DebugMessengers.Emit(VkDebugUtilsMessageSeverityFlagsEXT.ErrorBitEXT, VkDebugUtilsMessageTypeFlagsEXT.ValidationBitEXT, message);
    }

    public static void Warn(string message)
    {
        Write("WARN [webgpu] " + message);
        DebugMessengers.Emit(VkDebugUtilsMessageSeverityFlagsEXT.WarningBitEXT, VkDebugUtilsMessageTypeFlagsEXT.ValidationBitEXT, message);
    }

    public static void Info(string message)
    {
        Write("INFO [webgpu] " + message);
        DebugMessengers.Emit(VkDebugUtilsMessageSeverityFlagsEXT.InfoBitEXT, VkDebugUtilsMessageTypeFlagsEXT.GeneralBitEXT, message);
    }

    /// <summary>
    /// True the first time the key (<paramref name="a"/>, <paramref name="b"/>, <paramref name="n"/>) is seen since the last reset.
    /// Test it before building the message: the check itself does not allocate.
    /// </summary>
    public static bool First(string a, string b = "", long n = 0)
    {
        lock (s_once)
            return s_once.Add((a, b, n));
    }

    /// <summary>ERROR once per <paramref name="key"/> (for paths that run at object creation or on an application error).</summary>
    public static void ErrorOnce(string key, string message)
    {
        if (First(key))
            Error(message);
    }

    /// <summary>WARN once per <paramref name="key"/> (for paths that run at object creation or on an application error).</summary>
    public static void WarnOnce(string key, string message)
    {
        if (First(key))
            Warn(message);
    }

    /// <summary>INFO once per <paramref name="key"/>.</summary>
    public static void InfoOnce(string key, string message)
    {
        if (First(key))
            Info(message);
    }

    /// <summary>An unsupported feature that the ICD ignores (the call still succeeds): one WARN per function and reason.</summary>
    public static void Ignored(string function, string reason)
    {
        if (WebIcd.Options.LogUnsupported && First(function, reason, KindIgnored))
            Warn($"{function}: {reason} is not supported on WebGPU and is ignored");
    }

    /// <summary>An unsupported feature that makes the call fail: one ERROR per function and reason.</summary>
    public static void Unsupported(string function, string reason)
    {
        if (WebIcd.Options.LogUnsupported && First(function, reason, KindUnsupported))
            Error($"{function}: {reason} is not supported on WebGPU");
    }

    /// <summary>Forgets which once-only messages were written (<see cref="IcdTestHooks.Reset"/>).</summary>
    internal static void ResetOnce()
    {
        lock (s_once)
            s_once.Clear();
    }

    private static void Write(string line)
    {
        Console.WriteLine(line);
        IcdTestHooks.LogCapture?.Invoke(line);
    }
}
