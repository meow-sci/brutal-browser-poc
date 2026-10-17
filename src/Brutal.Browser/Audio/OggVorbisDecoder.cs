using NVorbis;

namespace Brutal.Browser.Audio;

/// <summary>Result of a managed decode: interleaved 32-bit float PCM.</summary>
/// <param name="Samples">Interleaved samples (frame-major: L R L R ...).</param>
/// <param name="Channels">Channel count.</param>
/// <param name="SampleRate">Sample rate in Hz.</param>
public readonly record struct DecodedPcm(float[] Samples, int Channels, int SampleRate)
{
    /// <summary>Number of sample frames.</summary>
    public int Frames => Channels == 0 ? 0 : Samples.Length / Channels;

    /// <summary>Duration in seconds.</summary>
    public double Duration => SampleRate == 0 ? 0 : (double)Frames / SampleRate;
}

/// <summary>
/// Managed Ogg Vorbis decoder (NVorbis, no native code). Used by <see cref="AudioEngine"/> when the browser's
/// <c>decodeAudioData</c> cannot decode Ogg (older Safari), and directly testable on any platform.
/// </summary>
public static class OggVorbisDecoder
{
    /// <summary>Decodes a complete Ogg Vorbis file to interleaved float PCM.</summary>
    /// <exception cref="ArgumentException">The data is not an Ogg Vorbis stream.</exception>
    /// <exception cref="InvalidDataException">The stream is corrupt.</exception>
    public static DecodedPcm Decode(ReadOnlyMemory<byte> fileBytes)
    {
        byte[] array = System.Runtime.InteropServices.MemoryMarshal.TryGetArray(fileBytes, out var seg) && seg.Offset == 0 && seg.Count == seg.Array!.Length
            ? seg.Array
            : fileBytes.ToArray();

        using var stream = new MemoryStream(array, writable: false);
        using var reader = new VorbisReader(stream, closeOnDispose: false);

        int channels = reader.Channels;
        int sampleRate = reader.SampleRate;
        long totalFrames = reader.TotalSamples;
        if (channels <= 0 || sampleRate <= 0 || totalFrames < 0)
            throw new ArgumentException("Not a valid Ogg Vorbis stream.", nameof(fileBytes));

        var samples = new float[checked(totalFrames * channels)];
        int read = 0;
        while (read < samples.Length)
        {
            int n = reader.ReadSamples(samples, read, samples.Length - read);
            if (n <= 0)
                break;
            read += n;
        }

        if (read != samples.Length)
            Array.Resize(ref samples, read - (read % channels)); // truncated stream: keep whole frames
        return new DecodedPcm(samples, channels, sampleRate);
    }
}
