using System.Reflection;

namespace Brutal.Vulkan.Browser.Tests;

/// <summary>Locations injected by the test project (AssemblyMetadata): the repository and the pinned BRUTAL tree.</summary>
internal static class TestPaths
{
    private static string Meta(string key) =>
        typeof(TestPaths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == key).Value ?? "";

    public static string RepoRoot { get; } = Meta("RepoRoot");
    public static string BrutalSourceRoot { get; } = Meta("BrutalSourceRoot");
    public static string PatchesDir => Path.Combine(RepoRoot, "patches", "brutal");
    public static string PatchedDir => Path.Combine(RepoRoot, "src", "Brutal.Vulkan.Browser", "Patched");
}
