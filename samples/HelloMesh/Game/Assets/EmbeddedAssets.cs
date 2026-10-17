namespace HelloMesh.Assets;

/// <summary>Reads files embedded in the game assembly (models, sounds, SPIR-V) by their logical resource name.</summary>
public static class EmbeddedAssets
{
    /// <summary>Returns the bytes of the embedded resource <paramref name="logicalName"/>, e.g. <c>Assets/SunglassesKhronos.glb</c>.</summary>
    public static byte[] Read(string logicalName)
    {
        using var stream = typeof(EmbeddedAssets).Assembly.GetManifestResourceStream(logicalName)
            ?? throw new FileNotFoundException($"Embedded resource '{logicalName}' not found.");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
