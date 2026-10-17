namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// Checks for the WebGPU-only rules of the web profile that Vulkan's validation layers cannot see
/// (docs/research/webgpu/web-vulkan-profile-proposal.md, "Desktop validation workflow" step 4): 256-byte dynamic offsets,
/// 4-byte sizes/offsets, at most 8 vertex buffers and 4 bind groups, positive viewport heights, STORE for attachments that
/// may be read later, no memory aliasing, no blocking waits on readback fences. Active when
/// <see cref="WebIcdOptions.ValidateProfile"/> is set; each rule is reported once per Vulkan function, as a warning (the call
/// proceeds) or an error (the call fails or the command is dropped).
/// </summary>
internal static class WebProfileLint
{
    private const long KindLint = 3;

    public static bool Enabled => WebIcd.Options.ValidateProfile;

    /// <summary>A violation the ICD can survive (WARN, once per function and rule).</summary>
    public static void Report(string function, string rule, string detail)
    {
        if (!Enabled || !IcdLog.First(function, rule, KindLint))
            return;
        Record(function, rule);
        IcdLog.Warn($"{function}: web profile rule \"{rule}\": {detail}");
    }

    /// <summary>A violation WebGPU rejects (ERROR, once per function and rule); reported even without ValidateProfile.</summary>
    public static void Fail(string function, string rule, string detail)
    {
        if (!IcdLog.First(function, rule, KindLint))
            return;
        Record(function, rule);
        IcdLog.Error($"{function}: web profile rule \"{rule}\": {detail}");
    }

    public static bool Aligned(ulong value, ulong alignment) => (value & (alignment - 1)) == 0;

    /// <summary>Dynamic uniform/storage offsets must be multiples of 256 (WebGPU min*BufferOffsetAlignment).</summary>
    public static bool CheckDynamicOffset(string function, uint offset)
    {
        if (Aligned(offset, 256))
            return true;
        Fail(function, "dynamic offsets are multiples of 256", $"dynamic offset {offset}");
        return false;
    }

    /// <summary>Copy sizes and offsets, vertex-buffer offsets and fill sizes must be multiples of 4.</summary>
    public static bool CheckAligned4(string function, string what, ulong value)
    {
        if (Aligned(value, 4))
            return true;
        Fail(function, "buffer offsets and sizes are multiples of 4", $"{what} = {value}");
        return false;
    }

    private static void Record(string function, string rule)
    {
        var violations = IcdTestHooks.Violations;
        lock (violations)
            violations.Add(function + ": " + rule);
    }
}
