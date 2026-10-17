using System.Reflection;
using Brutal.Vulkan.WebGpu;

namespace WebGpuIcd.Tests;

/// <summary>Installs the ICD once per process and gives every test a fresh recording bridge and log capture.</summary>
internal static class TestIcd
{
    private static readonly object Gate = new();
    private static bool s_installed;
    public static readonly List<string> Log = new();

    public static RecordingBridge Reset()
    {
        var bridge = new RecordingBridge();
        lock (Gate)
        {
            WebIcd.Bridge = bridge;
            if (!s_installed)
            {
                WebIcd.Install();
                IcdTestHooks.LogCapture = line => { lock (Log) Log.Add(line); };
                WebIcd.LoadShaderManifest(Resource("Shaders/manifest.json"));
                s_installed = true;
            }
            IcdTestHooks.Reset(); // once-only log keys, per-bridge stand-ins, readbacks in flight, violations, device-lost state
            lock (Log) Log.Clear();
        }
        return bridge;
    }

    public static string[] Errors()
    {
        lock (Log) return Log.Where(l => l.StartsWith("ERROR", StringComparison.Ordinal)).ToArray();
    }

    public static byte[] Resource(string name)
    {
        using var stream = typeof(TestIcd).Assembly.GetManifestResourceStream(name) ?? throw new FileNotFoundException(name);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    public static string RepoRoot => typeof(TestIcd).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepoRoot").Value!;
}
