using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Brutal.Vulkan.WebGpu;

/// <summary>
/// A growable, unmanaged (wasm heap) stream of replay records (<see cref="Op"/>). Command buffers record into one each;
/// <c>vkQueueSubmit</c> concatenates them and hands pointer + length to <c>replayCommandStream</c> (one JS call). Appending
/// is allocation-free once the buffer has grown to its working size. Records never contain pointers into a stream (inline
/// data is addressed relative to the record), so streams can be copied freely.
/// </summary>
internal sealed unsafe class CommandStream : IDisposable
{
    private uint* _words;
    private int _capacity; // in words
    private int _length;   // in words

    public CommandStream(int initialBytes = 1024)
    {
        _capacity = Math.Max(16, initialBytes / 4);
        _words = (uint*)NativeMemory.Alloc((nuint)_capacity * 4);
    }

    /// <summary>Address of the first word (changes when the stream grows).</summary>
    public nint Pointer => (nint)_words;

    public int ByteLength => _length * 4;

    public int WordLength => _length;

    public ReadOnlySpan<uint> Words => new(_words, _length);

    public void Reset() => _length = 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint* Reserve(int words)
    {
        if (_length + words > _capacity)
            Grow(_length + words);
        var p = _words + _length;
        _length += words;
        return p;
    }

    private void Grow(int required)
    {
        var capacity = _capacity;
        while (capacity < required)
            capacity *= 2;
        _words = (uint*)NativeMemory.Realloc(_words, (nuint)capacity * 4);
        _capacity = capacity;
    }

    /// <summary>Starts a record of <paramref name="payloadWords"/> payload words; returns the payload pointer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint* Record(Op op, int payloadWords)
    {
        var total = payloadWords + 1;
        if (total > ushort.MaxValue)
            throw new InvalidOperationException($"replay record {op} too large ({total} words)");
        var p = Reserve(total);
        p[0] = (uint)op | ((uint)total << 16);
        return p + 1;
    }

    /// <summary>A record with <paramref name="fixedWords"/> payload words followed by <paramref name="byteLength"/> inline bytes.</summary>
    public uint* RecordWithData(Op op, int fixedWords, void* data, int byteLength)
    {
        var dataWords = (byteLength + 3) / 4;
        var p = Record(op, fixedWords + dataWords);
        var dst = (byte*)(p + fixedWords);
        if (byteLength > 0)
            Buffer.MemoryCopy(data, dst, byteLength, byteLength);
        for (var i = byteLength; i < dataWords * 4; i++)
            dst[i] = 0;
        return p;
    }

    /// <summary>Appends the records of another stream (vkCmdExecuteCommands, submit concatenation).</summary>
    public void Append(CommandStream other) => Append(other._words, other._length);

    public void Append(uint* words, int count)
    {
        if (count == 0)
            return;
        var dst = Reserve(count);
        Buffer.MemoryCopy(words, dst, (long)count * 4, (long)count * 4);
    }

    public void Op0(Op op) => Record(op, 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Op1(Op op, uint a)
    {
        var p = Record(op, 1);
        p[0] = a;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Op3(Op op, uint a, uint b, uint c)
    {
        var p = Record(op, 3);
        p[0] = a; p[1] = b; p[2] = c;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Op4(Op op, uint a, uint b, uint c, uint d)
    {
        var p = Record(op, 4);
        p[0] = a; p[1] = b; p[2] = c; p[3] = d;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Op5(Op op, uint a, uint b, uint c, uint d, uint e)
    {
        var p = Record(op, 5);
        p[0] = a; p[1] = b; p[2] = c; p[3] = d; p[4] = e;
    }

    public static uint F(float value) => BitConverter.SingleToUInt32Bits(value);

    public void Dispose()
    {
        if (_words != null)
        {
            NativeMemory.Free(_words);
            _words = null;
            _capacity = _length = 0;
        }
        GC.SuppressFinalize(this);
    }

    ~CommandStream()
    {
        if (_words != null)
            NativeMemory.Free(_words);
    }
}
