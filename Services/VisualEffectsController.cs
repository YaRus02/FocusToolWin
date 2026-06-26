using FocusTool.Win.Models;

namespace FocusTool.Win.Services;

internal sealed class VisualEffectsController
{
    private readonly Func<ScreenPoint?> _cursorProvider;
    private readonly Action _invalidateOverlay;
    private readonly Action<ScreenPoint, ScreenPoint> _invalidateForCursor;
    private readonly double _movementThresholdPixels;
    private bool _hasSpotlightCursor;
    private ScreenPoint _spotlightCursor;

    public VisualEffectsController(
        Func<ScreenPoint?> cursorProvider,
        Action invalidateOverlay,
        Action<ScreenPoint, ScreenPoint> invalidateForCursor,
        double movementThresholdPixels)
    {
        _cursorProvider = cursorProvider;
        _invalidateOverlay = invalidateOverlay;
        _invalidateForCursor = invalidateForCursor;
        _movementThresholdPixels = movementThresholdPixels;
    }

    public bool SpotlightEnabled { get; set; }
    public bool HasSpotlightCursor => _hasSpotlightCursor;

    public bool TryGetSpotlightCursor(out ScreenPoint point)
    {
        point = _spotlightCursor;
        return _hasSpotlightCursor;
    }

    public bool IsSpotlightVisibleInMode(InteractionMode mode)
    {
        return SpotlightEnabled && !IsVisualBoardMode(mode);
    }

    public ScreenPoint? GetSpotlightPoint(InteractionMode mode, bool magnifierEnabled)
    {
        return (IsSpotlightVisibleInMode(mode) || magnifierEnabled && !IsVisualBoardMode(mode)) && _hasSpotlightCursor
            ? _spotlightCursor
            : null;
    }

    public void UpdateSpotlightCursor(bool force)
    {
        var cursor = _cursorProvider();
        if (cursor is null)
        {
            return;
        }

        var cursorPoint = cursor.Value;
        if (!force && _hasSpotlightCursor && cursorPoint.DistanceTo(_spotlightCursor) < _movementThresholdPixels)
        {
            return;
        }

        SetSpotlightCursor(cursorPoint, _hasSpotlightCursor ? _spotlightCursor : cursorPoint, force);
    }

    public void SetSpotlightCursor(ScreenPoint cursor, ScreenPoint? previous, bool force)
    {
        var previousPoint = previous ?? cursor;
        _spotlightCursor = cursor;
        _hasSpotlightCursor = true;
        if (force)
        {
            _invalidateOverlay();
        }
        else
        {
            _invalidateForCursor(cursor, previousPoint);
        }
    }

    public void ClearSpotlightCursor()
    {
        _hasSpotlightCursor = false;
    }

    public static bool ShouldRegisterExitVisualHotKey(
        InteractionMode mode,
        bool magnifierEnabled,
        bool spotlightEnabled)
    {
        return !IsAnnotationMode(mode) && (magnifierEnabled || spotlightEnabled);
    }

    private static bool IsAnnotationMode(InteractionMode mode)
    {
        return mode is InteractionMode.Annotate or InteractionMode.ScreenBoard or InteractionMode.BlackScreen or InteractionMode.WhiteScreen;
    }

    private static bool IsVisualBoardMode(InteractionMode mode)
    {
        return mode is InteractionMode.ScreenBoard or InteractionMode.BlackScreen or InteractionMode.WhiteScreen;
    }
}
