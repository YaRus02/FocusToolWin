using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

internal sealed class LiveAdjustmentHudController
{
    private const double HoldMs = 900;
    private const double FadeMs = 160;

    private string? _text;
    private ScreenPoint _anchor;
    private double _shownAtMs;

    public void Show(string text, ScreenPoint anchor, double nowMs)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _text = text;
        _anchor = anchor;
        _shownAtMs = nowMs;
    }

    public LiveAdjustmentHudFrame? GetFrame(double nowMs)
    {
        if (_text is null)
        {
            return null;
        }

        var ageMs = Math.Max(0, nowMs - _shownAtMs);
        if (ageMs > HoldMs + FadeMs)
        {
            return null;
        }

        var opacity = ageMs <= HoldMs
            ? 1
            : 1 - ((ageMs - HoldMs) / FadeMs);
        return new LiveAdjustmentHudFrame(_text, _anchor, opacity);
    }

    public bool IsVisible(double nowMs)
    {
        return GetFrame(nowMs) is not null;
    }

    public bool ClearExpired(double nowMs)
    {
        if (_text is null || nowMs - _shownAtMs <= HoldMs + FadeMs)
        {
            return false;
        }

        _text = null;
        return true;
    }
}
