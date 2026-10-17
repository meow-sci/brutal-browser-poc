using Xunit.Abstractions;

namespace Brutal.Vulkan.Browser.Tests;

/// <summary>
/// patches/brutal/*.patch are unified diffs against the pinned BRUTAL tree (commit a25cdf8), generated from the files under
/// src/Brutal.Vulkan.Browser/Patched/ by patches/brutal/make-patches.sh. These tests keep the three in sync. They need
/// git and the BRUTAL checkout (BrutalSourceRoot) and report what they skipped otherwise.
/// </summary>
public class PatchTests(ITestOutputHelper output)
{
    // Patched/<path> replaces BRUTAL/Brutal.Vulkan/<path>.
    private static readonly string[] PatchedFiles = ["ImportResolver.cs", "Generated/PInvoke/VulkanApi/VulkanApiEx.gen.cs"];
    private static readonly string[] Patches =
        ["0001-pluggable-loader-import-resolver.patch", "0002-pluggable-loader-global-commands.patch", "brutal-vulkan-pluggable-loader.patch"];

    private static string Upstream(string relative) => Path.Combine(TestPaths.BrutalSourceRoot, "Brutal.Vulkan", relative);
    private static string Patch(string name) => Path.Combine(TestPaths.PatchesDir, name);

    private bool HaveBrutalTree()
    {
        var present = File.Exists(Upstream(PatchedFiles[0]));
        if (!present) output.WriteLine($"skipped: BRUTAL tree not found at {TestPaths.BrutalSourceRoot}");
        return present;
    }

    [Fact]
    public void PatchedDirectory_HoldsExactlyTheFilesBeingReplaced()
    {
        var actual = Directory.EnumerateFiles(TestPaths.PatchedDir, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(TestPaths.PatchedDir, f).Replace('\\', '/')).Order().ToArray();
        Assert.Equal(PatchedFiles.Order().ToArray(), actual);
    }

    [Fact]
    public void PatchedFiles_ReplaceExistingUpstreamFiles()
    {
        if (!HaveBrutalTree()) return;
        Assert.All(PatchedFiles, f => Assert.True(File.Exists(Upstream(f)), $"{f} does not exist upstream"));
    }

    [Fact]
    public void CombinedPatch_IsTheConcatenationOfTheNumberedPatches()
    {
        Assert.Equal(File.ReadAllText(Patch(Patches[0])) + File.ReadAllText(Patch(Patches[1])), File.ReadAllText(Patch(Patches[2])));
    }

    [Theory]
    [InlineData("0001-pluggable-loader-import-resolver.patch")]
    [InlineData("0002-pluggable-loader-global-commands.patch")]
    [InlineData("brutal-vulkan-pluggable-loader.patch")]
    public void Patch_PassesGitApplyCheck_AgainstThePinnedBrutalTree(string patch)
    {
        if (!HaveBrutalTree()) return;
        // `git apply --check` only reads: the BRUTAL tree is never modified.
        var result = ProcessRunner.Run("git", ["-C", TestPaths.BrutalSourceRoot, "apply", "--check", Patch(patch)]);
        if (result is null) { output.WriteLine("skipped: git not installed"); return; }
        Assert.True(result.Value.ExitCode == 0, result.Value.Output);
    }

    [Fact]
    public void AppliedPatch_ReproducesThePatchedSources_ByteForByte()
    {
        if (!HaveBrutalTree()) return;
        var scratch = Directory.CreateTempSubdirectory("brutal-patch-");
        try
        {
            foreach (var f in PatchedFiles)
            {
                var target = Path.Combine(scratch.FullName, "Brutal.Vulkan", f);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Upstream(f), target);
            }
            var result = ProcessRunner.Run("git", ["apply", Patch(Patches[2])], scratch.FullName);
            if (result is null) { output.WriteLine("skipped: git not installed"); return; }
            Assert.True(result.Value.ExitCode == 0, result.Value.Output);
            foreach (var f in PatchedFiles)
                Assert.True(File.ReadAllBytes(Path.Combine(scratch.FullName, "Brutal.Vulkan", f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(TestPaths.PatchedDir, f))),
                    $"{f}: upstream + patch differs from src/Brutal.Vulkan.Browser/Patched/{f}; run patches/brutal/make-patches.sh");
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }
}
