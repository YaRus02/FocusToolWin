namespace FocusTool.Win.Overlay;

internal readonly record struct CursorClickPulse(
    ScreenPoint Point,
    CursorClickButton Button,
    double StartedAtMs);
