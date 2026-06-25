namespace FocusTool.Win.Overlay;

internal readonly record struct RectOverlayVisual(
    ScreenRect Rect,
    bool IsDraft,
    bool ShowHandles,
    bool ShowReadout);
