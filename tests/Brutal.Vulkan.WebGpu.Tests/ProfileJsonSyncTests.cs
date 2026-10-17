using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Brutal;
using Brutal.Vulkan.WebGpu;
using Brutal.VulkanApi;

namespace WebGpuIcd.Tests;

/// <summary>
/// Keeps <c>profiles/VP_BRUTAL_web.json</c> (profile <c>VP_BRUTAL_web_poc_2026</c> = capability <c>web_core</c>) and the ICD's
/// <c>Profile.cs</c> describing the same device. The ICD is the source of truth; every test compares what the ICD answers through
/// its Vulkan entry points (against a recording bridge whose adapter has exactly the WebGPU default limits and no optional
/// features) with what the JSON says, and fails with the full list of differences when either side drifts.
/// </summary>
public sealed unsafe class ProfileJsonSyncTests : IDisposable
{
    private const string PocProfile = "VP_BRUTAL_web_poc_2026";

    private readonly RecordingBridge _bridge = TestIcd.Reset();
    private readonly TestDevice _device;
    private readonly JsonElement _root;
    private readonly Capabilities _poc;

    public ProfileJsonSyncTests()
    {
        _bridge.LimitsJson = "{}"; // an adapter at the WebGPU defaults (what the PoC tier describes)
        _bridge.FeaturesJson = "[\"core-features-and-limits\"]"; // no optional WebGPU features
        IcdTestHooks.ResetAdapter();
        _device = new TestDevice();

        var path = Path.Combine(TestIcd.RepoRoot, "profiles", "VP_BRUTAL_web.json");
        Assert.True(File.Exists(path), $"profile JSON not found at {path}");
        _root = JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
        _poc = Capabilities.Resolve(_root, PocProfile);
    }

    public void Dispose()
    {
        _device.Dispose();
        IcdTestHooks.ResetAdapter();
    }

    private VkPhysicalDevice Pd => _device.PhysicalDevice.Handle;

    // ---- api-version ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ApiVersionMatches()
    {
        int instanceVersion;
        Icd.vkEnumerateInstanceVersion(&instanceVersion);
        VkPhysicalDeviceProperties props;
        Icd.vkGetPhysicalDeviceProperties(Pd, &props);
        Assert.Equal(instanceVersion, props.ApiVersion);

        var problems = new List<string>();
        foreach (var profile in _root.GetProperty("profiles").EnumerateObject())
        {
            var json = ParseApiVersion(profile.Value.GetProperty("api-version").GetString()!);
            if (json != instanceVersion)
                problems.Add($"{profile.Name}: api-version {profile.Value.GetProperty("api-version").GetString()} but the ICD reports {FormatVersion(instanceVersion)}");
        }
        NoDifferences(problems);
        Assert.Equal(instanceVersion, ParseApiVersion(_poc.ApiVersionText));
    }

    private static int ParseApiVersion(string text)
    {
        var parts = text.Split('.').Select(int.Parse).ToArray();
        return (parts[0] << 22) | (parts[1] << 12) | (parts.Length > 2 ? parts[2] : 0);
    }

    private static string FormatVersion(int v) => $"{v >> 22}.{(v >> 12) & 0x3FF}.{v & 0xFFF}";

    // ---- limits -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void LimitsMatchTheDeviceProperties()
    {
        VkPhysicalDeviceProperties props;
        Icd.vkGetPhysicalDeviceProperties(Pd, &props);
        var json = _poc.Properties["VkPhysicalDeviceProperties"];
        var problems = new List<string>();

        CompareMembers("limits", typeof(VkPhysicalDeviceLimits), (byte*)&props.Limits, json.GetProperty("limits"), listAll: true, problems);
        if (json.TryGetProperty("sparseProperties", out var sparse))
            CompareMembers("sparseProperties", typeof(VkPhysicalDeviceSparseProperties), (byte*)&props.SparseProperties, sparse, listAll: true, problems);
        NoDifferences(problems);
    }

