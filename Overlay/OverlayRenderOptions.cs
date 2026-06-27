namespace FocusTool.Win.Overlay;

internal readonly record struct OverlayRenderOptions(bool SuppressPointerVisuals)
{
    public static OverlayRenderOptions Live { get; } = new(SuppressPointerVisuals: false);
    public static OverlayRenderOptions CaptureStage { get; } = new(SuppressPointerVisuals: false);
}
