using System.Runtime.InteropServices.JavaScript;

namespace Brutal.Browser.Audio;

/// <summary>[JSImport] declarations of <c>wwwroot/brutal-browser-audio.js</c>. Only call when <see cref="OperatingSystem.IsBrowser"/>.</summary>
internal static partial class AudioInterop
{
    private const string Module = "brutal-browser-audio";

    [JSImport("resume", Module)]
    internal static partial Task<bool> Resume();

    [JSImport("decode", Module)]
    internal static partial Task<int> Decode(byte[] fileBytes);

    [JSImport("getLastError", Module)]
    internal static partial string GetLastError();

    [JSImport("createBufferFromPcm", Module)]
    internal static partial int CreateBufferFromPcm(nint floatPtr, int floatCount, int channels, int sampleRate);

    [JSImport("releaseBuffer", Module)]
    internal static partial void ReleaseBuffer(int handle);

    [JSImport("getDuration", Module)]
    internal static partial double GetDuration(int handle);

    [JSImport("getChannels", Module)]
    internal static partial int GetChannels(int handle);

    [JSImport("getSampleRate", Module)]
    internal static partial int GetSampleRate(int handle);

    [JSImport("play", Module)]
    internal static partial int Play(int handle, double gain, bool loop);

    [JSImport("stop", Module)]
    internal static partial void Stop(int voice);

    [JSImport("isPlaying", Module)]
    internal static partial bool IsPlaying(int voice);

    [JSImport("setMasterGain", Module)]
    internal static partial void SetMasterGain(double gain);

    [JSImport("getState", Module)]
    internal static partial string GetState();
}
