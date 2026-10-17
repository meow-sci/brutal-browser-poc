using System.Text.RegularExpressions;
using Brutal.GlfwApi;

namespace Brutal.Browser.Tests;

/// <summary>The KeyboardEvent.code table lives in brutal-browser.js; these tests keep it consistent with <see cref="GlfwKey"/>.</summary>
public sealed partial class KeyTableTests
{
    private static readonly Lazy<List<(string Code, int Key, int Scancode)>> Table = new(Load);

    private static List<(string, int, int)> Load()
    {
        string js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "brutal-browser.js"));
        int start = js.IndexOf("const KEY_TABLE = [", StringComparison.Ordinal);
        int end = js.IndexOf("];", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "KEY_TABLE not found in brutal-browser.js");

        var rows = new List<(string, int, int)>();
        foreach (Match m in Row().Matches(js[start..end]))
            rows.Add((m.Groups[1].Value, int.Parse(m.Groups[2].Value), Convert.ToInt32(m.Groups[3].Value, 16)));
        return rows;
    }

    [GeneratedRegex(@"^\s*\['(\w+)',\s*(-?\d+),\s*0x([0-9A-Fa-f]+)\],", RegexOptions.Multiline)]
    private static partial Regex Row();

    [Fact]
    public void Table_has_unique_codes_and_only_defined_glfw_keys()
    {
        var table = Table.Value;
        Assert.True(table.Count >= 120, $"only {table.Count} rows parsed");
        Assert.Equal(table.Count, table.Select(r => r.Code).Distinct().Count());

        foreach (var (code, key, scancode) in table)
        {
            Assert.True(Enum.IsDefined((GlfwKey)key), $"{code} -> {key} is not a GlfwKey");
            Assert.NotEqual(GlfwKey.Unknown, (GlfwKey)key);
            Assert.InRange(scancode, 1, 0xFFFF);
        }
    }

    [Fact]
    public void Every_glfw_key_is_reachable_from_some_dom_code()
    {
        var mapped = Table.Value.Select(r => r.Key).ToHashSet();
        var missing = Enum.GetValues<GlfwKey>()
            .Where(k => k != GlfwKey.Unknown) // GlfwKey.Last is an alias of Menu
            .Where(k => !mapped.Contains((int)k))
            .ToList();
        Assert.True(missing.Count == 0, "unmapped GlfwKey values: " + string.Join(", ", missing));
    }

    [Theory]
    [InlineData("Space", GlfwKey.Space)]
    [InlineData("KeyA", GlfwKey.A)]
    [InlineData("KeyZ", GlfwKey.Z)]
    [InlineData("Digit0", GlfwKey.Number0)]
    [InlineData("Digit9", GlfwKey.Number9)]
    [InlineData("Quote", GlfwKey.Apostrophe)]
    [InlineData("Backquote", GlfwKey.GraveAccent)]
    [InlineData("Escape", GlfwKey.Escape)]
    [InlineData("ArrowLeft", GlfwKey.Left)]
    [InlineData("ArrowRight", GlfwKey.Right)]
    [InlineData("ArrowUp", GlfwKey.Up)]
    [InlineData("ArrowDown", GlfwKey.Down)]
    [InlineData("PageUp", GlfwKey.PageUp)]
    [InlineData("F1", GlfwKey.F1)]
    [InlineData("F12", GlfwKey.F12)]
    [InlineData("F25", GlfwKey.F25)]
    [InlineData("Numpad0", GlfwKey.Kp0)]
    [InlineData("NumpadEnter", GlfwKey.KpEnter)]
    [InlineData("NumpadDivide", GlfwKey.KpDivide)]
    [InlineData("ShiftLeft", GlfwKey.LeftShift)]
    [InlineData("ShiftRight", GlfwKey.RightShift)]
    [InlineData("ControlRight", GlfwKey.RightControl)]
    [InlineData("AltLeft", GlfwKey.LeftAlt)]
    [InlineData("MetaLeft", GlfwKey.LeftSuper)]
    [InlineData("OSRight", GlfwKey.RightSuper)]
    [InlineData("ContextMenu", GlfwKey.Menu)]
    [InlineData("IntlBackslash", GlfwKey.World1)]
    public void Known_codes_map_to_expected_keys(string code, GlfwKey expected)
    {
        var row = Table.Value.Single(r => r.Code == code);
        Assert.Equal(expected, (GlfwKey)row.Key);
    }

    [Fact]
    public void Scancodes_follow_the_dom_pk_set1_values()
    {
        var byCode = Table.Value.ToDictionary(r => r.Code, r => r.Scancode);
        Assert.Equal(0x1E, byCode["KeyA"]);
        Assert.Equal(0x39, byCode["Space"]);
        Assert.Equal(0xE04B, byCode["ArrowLeft"]);
        Assert.Equal(0xE048, byCode["ArrowUp"]);
        Assert.Equal(0x1C, byCode["Enter"]);
        Assert.Equal(0xE01C, byCode["NumpadEnter"]);
    }
}
