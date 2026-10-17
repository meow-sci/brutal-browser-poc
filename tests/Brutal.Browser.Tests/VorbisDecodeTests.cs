using Brutal.Browser.Audio;

namespace Brutal.Browser.Tests;

public sealed class VorbisDecodeTests
{
    private static byte[] LoadOgg() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "874222__samrad20__generic-explosion.ogg"));

    [Fact]
    public void Decodes_the_poc_asset_to_stereo_48k_float_pcm()
    {
        var pcm = OggVorbisDecoder.Decode(LoadOgg());

        Assert.Equal(2, pcm.Channels);
        Assert.Equal(48000, pcm.SampleRate);
        Assert.Equal(74048, pcm.Frames); // the frame count of the file (ffprobe duration_ts)
        Assert.InRange(pcm.Duration, 1.53, 1.55);
        Assert.Equal(pcm.Frames * pcm.Channels, pcm.Samples.Length);
    }

    [Fact]
    public void Decoded_samples_are_audible_and_in_range()
    {
        var pcm = OggVorbisDecoder.Decode(LoadOgg());

        float peak = 0;
        foreach (float s in pcm.Samples)
        {
            Assert.False(float.IsNaN(s));
            peak = Math.Max(peak, Math.Abs(s));
        }

        Assert.InRange(peak, 0.05f, 1.5f);
    }

    [Fact]
    public void Sub_ranges_of_a_buffer_decode_too()
    {
        byte[] file = LoadOgg();
        var padded = new byte[file.Length + 10];
        file.CopyTo(padded, 5);

        var pcm = OggVorbisDecoder.Decode(new ReadOnlyMemory<byte>(padded, 5, file.Length));
        Assert.Equal(74048, pcm.Frames);
    }

    [Fact]
    public void Garbage_is_rejected()
    {
        var ex = Assert.ThrowsAny<Exception>(() => OggVorbisDecoder.Decode(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        Assert.True(ex is ArgumentException or InvalidDataException, ex.GetType().ToString());
    }
}
