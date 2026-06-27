namespace FocusTool.Win.Overlay;

internal readonly record struct OverlayRenderOptions(bool SuppressCursorHighlight, bool SuppressLaser)
{
    public static OverlayRenderOptions Live { get; } = new(SuppressCursorHighlight: false, SuppressLaser: false);

    // The Capture Stage mirrors the real OS cursor (WGC cursor capture), so the
    // overlay snapshot renders the full set of pointer visuals (cursor highlight,
    // laser) just like the live screen — they stay in sync with the cursor.
    public static OverlayRenderOptions CaptureStage { get; } = new(SuppressCursorHighlight: false, SuppressLaser: false);
}
