using Brutal.OpenAlApi;
using HelloMesh.Audio;
using NVorbis;

namespace HelloMesh;

/// <summary>Desktop <see cref="IAudio"/>: NVorbis decodes the Ogg to 16-bit PCM, OpenAL plays it from one source.</summary>
public sealed class OpenAlAudio : IAudio, IDisposable
{
    private Device? _device;
    private Context? _context;
    private AlBuffer _buffer;
    private AlSource _source;
    private bool _loaded;

    public Task LoadAsync(ReadOnlyMemory<byte> oggBytes)
    {
        try
        {
            var (pcm, channels, sampleRate) = Decode(oggBytes);
            _device = Alc.OpenDevice();
            _context = _device.CreateContext();
            _context.MakeCurrent();
            _buffer = Al.GenBuffer();
            _buffer.SetData(channels == 1 ? AlFormat.Mono16 : AlFormat.Stereo16, pcm, sampleRate);
            _source = Al.GenSource();
            _source.Buffer = _buffer;
            Al.ThrowIfError();
            _loaded = true;
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }

    public void PlayOneShot()
    {
        if (!_loaded)
            throw new InvalidOperationException("Sound is not loaded.");
        _source.Stop();
        _source.Play();
        Al.ThrowIfError();
    }

    private static (byte[] Pcm, int Channels, int SampleRate) Decode(ReadOnlyMemory<byte> oggBytes)
    {
        using var stream = new MemoryStream(oggBytes.ToArray());
        using var reader = new VorbisReader(stream);
        if (reader.Channels is < 1 or > 2)
            throw new NotSupportedException($"Unsupported channel count {reader.Channels}.");
        var samples = new float[reader.TotalSamples * reader.Channels];
        var count = 0;
        while (count < samples.Length)
        {
            var read = reader.ReadSamples(samples.AsSpan(count));
            if (read == 0)
                break;
            count += read;
        }
        var pcm = new byte[count * sizeof(short)];
        for (var i = 0; i < count; i++)
            BitConverter.TryWriteBytes(pcm.AsSpan(i * sizeof(short)), (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue));
        return (pcm, reader.Channels, reader.SampleRate);
    }

    public void Dispose()
    {
        if (_loaded)
        {
            _source.Stop();
            Al.DeleteSources([_source]);
            Al.DeleteBuffers([_buffer]);
        }
        _context?.Dispose();
        _device?.Dispose();
    }
}
