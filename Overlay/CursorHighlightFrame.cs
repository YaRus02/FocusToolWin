namespace FocusTool.Win.Overlay;

internal sealed record CursorHighlightFrame(
    ScreenPoint? Cursor,
    IReadOnlyList<CursorClickPulse> Pulses)
{
    public static readonly CursorHighlightFrame Empty = new(null, []);
}
