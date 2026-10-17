using System.Text.Json;
using System.Text.RegularExpressions;

namespace Brutal.Vulkan.WebGpu;

/// <summary>How a shader receives Vulkan push constants (manifest <c>push.mode</c>, docs/architecture.md section 6).</summary>
internal enum PushMode
{
    None,
    /// <summary><c>@group(3) @binding(0) var&lt;uniform&gt;</c>: served from the ICD's push-constant ring.</summary>
    Uniform,
    /// <summary><c>@group(3) @binding(0) var&lt;storage, read&gt;</c>: same ring, storage binding.</summary>
    Storage,
    /// <summary>Any other <c>push.mode</c> (e.g. WebGPU immediates): <c>vkCreateShaderModule</c> fails. tools/build-shaders.mjs never emits one.</summary>
    Unsupported,
}

/// <summary>A resource binding declared by a WGSL module, as reflected from its source text.</summary>
internal sealed class WgslBinding
{
    public int Group, Binding;
    /// <summary><c>uniform</c>, <c>storage</c>, <c>read-only-storage</c>, <c>texture</c>, <c>sampler</c>, <c>comparison</c>, <c>storageTexture</c>.</summary>
    public string Kind = "";
    /// <summary>For textures: <c>float</c>, <c>depth</c>, <c>sint</c>, <c>uint</c>.</summary>
    public string SampleType = "float";
    /// <summary>For textures: <c>1d</c>, <c>2d</c>, <c>2d-array</c>, <c>cube</c>, <c>cube-array</c>, <c>3d</c>.</summary>
    public string ViewDimension = "2d";
    public bool Multisampled;
    /// <summary>For storage textures: texel format and access (<c>write-only</c>, <c>read-only</c>, <c>read-write</c>).</summary>
    public string? StorageFormat;
    public string StorageAccess = "write-only";
}

/// <summary>One manifest entry: the WGSL that replaces a SPIR-V module.</summary>
internal sealed class ShaderEntry
{
    public string Hash = "", Name = "", Stage = "", EntryPoint = "main", Wgsl = "";
    public PushMode Push;
    public int PushSize;
    public WgslBinding[] Bindings = [];
    /// <summary>Specialization constant id → WGSL type (<c>bool</c>, <c>i32</c>, <c>u32</c>, <c>f32</c>, <c>f16</c>).</summary>
    public Dictionary<int, string> Overrides = new();
}

/// <summary>
/// The shader manifest (<c>Shaders/manifest.json</c>, docs/architecture.md section 6): SHA-256 of the exact SPIR-V bytes →
/// WGSL text plus metadata. Binding details WebGPU needs but Vulkan layouts lack (texture sample type and dimension, comparison
/// samplers, storage access) are reflected from the WGSL declarations.
/// </summary>
internal static partial class ShaderManifest
{
    private static readonly Dictionary<string, ShaderEntry> s_entries = new(StringComparer.Ordinal);

    public static int Count => s_entries.Count;

    public static int CombinedSamplerOffset { get; private set; } = 500;
    public static int PushConstantGroup { get; private set; } = 3;
    public static int PushConstantBinding { get; private set; }

    public static bool TryGet(string hash, out ShaderEntry entry) => s_entries.TryGetValue(hash, out entry!);

    public static void Load(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.TryGetProperty("version", out var version) && version.GetInt32() != 1)
            IcdLog.Warn($"shader manifest version {version.GetInt32()} (expected 1)");
        if (root.TryGetProperty("conventions", out var conv))
        {
            if (conv.TryGetProperty("combinedSamplerOffset", out var o)) CombinedSamplerOffset = o.GetInt32();
            if (conv.TryGetProperty("pushConstantGroup", out var g)) PushConstantGroup = g.GetInt32();
            if (conv.TryGetProperty("pushConstantBinding", out var b)) PushConstantBinding = b.GetInt32();
        }
        if (PushConstantGroup != 3 || PushConstantBinding != 0)
            IcdLog.Warn($"shader manifest: push constants at group {PushConstantGroup} binding {PushConstantBinding}; this ICD binds group 3 binding 0");

