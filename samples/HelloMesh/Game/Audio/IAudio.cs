namespace HelloMesh.Audio;

/// <summary>Audio backend of the sample: desktop uses OpenAL, the browser uses Web Audio.</summary>
public interface IAudio
{
    /// <summary>Decodes an Ogg Vorbis file and prepares it for playback. Faults the task when decoding or device setup fails.</summary>
    Task LoadAsync(ReadOnlyMemory<byte> oggBytes);

    /// <summary>Plays the loaded sound from the start, restarting it if it is already playing.</summary>
    void PlayOneShot();
}
