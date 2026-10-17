using System.Buffers.Binary;
using Brutal.Numerics;

namespace HelloMesh.Assets;

/// <summary>Element layout of an accessor as it sits in its buffer view.</summary>
internal readonly record struct AccessorLayout(int Count, int ComponentType, string Type, int ByteStride);

/// <summary>Stride-aware copies of glTF accessor data into BRUTAL vector arrays (used by <see cref="ModelLoader"/>).</summary>
internal static class AccessorData
{
    private const int Byte = 5121, UnsignedShort = 5123, UnsignedInt = 5125, Float = 5126;

    public static float3[] ReadFloat3(ReadOnlySpan<byte> data, AccessorLayout layout)
    {
        var stride = Prepare(data, layout, Float, "VEC3", 12);
        var result = new float3[layout.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var element = data[(i * stride)..];
            result[i] = new float3(BinaryPrimitives.ReadSingleLittleEndian(element), BinaryPrimitives.ReadSingleLittleEndian(element[4..]), BinaryPrimitives.ReadSingleLittleEndian(element[8..]));
        }
        return result;
    }

    public static float2[] ReadFloat2(ReadOnlySpan<byte> data, AccessorLayout layout)
    {
        var stride = Prepare(data, layout, Float, "VEC2", 8);
        var result = new float2[layout.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var element = data[(i * stride)..];
            result[i] = new float2(BinaryPrimitives.ReadSingleLittleEndian(element), BinaryPrimitives.ReadSingleLittleEndian(element[4..]));
        }
        return result;
    }

    public static uint[] ReadIndices(ReadOnlySpan<byte> data, AccessorLayout layout)
    {
        var size = layout.ComponentType switch
        {
            Byte => 1,
            UnsignedShort => 2,
            UnsignedInt => 4,
            _ => throw new NotSupportedException($"Index component type {layout.ComponentType} is not supported."),
        };
        var stride = Prepare(data, layout, layout.ComponentType, "SCALAR", size);
        var result = new uint[layout.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var element = data[(i * stride)..];
            result[i] = size switch
            {
                1 => element[0],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(element),
                _ => BinaryPrimitives.ReadUInt32LittleEndian(element),
            };
        }
        return result;
    }

    public static uint[] Sequence(int count) => Enumerable.Range(0, count).Select(i => (uint)i).ToArray();

    private static int Prepare(ReadOnlySpan<byte> data, AccessorLayout layout, int componentType, string type, int elementSize)
    {
        if (layout.ComponentType != componentType || layout.Type != type)
            throw new NotSupportedException($"Unsupported accessor {type}/{layout.ComponentType} (expected {componentType}).");
        var stride = layout.ByteStride == 0 ? elementSize : layout.ByteStride;
        if (layout.Count > 0 && (long)(layout.Count - 1) * stride + elementSize > data.Length)
            throw new InvalidDataException("Accessor exceeds its buffer view.");
        return stride;
    }
}