    [Fact]
    public void ExtensionPropertyStructsMatch()
    {
        var problems = new List<string>();
        foreach (var (name, json) in _poc.Properties)
        {
            if (name == "VkPhysicalDeviceProperties")
                continue;
            var type = FindVulkanType(name);
            if (type is null)
            {
                problems.Add($"{name}: no such struct in the bindings");
                continue;
            }
            var size = SizeOf(type);
            var buffer = stackalloc byte[size];
            new Span<byte>(buffer, size).Clear();
            ((VkBaseStructure*)buffer)->SType = StructureType(name);
            var p2 = new VkPhysicalDeviceProperties2 { Next = buffer };
            Icd.vkGetPhysicalDeviceProperties2(Pd, &p2);
            CompareMembers(name, type, buffer, json, listAll: false, problems);
        }
        NoDifferences(problems);
    }

    // ---- extensions -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ExtensionsMatchWhatTheIcdAdvertises()
    {
        var icd = new Dictionary<string, int>(StringComparer.Ordinal);
        int count;
        Icd.vkEnumerateInstanceExtensionProperties(null, &count, null);
        var instance = new VkExtensionProperties[count];
        fixed (VkExtensionProperties* p = instance)
        {
            Assert.Equal(VkResult.Success, Icd.vkEnumerateInstanceExtensionProperties(null, &count, p));
            for (var i = 0; i < count; i++)
                icd[Icd.Str(p[i].ExtensionName)] = p[i].SpecVersion;
        }
        Icd.vkEnumerateDeviceExtensionProperties(Pd, null, &count, null);
        var device = new VkExtensionProperties[count];
        fixed (VkExtensionProperties* p = device)
        {
            Assert.Equal(VkResult.Success, Icd.vkEnumerateDeviceExtensionProperties(Pd, null, &count, p));
            for (var i = 0; i < count; i++)
                icd[Icd.Str(p[i].ExtensionName)] = p[i].SpecVersion;
        }

        var problems = new List<string>();
        foreach (var (name, spec) in _poc.Extensions)
        {
            if (!icd.TryGetValue(name, out var icdSpec))
                problems.Add($"{name}: listed in the JSON but not advertised by the ICD");
            else if (icdSpec != spec)
                problems.Add($"{name}: spec version {spec} in the JSON, {icdSpec} in the ICD");
        }
        // the schema accepts registry extension names only, so the JSON cannot list the ICD's own canvas-surface extension
        foreach (var name in icd.Keys.Where(n => n != Profile.WebSurfaceExtension && !_poc.Extensions.ContainsKey(n)))
            problems.Add($"{name}: advertised by the ICD but missing from the JSON");
        NoDifferences(problems);
    }

    // ---- features ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void PhysicalDeviceFeaturesMatchInBothDirections()
    {
        VkPhysicalDeviceFeatures features;
        Icd.vkGetPhysicalDeviceFeatures(Pd, &features);
        var json = _poc.Features["VkPhysicalDeviceFeatures"];
        var problems = new List<string>();
        CompareMembers("VkPhysicalDeviceFeatures", typeof(VkPhysicalDeviceFeatures), (byte*)&features, json, listAll: true, problems);
        NoDifferences(problems);

        // the same answer through vkGetPhysicalDeviceFeatures2
        VkPhysicalDeviceFeatures2 f2 = default;
        Icd.vkGetPhysicalDeviceFeatures2(Pd, &f2);
        Assert.Equal(new ReadOnlySpan<byte>(&features, sizeof(VkPhysicalDeviceFeatures)).ToArray(),
            new ReadOnlySpan<byte>(&f2.Features, sizeof(VkPhysicalDeviceFeatures)).ToArray());
    }

