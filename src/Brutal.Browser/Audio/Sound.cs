namespace Brutal.Browser.Audio;

/// <summary>A decoded sound (an <c>AudioBuffer</c> in JS). Cheap to play many times.</summary>
public sealed class Sound : IDisposable
{
    private int _handle;

    internal Sound(int handle, SoundDecoder decoder)
    {
        _handle = handle;
        Decoder = decoder;
        Duration = AudioInterop.GetDuration(handle);
        Channels = AudioInterop.GetChannels(handle);
        SampleRate = AudioInterop.GetSampleRate(handle);
    }

    /// <summary>Length in seconds.</summary>
    public double Duration { get; }

    /// <summary>Channel count.</summary>
    public int Channels { get; }

    /// <summary>Sample rate of the decoded buffer in Hz.</summary>
    public int SampleRate { get; }

    /// <summary>Which decoder produced this sound.</summary>
    public SoundDecoder Decoder { get; }

    /// <summary>Starts a new voice. Playback begins once the AudioContext is running.</summary>
    /// <param name="gain">Linear volume of this voice.</param>
    /// <param name="loop">Loop until <see cref="Voice.Stop"/>.</param>
    public Voice Play(float gain = 1f, bool loop = false)
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        return new Voice(AudioInterop.Play(_handle, gain, loop));
    }

    /// <summary>Releases the JS buffer. Voices already playing continue.</summary>
    public void Dispose()
    {
        if (_handle != 0)
        {
            AudioInterop.ReleaseBuffer(_handle);
            _handle = 0;
        }
    }
}