        if (!root.TryGetProperty("shaders", out var shaders) || shaders.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("shader manifest: missing \"shaders\" object");

        foreach (var property in shaders.EnumerateObject())
        {
            var e = property.Value;
            var entry = new ShaderEntry
            {
                Hash = property.Name.ToLowerInvariant(),
                Name = Str(e, "name") ?? property.Name,
                Stage = Str(e, "stage") ?? "",
                EntryPoint = Str(e, "entryPoint") ?? "main",
                Wgsl = Str(e, "wgsl") ?? throw new InvalidDataException($"shader manifest: entry {property.Name} has no wgsl"),
            };
            if (e.TryGetProperty("push", out var push))
            {
                entry.Push = (Str(push, "mode") ?? "none") switch
                {
                    "uniform" => PushMode.Uniform,
                    "storage" => PushMode.Storage,
                    "none" => PushMode.None,
                    _ => PushMode.Unsupported,
                };
                if (push.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number)
                    entry.PushSize = size.GetInt32();
            }
            entry.Bindings = Reflect(entry.Wgsl);
            foreach (Match m in OverrideRegex().Matches(entry.Wgsl))
                entry.Overrides[int.Parse(m.Groups[1].ValueSpan)] = m.Groups[2].Value;
            // manifest form: "overrides": { "<SpecId>": { "name": "<wgsl name>", "type": "f32" } }
            if (e.TryGetProperty("overrides", out var overrides) && overrides.ValueKind == JsonValueKind.Object)
                foreach (var o in overrides.EnumerateObject())
                    if (int.TryParse(o.Name, out var id) && o.Value.ValueKind == JsonValueKind.Object && Str(o.Value, "type") is { } t)
                        entry.Overrides[id] = t;
            s_entries[entry.Hash] = entry;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Reflects <c>@group(g) @binding(b) var&lt;space&gt; name: type;</c> declarations.</summary>
    internal static WgslBinding[] Reflect(string wgsl)
    {
        var list = new List<WgslBinding>();
        foreach (Match m in BindingRegex().Matches(wgsl))
        {
            var b = new WgslBinding { Group = int.Parse(m.Groups[1].ValueSpan), Binding = int.Parse(m.Groups[2].ValueSpan) };
            var space = m.Groups[3].Success ? m.Groups[3].Value.Trim('<', '>').Replace(" ", "") : "";
            var type = m.Groups[4].Value.Trim();
            if (space.StartsWith("uniform", StringComparison.Ordinal))
                b.Kind = "uniform";
            else if (space.StartsWith("storage", StringComparison.Ordinal))
                b.Kind = space.Contains("read_write", StringComparison.Ordinal) || space == "storage" ? "storage" : "read-only-storage";
            else if (type.StartsWith("sampler_comparison", StringComparison.Ordinal))
                b.Kind = "comparison";
            else if (type.StartsWith("sampler", StringComparison.Ordinal))
                b.Kind = "sampler";
            else if (type.StartsWith("texture_storage_", StringComparison.Ordinal))
            {
                b.Kind = "storageTexture";
                b.ViewDimension = Dimension(type["texture_storage_".Length..]);
                var args = type[(type.IndexOf('<') + 1)..type.LastIndexOf('>')].Split(',', StringSplitOptions.TrimEntries);
                b.StorageFormat = args[0];
                b.StorageAccess = args.Length > 1 ? args[1] switch { "read" => "read-only", "read_write" => "read-write", _ => "write-only" } : "write-only";
            }
            else if (type.StartsWith("texture_", StringComparison.Ordinal))
            {
                b.Kind = "texture";
                var rest = type["texture_".Length..];
                if (rest.StartsWith("depth_", StringComparison.Ordinal))
                {
                    b.SampleType = "depth";
                    rest = rest["depth_".Length..];
                }
                if (rest.StartsWith("multisampled_", StringComparison.Ordinal))
                {
                    b.Multisampled = true;
                    rest = rest["multisampled_".Length..];
                }
                b.ViewDimension = Dimension(rest);
                if (b.SampleType != "depth")
                {
                    var lt = rest.IndexOf('<');
                    var scalar = lt >= 0 ? rest[(lt + 1)..rest.LastIndexOf('>')].Trim() : "f32";
                    b.SampleType = scalar switch { "i32" => "sint", "u32" => "uint", _ => "float" };
                }
            }
            else
                continue;
            list.Add(b);
        }
        return list.ToArray();
    }

    private static string Dimension(string t)
    {
        var end = t.IndexOf('<');
        var d = end >= 0 ? t[..end] : t;
        return d switch
        {
            "1d" => "1d",
            "2d_array" => "2d-array",
            "cube" => "cube",
            "cube_array" => "cube-array",
            "3d" => "3d",
            _ => "2d",
        };
    }

    [GeneratedRegex(@"@group\(\s*(\d+)\s*\)\s*@binding\(\s*(\d+)\s*\)\s*var\s*(<[^>]*>)?\s*[A-Za-z_]\w*\s*:\s*([^;]+);", RegexOptions.CultureInvariant)]
    private static partial Regex BindingRegex();

    [GeneratedRegex(@"@id\(\s*(\d+)\s*\)\s*override\s+[A-Za-z_]\w*\s*:\s*(\w+)", RegexOptions.CultureInvariant)]
    private static partial Regex OverrideRegex();
}
