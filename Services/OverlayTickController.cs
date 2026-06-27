using FocusTool.Win.Models;

namespace FocusTool.Win.Services;

internal sealed class OverlayTickController
{
    private readonly Func<bool> _disposedProvider;
    private readonly Func<InteractionMode> _modeProvider;
    private readonly Func<LaserActivationMode> _activationModeProvider;
    private readonly Func<bool> _magnifierEnabledProvider;
    private readonly Func<bool> _canExitPushToAnnotate;
    private readonly Func<bool> _updateFadingAnnotations;
    private readonly Action _trackExternalForegroundWindow;
    private readonly PushToAnnotateController _pushToAnnotate;
    private readonly PointerVisualController _pointerVisuals;
    private readonly MagnifierController _magnifier;
    private readonly VisualEffectsController _visualEffects;
    private readonly Action _invalidateOverlay;
    private readonly Action<TimeSpan> _setTimerInterval;
    private readonly TimeSpan _activeInterval;
    private readonly TimeSpan _fadeInterval;
    private readonly double _movementThresholdPixels;

    public OverlayTickController(
        Func<bool> disposedProvider,
        Func<InteractionMode> modeProvider,
        Func<LaserActivationMode> activationModeProvider,
        Func<bool> magnifierEnabledProvider,
        Func<bool> canExitPushToAnnotate,
        Func<bool> updateFadingAnnotations,
        Action trackExternalForegroundWindow,
        PushToAnnotateController pushToAnnotate,
        PointerVisualController pointerVisuals,
        MagnifierController magnifier,
        VisualEffectsController visualEffects,
        Action invalidateOverlay,
        Action<TimeSpan> setTimerInterval,
        TimeSpan activeInterval,
        TimeSpan fadeInterval,
        double movementThresholdPixels)
    {
        _disposedProvider = disposedProvider;
        _modeProvider = modeProvider;
        _activationModeProvider = activationModeProvider;
        _magnifierEnabledProvider = magnifierEnabledProvider;
        _canExitPushToAnnotate = canExitPushToAnnotate;
        _updateFadingAnnotations = updateFadingAnnotations;
        _trackExternalForegroundWindow = trackExternalForegroundWindow;
        _pushToAnnotate = pushToAnnotate;
        _pointerVisuals = pointerVisuals;
        _magnifier = magnifier;
        _visualEffects = visualEffects;
        _invalidateOverlay = invalidateOverlay;
        _setTimerInterval = setTimerInterval;
        _activeInterval = activeInterval;
        _fadeInterval = fadeInterval;
        _movementThresholdPixels = movementThresholdPixels;
    }

    public void Tick()
    {
        if (_disposedProvider())
        {
            return;
        }

        _trackExternalForegroundWindow();
        _pushToAnnotate.Update(_canExitPushToAnnotate());

        var fadingAnnotationsAnimating = _updateFadingAnnotations();
        var mode = _modeProvider();
        var magnifierActive = _magnifierEnabledProvider();
        var suppressPointerVisuals = magnifierActive;
        var cursorHighlightAnimating = false;
        if (suppressPointerVisuals)
        {
            _pointerVisuals.ClearLaser();
            _pointerVisuals.ClearCursorHighlightPoint();
            _pointerVisuals.ClearCursorClickPulses();
        }
        else
        {
            cursorHighlightAnimating = _pointerVisuals.UpdateCursorHighlight(force: false);
        }

        var spotlightActive = _visualEffects.IsSpotlightVisibleInMode(mode);
        var activationMode = _activationModeProvider();
        var holdActive = !suppressPointerVisuals && _pointerVisuals.IsLaserHoldActive(activationMode);

        _pointerVisuals.SetLaserVisualActive(!suppressPointerVisuals && holdActive);
        if (magnifierActive)
        {
            if (!_magnifier.IsRenderingSubscribed)
            {
                _magnifier.RefreshFromCurrentCursor(forceCursorInvalidation: false, _movementThresholdPixels);
                _invalidateOverlay();
            }
        }
        else if (spotlightActive)
        {
            _visualEffects.UpdateSpotlightCursor(force: false);
        }

        if (holdActive)
        {
            _pointerVisuals.TrackLaserWhileHeld(activationMode);
            return;
        }

        _pointerVisuals.FadeLaserAfterRelease();
        if (_pushToAnnotate.Active)
        {
            _setTimerInterval(_activeInterval);
        }
        else if (magnifierActive && !_magnifier.IsRenderingSubscribed)
        {
            _setTimerInterval(_activeInterval);
        }
        else if (spotlightActive)
        {
            _setTimerInterval(_activeInterval);
        }
        else if (fadingAnnotationsAnimating)
        {
            _setTimerInterval(_fadeInterval);
        }
        else if (cursorHighlightAnimating)
        {
            _setTimerInterval(_fadeInterval);
        }
    }

    private static bool IsAnnotationMode(InteractionMode mode)
    {
        return mode is InteractionMode.Annotate or InteractionMode.ScreenBoard or InteractionMode.BlackScreen or InteractionMode.WhiteScreen;
    }
}
