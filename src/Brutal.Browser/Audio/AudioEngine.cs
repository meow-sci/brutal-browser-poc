using System.Runtime.InteropServices;

namespace Brutal.Browser.Audio;

/// <summary>Which decoder produced a <see cref="Sound"/>.</summary>
public enum SoundDecoder
{
    /// <summary>The browser's <c>AudioContext.decodeAudioData</c>.</summary>
    WebAudio,

    /// <summary>NVorbis (managed), used when the browser cannot decode the file.</summary>
    Managed,
}

/// <summary>
/// Web Audio engine facade over <c>brutal-browser-audio.js</c>. The engine prints nothing: the game writes the
/// <c>AUDIO loaded</c> / <c>AUDIO play</c> / <c>AUDIO error</c> diagnostics lines (architecture section 12).
/// <para>
/// The <c>AudioContext</c> is created lazily by the JS module and stays <c>suspended</c> until <see cref="ResumeAsync"/> runs
/// inside a user gesture (the page's start overlay calls the JS <c>resume()</c> in the click handler; calling
/// <see cref="ResumeAsync"/> later is harmless). <see cref="DecodeAsync"/> works while suspended.
/// </para>
/// </summary>
public sealed class AudioEngine
{
    private static AudioEngine? s_instance;
    private float _masterGain = 1f;

    private AudioEngine() { }

    /// <summary>
    /// Forces the managed NVorbis path in <see cref="DecodeAsync"/> (for testing the fallback in browsers that can decode Ogg).
    /// </summary>
    public static bool PreferManagedDecoder { get; set; }

    /// <summary>Returns the process-wide engine (there is a single <c>AudioContext</c>). Browser only.</summary>
    /// <exception cref="PlatformNotSupportedException">Not running in the browser.</exception>
    public static AudioEngine Create()
    {
        if (!OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("AudioEngine requires the browser (Web Audio).");
        return s_instance ??= new AudioEngine();
    }

    /// <summary><c>true</c> when the AudioContext exists and its state is <c>running</c>.</summary>
    public bool IsRunning => State == "running";

    /// <summary><c>none</c> (no context yet), <c>suspended</c>, <c>running</c> or <c>closed</c>.</summary>
    public string State => AudioInterop.GetState();

    /// <summary>Master volume (0..1+), applied to every voice.</summary>
    public float MasterGain
    {
        get => _masterGain;
        set
        {
            _masterGain = value;
            AudioInterop.SetMasterGain(value);
        }
    }

    /// <summary>Creates the context if needed and resumes it. Must run inside a user gesture to succeed; resolves whether it is running.</summary>
    public Task<bool> ResumeAsync() => AudioInterop.Resume();

    /// <summary>
    /// Decodes a complete audio file. First <c>decodeAudioData</c>; when the browser rejects the data (for example Ogg Vorbis
    /// on Safari before 18.4) or <see cref="PreferManagedDecoder"/> is set, NVorbis decodes to float PCM which is handed to
    /// <c>createBufferFromPcm</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Neither decoder could decode the data.</exception>
    public async Task<Sound> DecodeAsync(ReadOnlyMemory<byte> fileBytes)
    {
        string? webError = null;
        if (!PreferManagedDecoder)
        {
            // JSImport copies the array into JS, so a private copy is only needed for sub-ranges
            byte[] array = MemoryMarshal.TryGetArray(fileBytes, out var seg) && seg.Offset == 0 && seg.Count == seg.Array!.Length
                ? seg.Array
                : fileBytes.ToArray();
            int handle = await AudioInterop.Decode(array).ConfigureAwait(false);
            if (handle > 0)
                return new Sound(handle, SoundDecoder.WebAudio);
            webError = AudioInterop.GetLastError();
        }

        DecodedPcm pcm;
        try
        {
            pcm = OggVorbisDecoder.Decode(fileBytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Audio decode failed (Web Audio: {webError ?? "skipped"}; managed: {ex.Message})", ex);
        }

        return new Sound(CreateBuffer(pcm), SoundDecoder.Managed);
    }

    private static unsafe int CreateBuffer(in DecodedPcm pcm)
    {
        int handle;
        fixed (float* p = pcm.Samples)
            handle = AudioInterop.CreateBufferFromPcm((nint)p, pcm.Samples.Length, pcm.Channels, pcm.SampleRate);
        if (handle <= 0)
            throw new InvalidOperationException($"createBufferFromPcm failed: {AudioInterop.GetLastError()}");
        return handle;
    }
}
