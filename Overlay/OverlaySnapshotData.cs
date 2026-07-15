namespace FocusTool.Win.Overlay;

internal readonly record struct OverlaySnapshotRevision(long Surface, long Sprites);

/// <summary>
/// What a Capture Stage needs to composite FocusTool's overlays over a captured
/// source frame: the source-rect overlay <see cref="Surface"/> layer plus any
/// free-floating <see cref="Sprites"/> (timers, etc.) positioned by screen rect.
/// Produced on the UI thread; consumed on the Stage's compositor thread.
/// </summary>
internal sealed class OverlaySnapshotData
{
    public OverlaySnapshotData(OverlayLayer? surface, IReadOnlyList<OverlaySprite> sprites)
    {
        Surface = surface;
        Sprites = sprites;
    }

    public OverlayLayer? Surface { get; }
    public IReadOnlyList<OverlaySprite> Sprites { get; }
}

/// <summary>A premultiplied-BGRA snapshot of the overlay surface cropped to the source window's rect, so it maps 1:1 onto the Stage back buffer.</summary>
internal sealed class OverlayLayer
{
    public OverlayLayer(byte[] pixels, int width, int height, int stride)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
        Stride = stride;
    }

    public byte[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
}

/// <summary>A premultiplied-BGRA snapshot of a floating element (e.g. a timer) and its screen rect (physical px).</summary>
internal sealed class OverlaySprite
{
    public OverlaySprite(byte[] pixels, int width, int height, int stride, double screenLeft, double screenTop, double screenWidth, double screenHeight)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
        Stride = stride;
        ScreenLeft = screenLeft;
        ScreenTop = screenTop;
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
    }

    public byte[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public double ScreenLeft { get; }
    public double ScreenTop { get; }
    public double ScreenWidth { get; }
    public double ScreenHeight { get; }
}
