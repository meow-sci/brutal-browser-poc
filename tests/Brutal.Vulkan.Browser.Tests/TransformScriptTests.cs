using Xunit.Abstractions;

namespace Brutal.Vulkan.Browser.Tests;

/// <summary>patches/brutal/transform.py on synthetic sources: rules R1-R5, copy mode, excludes, incremental writes, stale removal.</summary>
public sealed class TransformScriptTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("brutal-transform-");
    private string Src => Path.Combine(_root.FullName, "src");
    private string Out => Path.Combine(_root.FullName, "out");

    public TransformScriptTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(Src);
    }

    public void Dispose() => _root.Delete(recursive: true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(Src, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(Out, relative));

    /// <summary>Runs `python3 -I transform.py`; null when python3 is missing.</summary>
    private (int ExitCode, string Output)? Transform(params string[] extra)
    {
        var args = new List<string> { "-I", Path.Combine(TestPaths.PatchesDir, "transform.py"), "--src", Src, "--out", Out };
        args.AddRange(extra);
        var result = ProcessRunner.Run("python3", args);
        if (result is null) _output.WriteLine("skipped: python3 not installed");
        else _output.WriteLine(result.Value.Output);
        return result;
    }

    private const string Pointers = """
        using System.Runtime.InteropServices;
        public unsafe struct S<T> where T : unmanaged
        {
            public delegate* unmanaged[Cdecl]<int, void> A;
            public delegate*  unmanaged [ Stdcall ] <int, void> B;
            public delegate* unmanaged[Cdecl, SuppressGCTransition]<int, void> C;
            public delegate* unmanaged<int, void> D;
            public delegate* managed<int, void> E;
        }
        public static class Callbacks
        {
            [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
            public static int One() => 1;
            [UnmanagedCallersOnly] public static int Two() => 2;
        }

        """;

    [Fact]
    public void ManagedFlavor_AppliesRulesR1ToR5()
    {
        Write("Pointers.cs", Pointers);
        if (Transform("--managed-fnptr") is not { } r) return;
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("R1=", r.Output);
        var text = Read("Pointers.cs");
        Assert.DoesNotContain("unmanaged[", text);
        Assert.DoesNotContain("delegate* unmanaged", text);
        Assert.DoesNotContain("UnmanagedCallersOnly", text);
        Assert.Equal(4, text.Split("delegate*<int, void>").Length - 1); // A, B, C, D; E (`delegate* managed<`) is not an unmanaged pointer
        Assert.Contains("where T : unmanaged", text); // constraints are untouched
        Assert.Contains("public static int One() => 1;", text);
        Assert.Contains("public static int Two() => 2;", text);
    }

    [Fact]
    public void ManagedFlavor_NoStripUco_KeepsAttributesAndFailsOnNothing()
    {
        Write("Pointers.cs", Pointers);
        if (Transform("--managed-fnptr", "--no-strip-uco") is not { } r) return;
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("UnmanagedCallersOnly", Read("Pointers.cs"));
    }

    [Fact]
    public void DefaultMode_CopiesSourcesUnchanged_IncludingBomAndCrlf()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat("// delegate* unmanaged[Cdecl]<void>\r\nclass A {}\r\n"u8.ToArray()).ToArray();
        Directory.CreateDirectory(Path.Combine(Src, "sub"));
        File.WriteAllBytes(Path.Combine(Src, "sub", "A.cs"), bytes);
        if (Transform() is not { } r) return;
        Assert.Equal(0, r.ExitCode);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(Out, "sub", "A.cs")));
    }

    [Fact]
    public void ManagedFlavor_PreservesBomAndCrlf()
    {
        var original = new byte[] { 0xEF, 0xBB, 0xBF }.Concat("delegate* unmanaged[Cdecl]<void> f;\r\n"u8.ToArray()).ToArray();
        File.WriteAllBytes(Path.Combine(Src, "A.cs"), original);
        if (Transform("--managed-fnptr") is not { } r) return;
        Assert.Equal(0, r.ExitCode);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }.Concat("delegate*<void> f;\r\n"u8.ToArray()), File.ReadAllBytes(Path.Combine(Out, "A.cs")));
    }

    [Fact]
    public void Exclude_SkipsFilesThatHaveAPatchedReplacement()
    {
        Write("ImportResolver.cs", "class Original {}");
        Write("Generated/Deep/File.gen.cs", "class Deep {}");
        Write("Keep.cs", "class Keep {}");
        if (Transform("--exclude", "ImportResolver.cs", "--exclude", "Generated/Deep/File.gen.cs") is not { } r) return;
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("2 excluded", r.Output);
        Assert.False(File.Exists(Path.Combine(Out, "ImportResolver.cs")));
        Assert.False(File.Exists(Path.Combine(Out, "Generated", "Deep", "File.gen.cs")));
        Assert.True(File.Exists(Path.Combine(Out, "Keep.cs")));
        // ';' separated lists are accepted as well (MSBuild passes them that way).
        Write("Other.cs", "class Other {}");
        Assert.Equal(0, Transform("--exclude", "ImportResolver.cs;Other.cs")!.Value.ExitCode);
        Assert.False(File.Exists(Path.Combine(Out, "Other.cs")));
    }

    [Fact]
    public void SecondRun_WritesNothing_ChangedSourceIsRewritten_RemovedSourceIsPruned()
    {
        Write("A.cs", "delegate* unmanaged[Cdecl]<void> a;");
        Write("B.cs", "class B {}");
        if (Transform("--managed-fnptr") is not { } first) return;
        Assert.Contains("2 files (2 written, 0 unchanged", first.Output);
        var stamp = File.GetLastWriteTimeUtc(Path.Combine(Out, "B.cs"));

        var second = Transform("--managed-fnptr")!.Value;
        Assert.Contains("(0 written, 2 unchanged", second.Output);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(Path.Combine(Out, "B.cs")));

        Write("A.cs", "delegate* unmanaged[Cdecl]<int> a;");
        Assert.Contains("(1 written, 1 unchanged", Transform("--managed-fnptr")!.Value.Output);
        Assert.Equal("delegate*<int> a;", Read("A.cs"));

        File.Delete(Path.Combine(Src, "B.cs"));
        Assert.Contains("1 stale removed", Transform("--managed-fnptr")!.Value.Output);
        Assert.False(File.Exists(Path.Combine(Out, "B.cs")));
    }

    [Fact]
    public void OutputInsideSource_IsRejected_AndMissingSourceIsRejected()
    {
        Write("A.cs", "class A {}");
        var inside = ProcessRunner.Run("python3", ["-I", Path.Combine(TestPaths.PatchesDir, "transform.py"), "--src", Src, "--out", Path.Combine(Src, "out")]);
        if (inside is null) { _output.WriteLine("skipped: python3 not installed"); return; }
        Assert.Equal(2, inside.Value.ExitCode);
        var missing = ProcessRunner.Run("python3", ["-I", Path.Combine(TestPaths.PatchesDir, "transform.py"), "--src", Path.Combine(_root.FullName, "nope"), "--out", Out])!.Value;
        Assert.Equal(2, missing.ExitCode);
    }

    [Fact]
    public void PinnedBrutalVulkan_TransformsWithoutLeftovers_AndMatchesTheDocumentedRuleCounts()
    {
        var src = Path.Combine(TestPaths.BrutalSourceRoot, "Brutal.Vulkan");
        if (!Directory.Exists(src)) { _output.WriteLine($"skipped: {src} not found"); return; }
        var result = ProcessRunner.Run("python3", ["-I", Path.Combine(TestPaths.PatchesDir, "transform.py"), "--src", src, "--out", Out, "--managed-fnptr",
            "--exclude", "ImportResolver.cs", "--exclude", "Generated/PInvoke/VulkanApi/VulkanApiEx.gen.cs"]);
        if (result is null) { _output.WriteLine("skipped: python3 not installed"); return; }
        _output.WriteLine(result.Value.Output);
        Assert.Equal(0, result.Value.ExitCode);
        // a25cdf8: 2442 `delegate* unmanaged[Cdecl]<` in Brutal.Vulkan, one of them in the excluded ImportResolver.cs.
        Assert.Contains("R1=2441", result.Value.Output);
        Assert.Contains("leftovers=0", result.Value.Output);
    }
}