    [Fact]
    public void FeatureStructsMatchInBothDirections()
    {
        var problems = new List<string>();
        foreach (var (name, json) in _poc.Features)
        {
            if (name == "VkPhysicalDeviceFeatures")
                continue;
            var type = FindVulkanType(name);
            if (type is null)
            {
                problems.Add($"{name}: no such struct in the bindings");
                continue;
            }
            var size = SizeOf(type);
            var buffer = stackalloc byte[size];
            new Span<byte>(buffer, size).Clear();
            ((VkBaseStructure*)buffer)->SType = StructureType(name);
            var f2 = new VkPhysicalDeviceFeatures2 { Next = buffer };
            Icd.vkGetPhysicalDeviceFeatures2(Pd, &f2);
            // the full Vulkan 1.1/1.2/1.3 aggregate structs must be listed member by member; extension structs only list what they carry
            CompareMembers(name, type, buffer, json, listAll: name.StartsWith("VkPhysicalDeviceVulkan1", StringComparison.Ordinal)
                || name.StartsWith("VkPhysicalDevicePortabilitySubset", StringComparison.Ordinal), problems);
        }
        // Vulkan 1.1 / 1.2 / 1.3 aggregate structs the ICD fills must all appear in the JSON
        foreach (var required in new[] { "VkPhysicalDeviceVulkan11Features", "VkPhysicalDeviceVulkan12Features", "VkPhysicalDeviceVulkan13Features" })
            if (!_poc.Features.ContainsKey(required))
                problems.Add($"{required}: reported by the ICD but missing from the JSON");
        NoDifferences(problems);
    }

    // ---- formats ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void FormatsMatchInBothDirections()
    {
        var byName = new Dictionary<string, VkFormat>(StringComparer.Ordinal);
        foreach (var f in Enum.GetValues<VkFormat>())
            byName.TryAdd(Key(Enum.GetName(f)!), f);

        var problems = new List<string>();
        var listed = new HashSet<VkFormat>();
        foreach (var (name, entry) in _poc.Formats)
        {
            if (!name.StartsWith("VK_FORMAT_", StringComparison.Ordinal) || !byName.TryGetValue(Key(name["VK_FORMAT_".Length..]), out var format))
            {
                problems.Add($"{name}: unknown format");
                continue;
            }
            listed.Add(format);
            VkFormatProperties icd;
            Icd.vkGetPhysicalDeviceFormatProperties(Pd, format, &icd);
            CompareFlags(name, "optimalTilingFeatures", icd.OptimalTilingFeatures, entry.Optimal, problems);
            CompareFlags(name, "linearTilingFeatures", icd.LinearTilingFeatures, entry.Linear, problems);
            CompareFlags(name, "bufferFeatures", icd.BufferFeatures, entry.Buffer, problems);
        }
        foreach (var format in Enum.GetValues<VkFormat>().Distinct())
        {
            if (listed.Contains(format))
                continue;
            VkFormatProperties icd;
            Icd.vkGetPhysicalDeviceFormatProperties(Pd, format, &icd);
            if ((icd.OptimalTilingFeatures | icd.LinearTilingFeatures | icd.BufferFeatures) != VkFormatFeatureFlags.None)
                problems.Add($"{format}: the ICD reports {icd.OptimalTilingFeatures} / {icd.LinearTilingFeatures} / {icd.BufferFeatures} but the JSON does not list the format");
        }
        NoDifferences(problems);
    }

    private static void CompareFlags(string format, string member, VkFormatFeatureFlags icd, string[] json, List<string> problems)
    {
        var expected = FlagsFromNames(typeof(VkFormatFeatureFlags), json);
        if (expected != (ulong)(uint)(int)icd)
            problems.Add($"{format}.{member}: JSON [{string.Join(", ", json.Select(s => s["VK_FORMAT_FEATURE_".Length..]))}] vs ICD {icd}");
    }

