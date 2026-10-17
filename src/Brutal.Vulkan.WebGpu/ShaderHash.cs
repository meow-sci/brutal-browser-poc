using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// SHA-256 of SPIR-V modules (manifest keys). Uses <see cref="SHA256.HashData(ReadOnlySpan{byte}, Span{byte})"/>; if the
/// platform throws <see cref="PlatformNotSupportedException"/> a small managed FIPS 180-4 implementation is used instead
/// (the browser's <c>crypto.subtle</c> is asynchronous and cannot back the synchronous <c>vkCreateShaderModule</c>).
/// </summary>
internal static class ShaderHash
{
    private static bool s_useFallback;

    public static string Hex(ReadOnlySpan<byte> data)
    {
        Span<byte> hash = stackalloc byte[32];
        Compute(data, hash);
        return Convert.ToHexStringLower(hash);
    }

    public static void Compute(ReadOnlySpan<byte> data, Span<byte> hash)
    {
        if (!s_useFallback)
        {
            try
            {
                SHA256.HashData(data, hash);
                return;
            }
            catch (PlatformNotSupportedException)
            {
                s_useFallback = true;
                IcdLog.InfoOnce("sha256", "SHA256.HashData is not supported on this platform; using the managed SHA-256");
            }
        }
        ManagedSha256(data, hash);
    }

    private static readonly uint[] K =
    [
        0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
        0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
        0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
        0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
        0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
        0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
        0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
        0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
    ];

    /// <summary>Plain SHA-256 (FIPS 180-4). Internal for the test that checks it against <see cref="SHA256"/>.</summary>
    internal static void ManagedSha256(ReadOnlySpan<byte> data, Span<byte> hash)
    {
        Span<uint> h = [0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19];
        Span<uint> w = stackalloc uint[64];
        Span<byte> tail = stackalloc byte[128];
        var full = data.Length / 64;
        for (var b = 0; b < full; b++)
            Block(data.Slice(b * 64, 64), h, w);
        var rest = data[(full * 64)..];
        rest.CopyTo(tail);
        tail[rest.Length] = 0x80;
        var tailBlocks = rest.Length + 9 <= 64 ? 1 : 2;
        BinaryPrimitives.WriteUInt64BigEndian(tail.Slice(tailBlocks * 64 - 8, 8), (ulong)data.Length * 8);
        for (var b = 0; b < tailBlocks; b++)
            Block(tail.Slice(b * 64, 64), h, w);
        for (var i = 0; i < 8; i++)
            BinaryPrimitives.WriteUInt32BigEndian(hash.Slice(i * 4, 4), h[i]);
    }

    private static void Block(ReadOnlySpan<byte> block, Span<uint> h, Span<uint> w)
    {
        for (var i = 0; i < 16; i++)
            w[i] = BinaryPrimitives.ReadUInt32BigEndian(block.Slice(i * 4, 4));
        for (var i = 16; i < 64; i++)
        {
            var s0 = BitOperations.RotateRight(w[i - 15], 7) ^ BitOperations.RotateRight(w[i - 15], 18) ^ (w[i - 15] >> 3);
            var s1 = BitOperations.RotateRight(w[i - 2], 17) ^ BitOperations.RotateRight(w[i - 2], 19) ^ (w[i - 2] >> 10);
            w[i] = w[i - 16] + s0 + w[i - 7] + s1;
        }
        uint a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
        for (var i = 0; i < 64; i++)
        {
            var s1 = BitOperations.RotateRight(e, 6) ^ BitOperations.RotateRight(e, 11) ^ BitOperations.RotateRight(e, 25);
            var ch = (e & f) ^ (~e & g);
            var t1 = hh + s1 + ch + K[i] + w[i];
            var s0 = BitOperations.RotateRight(a, 2) ^ BitOperations.RotateRight(a, 13) ^ BitOperations.RotateRight(a, 22);
            var maj = (a & b) ^ (a & c) ^ (b & c);
            var t2 = s0 + maj;
            hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
        }
        h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
    }
}
