using Brutal.Browser.Audio;
using HelloMesh.Audio;

namespace HelloMesh;

/// <summary>
/// Browser <see cref="IAudio"/> over <see cref="AudioEngine"/> (Web Audio): <c>decodeAudioData</c>, NVorbis when the browser
/// cannot decode the file. Decoding works while the AudioContext is still suspended; the page resumes it from the start click.
/// </summary>
public sealed class BrowserAudio : IAudio
{
    private Sound? _sound;
    private Voice? _voice;

    public async Task LoadAsync(ReadOnlyMemory<byte> oggBytes) =>
        _sound = await AudioEngine.Create().DecodeAsync(oggBytes);

    public void PlayOneShot()
    {
        var sound = _sound ?? throw new InvalidOperationException("Sound is not loaded.");
        _voice?.Stop();
        _voice = sound.Play(1f);
    }
}
