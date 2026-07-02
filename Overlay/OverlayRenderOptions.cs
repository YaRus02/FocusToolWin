namespace FocusTool.Win.Overlay;

internal readonly record struct OverlayRenderOptions(bool SuppressCursorHighlight, bool SuppressLaser, bool SuppressLiveAdjustmentHud)
{
    public static OverlayRenderOptions Live { get; } = new(
        SuppressCursorHighlight: false,
        SuppressLaser: false,
        SuppressLiveAdjustmentHud: false);

    // Capture Stage disables WGC cursor capture, so FocusTool pointer visuals are
    // rendered in the overlay snapshot without doubling the OS cursor.
    public static OverlayRenderOptions CaptureStage { get; } = new(
        SuppressCursorHighlight: false,
        SuppressLaser: false,
        SuppressLiveAdjustmentHud: true);
}
