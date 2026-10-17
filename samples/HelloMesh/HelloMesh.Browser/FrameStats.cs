using System.Globalization;

namespace HelloMesh;

/// <summary>
/// Prints <c>PERF avg=&lt;ms&gt; p95=&lt;ms&gt; max=&lt;ms&gt; interval=&lt;ms&gt; alloc=&lt;bytes/frame&gt; gc=&lt;n&gt;</c> every <see cref="Window"/> frames:
/// the CPU time of the frame delegate (input, update, recording, the submit's JS replay call), the mean time between animation
/// frames, the managed bytes allocated per frame and the number of garbage collections in the window.
/// </summary>
internal sealed class FrameStats
{
    public const int Window = 300;

    private readonly double[] _work = new double[Window];
    private readonly double[] _sorted = new double[Window];
    private double _intervalSum;
    private int _count;
    private long _allocatedAtStart = GC.GetAllocatedBytesForCurrentThread();
    private int _collectionsAtStart = GC.CollectionCount(0);

    /// <param name="workMs">CPU time of this frame in milliseconds.</param>
    /// <param name="intervalMs">Time since the previous animation frame in milliseconds (the clamped dt).</param>
    public void Add(double workMs, double intervalMs)
    {
        _work[_count] = workMs;
        _intervalSum += intervalMs;
        if (++_count < Window)
            return;

        _work.CopyTo(_sorted, 0);
        Array.Sort(_sorted);
        var sum = 0.0;
        foreach (var ms in _sorted)
            sum += ms;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var collections = GC.CollectionCount(0);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"PERF avg={sum / Window:F2} p95={_sorted[(int)(Window * 0.95) - 1]:F2} max={_sorted[^1]:F2} interval={_intervalSum / Window:F2} alloc={(allocated - _allocatedAtStart) / Window} gc={collections - _collectionsAtStart}"));
        _count = 0;
        _intervalSum = 0;
        _allocatedAtStart = GC.GetAllocatedBytesForCurrentThread();
        _collectionsAtStart = collections;
    }
}
