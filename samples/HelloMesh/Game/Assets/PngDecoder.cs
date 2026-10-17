using System.Buffers.Binary;
using System.IO.Compression;

namespace HelloMesh.Assets;

/// <summary>Minimal managed PNG decoder: 8-bit gray, gray+alpha, RGB and RGBA, non-interlaced.</summary>
public static class PngDecoder
{
    private static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Decodes <paramref name="png"/> to RGBA8; throws <see cref="NotSupportedException"/> for other PNG flavours.</summary>
    public static GlbImage Decode(ReadOnlySpan<byte> png)
    {
        if (!png.StartsWith(Signature))
            throw new NotSupportedException("Image is not a PNG.");

        int width = 0, height = 0, channels = 0;
        var header = false;
        using var compressed = new MemoryStream();
        for (var offset = Signature.Length; offset + 12 <= png.Length;)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png[offset..]);
            var type = png.Slice(offset + 4, 4);
            if (length < 0 || offset + 12 + length > png.Length)
                throw new InvalidDataException("PNG chunk exceeds the file length.");
            var data = png.Slice(offset + 8, length);
            if (type.SequenceEqual("IHDR"u8))
            {
                (width, height, channels) = ParseHeader(data);
                header = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
            offset += 12 + length;
        }
        if (!header || compressed.Length == 0)
            throw new InvalidDataException("PNG is missing its header or image data.");

        var stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        compressed.Position = 0;
        using (var inflate = new ZLibStream(compressed, CompressionMode.Decompress))
            inflate.ReadExactly(raw);

        Unfilter(raw, stride, height, channels);
        return new GlbImage(width, height, ToRgba(raw, width, height, channels));
    }

    private static (int Width, int Height, int Channels) ParseHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < 13)
            throw new InvalidDataException("PNG header is truncated.");
        var width = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
        var height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        var (bitDepth, colorType, interlace) = (data[8], data[9], data[12]);
        if (bitDepth != 8)
            throw new NotSupportedException($"PNG bit depth {bitDepth} is not supported (8 only).");
        if (interlace != 0)
            throw new NotSupportedException("Interlaced PNGs are not supported.");
        if (width <= 0 || height <= 0)
            throw new InvalidDataException("PNG has an invalid size.");
        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => throw new NotSupportedException($"PNG colour type {colorType} is not supported."),
        };
        return (width, height, channels);
    }

    private static void Unfilter(byte[] raw, int stride, int height, int bpp)
    {
        for (var y = 0; y < height; y++)
        {
            var row = y * (stride + 1);
            var filter = raw[row];
            var current = raw.AsSpan(row + 1, stride);
            var previous = y == 0 ? default : raw.AsSpan(row - stride, stride);
            for (var x = 0; x < stride; x++)
            {
                int left = x >= bpp ? current[x - bpp] : 0;
                int up = y > 0 ? previous[x] : 0;
                int upLeft = y > 0 && x >= bpp ? previous[x - bpp] : 0;
                current[x] += filter switch
                {
                    0 => (byte)0,
                    1 => (byte)left,
                    2 => (byte)up,
                    3 => (byte)((left + up) / 2),
                    4 => (byte)Paeth(left, up, upLeft),
                    _ => throw new InvalidDataException($"Invalid PNG filter type {filter}."),
                };
            }
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var (pa, pb, pc) = (Math.Abs(p - a), Math.Abs(p - b), Math.Abs(p - c));
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static byte[] ToRgba(byte[] raw, int width, int height, int channels)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var source = raw.AsSpan(y * (width * channels + 1) + 1, width * channels);
            var target = rgba.AsSpan(y * width * 4, width * 4);
            for (var x = 0; x < width; x++)
            {
                var pixel = source.Slice(x * channels, channels);
                var (r, g, b, a) = channels switch
                {
                    1 => (pixel[0], pixel[0], pixel[0], (byte)255),
                    2 => (pixel[0], pixel[0], pixel[0], pixel[1]),
                    3 => (pixel[0], pixel[1], pixel[2], (byte)255),
                    _ => (pixel[0], pixel[1], pixel[2], pixel[3]),
                };
                target[x * 4] = r;
                target[x * 4 + 1] = g;
                target[x * 4 + 2] = b;
                target[x * 4 + 3] = a;
            }
        }
        return rgba;
    }
}
