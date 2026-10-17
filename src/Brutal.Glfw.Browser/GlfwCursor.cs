namespace Brutal.GlfwApi;

/// <summary>A cursor shape. In the browser only the standard shapes exist; they map to CSS <c>cursor</c> values.</summary>
public unsafe struct GlfwCursor : IDisposable, INullable
{
    /// <summary>Image cursor description (custom cursor images are not available in the browser).</summary>
    public struct CreateInfo
    {
        /// <summary>The cursor image.</summary>
        public GlfwImage Image;

        /// <summary>The hot spot in image pixels.</summary>
        public int2 Hot;
    }

    internal GlfwCursor(string cssCursor)
    {
        CssCursor = cssCursor;
    }

    internal string? CssCursor { get; private set; }

    /// <summary>1 for a cursor, 0 for a null cursor (there is no native handle).</summary>
    public nint NativeHandle => CssCursor is null ? 0 : 1;

    /// <summary>Releases the cursor; it becomes null.</summary>
    public void Dispose() => CssCursor = null;

    /// <summary>True for a null or disposed cursor.</summary>
    public bool IsNull() => CssCursor is null;
}
