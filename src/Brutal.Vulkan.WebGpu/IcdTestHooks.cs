using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Brutal.Vulkan.WebGpu.Tests")]

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// The test seams of the ICD in one place (used by <c>tests/Brutal.Vulkan.WebGpu.Tests</c> through <c>InternalsVisibleTo</c>; the
/// bridge itself is swapped through the internal <see cref="WebIcd.Bridge"/> setter). Product code only writes to
/// <see cref="LogCapture"/> and <see cref="Violations"/>; nothing here runs unless a test calls it.
/// </summary>
internal static class IcdTestHooks
{
    /// <summary>Receives every ICD log line (with its <c>ERROR</c>/<c>WARN</c>/<c>INFO [webgpu]</c> prefix) in addition to the console.</summary>
    internal static Action<string>? LogCapture;

    /// <summary>Web-profile rules reported so far, as <c>"function: rule"</c> (first occurrence of each).</summary>
    internal static readonly List<string> Violations = new();

    /// <summary>
    /// Per-test reset after a new bridge was set: once-only log keys, stand-in GPU objects and the null fragment module (they belong to
    /// the previous bridge), readbacks still in flight, recorded violations and the device-lost state.
    /// </summary>
    internal static void Reset()
    {
        IcdLog.ResetOnce();
        Icd.ResetStandIns();
        Icd.ResetNullFragment();
        Icd.ResetDeviceLost();
        Readbacks.ResetForTests();
        lock (Violations)
            Violations.Clear();
    }

    /// <summary>Forgets the adapter limits and features so that the next use re-reads them from the current bridge.</summary>
    internal static void ResetAdapter() => Profile.Reset();

    /// <summary>Decodes a replay stream (see <c>Opcodes.cs</c>) into its records: opcode and payload words, up to <c>End</c>.</summary>
    internal static List<(Op Op, uint[] Payload)> Decode(ReadOnlySpan<uint> words)
    {
        var list = new List<(Op, uint[])>();
        var i = 0;
        while (i < words.Length)
        {
            var header = words[i];
            var op = (Op)(header & 0xFFFF);
            var len = (int)(header >> 16);
            if (len == 0 || i + len > words.Length)
                throw new InvalidDataException($"malformed record at word {i}: {header:x8}");
            list.Add((op, words.Slice(i + 1, len - 1).ToArray()));
            if (op == Op.End)
                break;
            i += len;
        }
        return list;
    }
}