    // ---- queue family -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void QueueFamiliesMatch()
    {
        int count = 0;
        Icd.vkGetPhysicalDeviceQueueFamilyProperties(Pd, &count, null);
        var icd = new VkQueueFamilyProperties[count];
        fixed (VkQueueFamilyProperties* p = icd)
            Icd.vkGetPhysicalDeviceQueueFamilyProperties(Pd, &count, p);

        var problems = new List<string>();
        if (_poc.QueueFamilies.Count != icd.Length)
            problems.Add($"queue family count: JSON {_poc.QueueFamilies.Count}, ICD {icd.Length}");
        for (var i = 0; i < Math.Min(icd.Length, _poc.QueueFamilies.Count); i++)
        {
            var json = _poc.QueueFamilies[i].GetProperty("VkQueueFamilyProperties");
            var q = icd[i];
            var where = $"queueFamily[{i}]";
            var flags = FlagsFromNames(typeof(VkQueueFlags), json.GetProperty("queueFlags").EnumerateArray().Select(e => e.GetString()!).ToArray());
            if (flags != (ulong)(uint)(int)q.QueueFlags)
                problems.Add($"{where}.queueFlags: JSON {flags:x} vs ICD {q.QueueFlags}");
            if (json.GetProperty("queueCount").GetInt32() != q.QueueCount)
                problems.Add($"{where}.queueCount: JSON {json.GetProperty("queueCount")} vs ICD {q.QueueCount}");
            if (json.TryGetProperty("timestampValidBits", out var bits) && bits.GetInt32() != q.TimestampValidBits)
                problems.Add($"{where}.timestampValidBits: JSON {bits} vs ICD {q.TimestampValidBits}");
            var g = json.GetProperty("minImageTransferGranularity");
            if (g.GetProperty("width").GetInt32() != q.MinImageTransferGranularity.Width
                || g.GetProperty("height").GetInt32() != q.MinImageTransferGranularity.Height
                || g.GetProperty("depth").GetInt32() != q.MinImageTransferGranularity.Depth)
                problems.Add($"{where}.minImageTransferGranularity: JSON {g} vs ICD {q.MinImageTransferGranularity.Width}x{q.MinImageTransferGranularity.Height}x{q.MinImageTransferGranularity.Depth}");
        }
        NoDifferences(problems);
    }

    private static void NoDifferences(List<string> problems) =>
        Assert.True(problems.Count == 0, $"{problems.Count} difference(s) between profiles/VP_BRUTAL_web.json and the ICD:\n  " + string.Join("\n  ", problems));

