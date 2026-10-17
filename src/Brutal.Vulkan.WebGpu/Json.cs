using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// Builds the JSON descriptors handed to the bridge (object creation only, never per frame). Trim-safe: plain
/// <see cref="Utf8JsonWriter"/>, no serializer reflection.
/// </summary>
internal sealed class Json : IDisposable
{
    private readonly ArrayBufferWriter<byte> _buffer = new(512);

    public Json() => W = new Utf8JsonWriter(_buffer);

    public Utf8JsonWriter W { get; }

    /// <summary>Finishes and returns the UTF-16 JSON text.</summary>
    public override string ToString()
    {
        W.Flush();
        return Encoding.UTF8.GetString(_buffer.WrittenSpan);
    }

    public void Dispose() => W.Dispose();
}
