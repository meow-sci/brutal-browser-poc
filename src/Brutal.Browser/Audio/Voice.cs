namespace Brutal.Browser.Audio;

/// <summary>One playing instance of a <see cref="Sound"/>.</summary>
public sealed class Voice
{
    private readonly int _handle;

    internal Voice(int handle) => _handle = handle;

    /// <summary><c>true</c> until the sound ends or <see cref="Stop"/> is called.</summary>
    public bool IsPlaying => _handle != 0 && AudioInterop.IsPlaying(_handle);

    /// <summary>Stops the voice immediately.</summary>
    public void Stop()
    {
        if (_handle != 0)
            AudioInterop.Stop(_handle);
    }
}