    // ---- the comparison engine ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Compares the JSON members of <paramref name="json"/> with the same-named fields of the struct at <paramref name="memory"/>
    /// (read by field offset). Members the JSON names that the struct lacks, and (with <paramref name="listAll"/>) members of the
    /// struct the JSON does not name, are differences too.
    /// </summary>
    private static void CompareMembers(string where, Type type, byte* memory, JsonElement json, bool listAll, List<string> problems)
    {
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.GetCustomAttribute<FieldOffsetAttribute>() is not null && !IsFixedBuffer(f) && f.Name is not ("SType" or "Next"))
            .ToDictionary(f => Key(f.Name), f => f);
        var seen = new HashSet<string>();
        foreach (var member in json.EnumerateObject())
        {
            if (!fields.TryGetValue(Key(member.Name), out var field))
            {
                problems.Add($"{where}.{member.Name}: not a member of {type.Name}");
                continue;
            }
            seen.Add(Key(member.Name));
            var actual = Read(memory, field);
            string expected;
            try
            {
                expected = FromJson(member.Value, field.FieldType);
            }
            catch (Exception e) when (e is InvalidOperationException or FormatException or ArgumentException or KeyNotFoundException)
            {
                problems.Add($"{where}.{member.Name}: cannot interpret JSON {member.Value} as {field.FieldType.Name} ({e.Message})");
                continue;
            }
            if (!actual.Contains(expected))
                problems.Add($"{where}.{member.Name}: JSON {member.Value} vs ICD {actual[0]}");
        }
        if (listAll)
            foreach (var (key, field) in fields)
                if (!seen.Contains(key))
                    problems.Add($"{where}.{field.Name}: the ICD reports {Read(memory, field)[0]} but the JSON does not list it");
    }

    private static bool IsFixedBuffer(FieldInfo f) => f.GetCustomAttribute<FixedBufferAttribute>() is not null;

    /// <summary>The field's value as canonical strings (a 32-bit integer can be read signed or unsigned; the first entry is shown).</summary>
    private static string[] Read(byte* memory, FieldInfo field)
    {
        var q = memory + field.GetCustomAttribute<FieldOffsetAttribute>()!.Value;
        var t = field.FieldType;
        if (t == typeof(Bool32))
            return [*(int*)q != 0 ? "true" : "false"];
        if (t == typeof(int))
        {
            var v = *(int*)q;
            return v >= 0 ? [Num(v)] : [Num(v), Num((uint)v)];
        }
        if (t == typeof(uint))
            return [Num(*(uint*)q)];
        if (t == typeof(float))
            return [Num(*(float*)q)];
        if (t == typeof(ByteSize64) || t == typeof(nuint) || t == typeof(ulong))
            return [Num(*(ulong*)q)];
        if (t == typeof(nint) || t == typeof(long))
            return [Num(*(long*)q)];
        if (t.IsEnum)
            return [$"flags:{(ulong)(uint)*(int*)q:x}"];
        if (t.Name == "uint2")
            return [$"[{Num(((uint*)q)[0])}, {Num(((uint*)q)[1])}]"];
        if (t.Name == "uint3")
            return [$"[{Num(((uint*)q)[0])}, {Num(((uint*)q)[1])}, {Num(((uint*)q)[2])}]"];
        if (t.Name == "float2")
            return [$"[{Num(((float*)q)[0])}, {Num(((float*)q)[1])}]"];
        throw new NotSupportedException($"{field.DeclaringType!.Name}.{field.Name}: unsupported field type {t}");
    }

    private static string FromJson(JsonElement e, Type fieldType)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.True: return "true";
            case JsonValueKind.False: return "false";
            case JsonValueKind.Number: return Num(e.GetDouble());
            case JsonValueKind.Array when fieldType.IsEnum:
                return $"flags:{FlagsFromNames(fieldType, e.EnumerateArray().Select(x => x.GetString()!).ToArray()):x}";
            case JsonValueKind.Array:
                return "[" + string.Join(", ", e.EnumerateArray().Select(x => Num(x.GetDouble()))) + "]";
            default:
                throw new InvalidOperationException($"unexpected JSON {e.ValueKind}");
        }
    }

    private static string Num(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Bit mask of the JSON flag names (VK_SAMPLE_COUNT_1_BIT, VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT, ...) of a flags enum.</summary>
    private static ulong FlagsFromNames(Type enumType, string[] names)
    {
        // VkSampleCountFlags -> "VK_SAMPLE_COUNT_", VkQueueFlags -> "VK_QUEUE_", VkFormatFeatureFlags -> "VK_FORMAT_FEATURE_"
        var stem = enumType.Name["Vk".Length..];
        if (stem.EndsWith("Flags", StringComparison.Ordinal))
            stem = stem[..^"Flags".Length];
        var prefix = "VK_" + string.Concat(stem.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString())).ToUpperInvariant() + "_";
        ulong mask = 0;
        foreach (var name in names)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
                throw new ArgumentException($"{name} does not start with {prefix}");
            var member = Enum.GetNames(enumType).FirstOrDefault(n => Key(n) == Key(name[prefix.Length..]))
                ?? throw new KeyNotFoundException($"{name}: no member in {enumType.Name}");
            mask |= (ulong)(uint)Convert.ToInt32(Enum.Parse(enumType, member));
        }
        return mask;
    }

    /// <summary>Case- and underscore-insensitive member name (textureCompressionASTC_LDR = TextureCompressionASTCLDR).</summary>
    private static string Key(string name) => name.Replace("_", "").ToLowerInvariant();

    // ---- reflection over the bindings -----------------------------------------------------------------------------------

    /// <summary>The binding type of a profile struct name (the KHR/EXT suffix of a promoted struct is dropped).</summary>
    private static Type? FindVulkanType(string name)
    {
        var assembly = typeof(VkPhysicalDeviceFeatures).Assembly;
        foreach (var candidate in new[] { name, TrimSuffix(name, "KHR"), TrimSuffix(name, "EXT") })
            if (assembly.GetType("Brutal.VulkanApi." + candidate) is { } type)
                return type;
        return null;
    }

    private static VkStructureType StructureType(string name)
    {
        foreach (var candidate in new[] { name, TrimSuffix(name, "KHR"), TrimSuffix(name, "EXT") })
            if (Enum.TryParse<VkStructureType>(candidate["Vk".Length..], out var sType))
                return sType;
        throw new InvalidOperationException($"{name}: no VkStructureType");
    }

    private static string TrimSuffix(string s, string suffix) => s.EndsWith(suffix, StringComparison.Ordinal) ? s[..^suffix.Length] : s;

    private static int SizeOf(Type type) => (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(type).Invoke(null, null)!;

    // ---- the profile JSON ----------------------------------------------------------------------------------------------------

    /// <summary>The capabilities of a profile merged in order (later blocks win; a whole format entry replaces an earlier one; arrays append).</summary>
    private sealed class Capabilities
    {
        public string ApiVersionText = "";
        public readonly Dictionary<string, int> Extensions = new(StringComparer.Ordinal);
        public readonly Dictionary<string, JsonElement> Features = new(StringComparer.Ordinal);
        public readonly Dictionary<string, JsonElement> Properties = new(StringComparer.Ordinal);
        public readonly Dictionary<string, FormatEntry> Formats = new(StringComparer.Ordinal);
        public readonly List<JsonElement> QueueFamilies = new();

        public sealed record FormatEntry(string[] Optimal, string[] Linear, string[] Buffer);

        public static Capabilities Resolve(JsonElement root, string profileName)
        {
            var profile = root.GetProperty("profiles").GetProperty(profileName);
            var result = new Capabilities { ApiVersionText = profile.GetProperty("api-version").GetString()! };
            foreach (var capability in profile.GetProperty("capabilities").EnumerateArray())
            {
                var block = root.GetProperty("capabilities").GetProperty(capability.GetString()!);
                if (block.TryGetProperty("extensions", out var extensions))
                    foreach (var e in extensions.EnumerateObject())
                        result.Extensions[e.Name] = e.Value.GetInt32();
                if (block.TryGetProperty("features", out var features))
                    foreach (var f in features.EnumerateObject())
                        result.Features[f.Name] = Merge(result.Features.GetValueOrDefault(f.Name), f.Value);
                if (block.TryGetProperty("properties", out var properties))
                    foreach (var p in properties.EnumerateObject())
                        result.Properties[p.Name] = Merge(result.Properties.GetValueOrDefault(p.Name), p.Value);
                if (block.TryGetProperty("formats", out var formats))
                    foreach (var f in formats.EnumerateObject())
                    {
                        var props = f.Value.GetProperty("VkFormatProperties");
                        string[] List(string name) => props.TryGetProperty(name, out var a) ? a.EnumerateArray().Select(x => x.GetString()!).ToArray() : [];
                        result.Formats[f.Name] = new FormatEntry(List("optimalTilingFeatures"), List("linearTilingFeatures"), List("bufferFeatures"));
                    }
                if (block.TryGetProperty("queueFamiliesProperties", out var queues))
                    result.QueueFamilies.AddRange(queues.EnumerateArray().Select(q => q.Clone()));
            }
            return result;
        }

        /// <summary>Object overlay (recursive for nested objects such as <c>limits</c>); the later value replaces everything else.</summary>
        private static JsonElement Merge(JsonElement earlier, JsonElement later)
        {
            if (earlier.ValueKind != JsonValueKind.Object || later.ValueKind != JsonValueKind.Object)
                return later.Clone();
            var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var m in earlier.EnumerateObject())
                merged[m.Name] = m.Value;
            foreach (var m in later.EnumerateObject())
                merged[m.Name] = merged.TryGetValue(m.Name, out var old) ? Merge(old, m.Value) : m.Value;
            var sb = new StringBuilder("{");
            sb.AppendJoin(',', merged.Select(kv => JsonSerializer.Serialize(kv.Key) + ":" + kv.Value.GetRawText()));
            return JsonDocument.Parse(sb.Append('}').ToString()).RootElement.Clone();
        }
    }
}
