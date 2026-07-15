using FocusTool.Win.Models;
using FocusTool.Win.Native;
using FocusTool.Win.Overlay;
using Shortcut = FocusTool.Win.Native.Shortcut;

namespace FocusTool.Win.Services;

internal sealed class PointerVisualController : IDisposable
{
    private const double CursorPulseDurationMs = 360;
    private const int MaximumCursorClickPulses = 4;

    private readonly TrailModel _trail = new();
    private readonly List<CursorClickPulse> _cursorClickPulses = [];
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Func<double> _clock;
    private readonly Func<bool> _isDisposed;
    private readonly TryGetScreenPoint _tryGetCursor;
    private readonly Action<TimeSpan> _setTimerInterval;
    private readonly Action _invalidate;
    private readonly Action<ScreenPoint, ScreenPoint> _invalidateForCursor;
    private readonly Action _stateChanged;
    private readonly double _movementThresholdPixels;
    private readonly TimeSpan _activeInterval;
    private readonly TimeSpan _fadeInterval;
    private readonly TimeSpan _idleInterval;
    private MouseHook? _mouseHook;
    private Shortcut _laserHoldShortcut;
    private Shortcut _cursorHighlightHoldShortcut;
    private ScreenPoint _lastCursor;
    private ScreenPoint _cursorHighlightPoint;
    private bool _hasLastCursor;
    private bool _hasCursorHighlightPoint;
    private bool _laserVisuallyActive;

    public PointerVisualController(
        Func<AppSettings> settingsProvider,
        Func<double> clock,
        Func<bool> isDisposed,
        TryGetScreenPoint tryGetCursor,
        Action<TimeSpan> setTimerInterval,
        Action invalidate,
        Action<ScreenPoint, ScreenPoint> invalidateForCursor,
        Action stateChanged,
        double movementThresholdPixels,
        TimeSpan activeInterval,
        TimeSpan fadeInterval,
        TimeSpan idleInterval)
    {
        _settingsProvider = settingsProvider;
        _clock = clock;
        _isDisposed = isDisposed;
        _tryGetCursor = tryGetCursor;
        _setTimerInterval = setTimerInterval;
        _invalidate = invalidate;
        _invalidateForCursor = invalidateForCursor;
        _stateChanged = stateChanged;
        _movementThresholdPixels = movementThresholdPixels;
        _activeInterval = activeInterval;
        _fadeInterval = fadeInterval;
        _idleInterval = idleInterval;
    }

    public TrailModel Trail => _trail;
    public bool LaserVisuallyActive => _laserVisuallyActive;
    public bool HasCursorHighlightPoint => _hasCursorHighlightPoint;
    public bool HasCursorClickPulses => _cursorClickPulses.Count > 0;

    public void StartMouseHook()
    {
        if (_mouseHook is not null)
        {
            return;
        }

        _mouseHook = new MouseHook(ex => AppLog.Error("Cursor click hook callback failed.", ex));
        _mouseHook.Clicked += OnMouseHookClicked;
        UpdateMouseHook();
    }

    public void CacheParsedSettings()
    {
        var settings = _settingsProvider();
        if (settings.GetLaserActivationMode() == LaserActivationMode.Always)
        {
            _laserHoldShortcut = default;
        }
        else if (AppSettings.IsLaserHoldShortcutDisabled(settings.LaserHoldShortcut)
            || !Shortcut.TryParse(settings.LaserHoldShortcut, out _laserHoldShortcut))
        {
            settings.LaserHoldShortcut = "Alt+Z";
            Shortcut.TryParse(settings.LaserHoldShortcut, out _laserHoldShortcut);
        }

        if (settings.GetCursorHighlightActivationMode() == LaserActivationMode.Always)
        {
            _cursorHighlightHoldShortcut = default;
        }
        else if (AppSettings.IsLaserHoldShortcutDisabled(settings.CursorHighlightHoldShortcut)
            || !Shortcut.TryParse(settings.CursorHighlightHoldShortcut, out _cursorHighlightHoldShortcut))
        {
            settings.CursorHighlightHoldShortcut = "Alt+X";
            Shortcut.TryParse(settings.CursorHighlightHoldShortcut, out _cursorHighlightHoldShortcut);
        }
    }

    public void UpdateMouseHook()
    {
        if (_mouseHook is null)
        {
            return;
        }

        if (_settingsProvider().ClickPulseEnabled)
        {
            if (!_mouseHook.Install())
            {
                AppLog.Error("Could not install low-level mouse hook for cursor click pulse.");
            }

            return;
        }

        _mouseHook.Uninstall();
        ClearCursorClickPulses();
    }

    public bool UpdateCursorHighlight(bool force)
    {
        var nowMs = _clock();
        var removedExpiredPulses = RemoveExpiredCursorClickPulses(nowMs);
        var active = IsCursorHighlightVisuallyActive();

        if (active && _tryGetCursor(out var cursor))
        {
            var previous = _hasCursorHighlightPoint ? _cursorHighlightPoint : cursor;
            var moved = force
                || !_hasCursorHighlightPoint
                || cursor.DistanceTo(_cursorHighlightPoint) >= 0.5;
            _cursorHighlightPoint = cursor;
            _hasCursorHighlightPoint = true;

            if (moved)
            {
                if (force)
                {
                    _invalidate();
                }
                else
                {
                    _invalidateForCursor(cursor, previous);
                }

                _setTimerInterval(_activeInterval);
            }
            else
            {
                _setTimerInterval(_fadeInterval);
            }
        }
        else if (_hasCursorHighlightPoint)
        {
            var previous = _cursorHighlightPoint;
            _hasCursorHighlightPoint = false;
            _invalidateForCursor(previous, previous);
        }

        if (removedExpiredPulses || _cursorClickPulses.Count > 0)
        {
            _invalidate();
        }

        return active || _cursorClickPulses.Count > 0;
    }

    public CursorHighlightFrame GetCursorHighlightFrame()
    {
        if (!_hasCursorHighlightPoint && _cursorClickPulses.Count == 0)
        {
            return CursorHighlightFrame.Empty;
        }

        return new CursorHighlightFrame(
            _hasCursorHighlightPoint ? _cursorHighlightPoint : null,
            _cursorClickPulses.ToArray());
    }

    public void ClearCursorHighlightPoint()
    {
        var hadVisual = _hasCursorHighlightPoint;
        _hasCursorHighlightPoint = false;
        if (hadVisual)
        {
            _invalidate();
        }
    }

    public void ClearCursorClickPulses()
    {
        if (_cursorClickPulses.Count == 0)
        {
            return;
        }

        _cursorClickPulses.Clear();
        _invalidate();
    }

    public void ClearLaser()
    {
        var hadTrail = _trail.Points.Count > 0;
        _trail.Clear();
        _hasLastCursor = false;
        SetLaserVisualActive(false);
        if (hadTrail)
        {
            _invalidate();
        }
    }

    public bool IsLaserHoldActive(LaserActivationMode activationMode)
    {
        return activationMode == LaserActivationMode.Always || _laserHoldShortcut.IsPressed();
    }

    public void SetLaserVisualActive(bool active)
    {
        if (_laserVisuallyActive == active)
        {
            return;
        }

        _laserVisuallyActive = active;
        _stateChanged();
    }

    public void TrackLaserWhileHeld(LaserActivationMode activationMode)
    {
        if (!_tryGetCursor(out var cursor))
        {
            _setTimerInterval(_idleInterval);
            return;
        }

        var nowMs = _clock();
        if (!_hasLastCursor)
        {
            if (activationMode == LaserActivationMode.Hold)
            {
                _trail.Clear();
            }

            _lastCursor = cursor;
            _hasLastCursor = true;
            _trail.AddPoint(cursor, nowMs);
        }
        else if (cursor.DistanceTo(_lastCursor) >= _movementThresholdPixels)
        {
            _lastCursor = cursor;
            _trail.AddPoint(cursor, nowMs);
        }
        else
        {
            var hadVisibleTrail = _trail.Points.Count > 1;
            _trail.TouchLastPoint(cursor, nowMs);
            _trail.TrimWhileMoving(nowMs, RetainedTrailLengthMs);
            _setTimerInterval(hadVisibleTrail ? _fadeInterval : _idleInterval);
            if (hadVisibleTrail)
            {
                _invalidate();
            }

            return;
        }

        _trail.TrimWhileMoving(nowMs, RetainedTrailLengthMs);
        _setTimerInterval(_activeInterval);
        _invalidate();
    }

    public void FadeLaserAfterRelease()
    {
        _hasLastCursor = false;

        if (_trail.Points.Count == 0 || _trail.LastMovementMs < 0)
        {
            _setTimerInterval(_idleInterval);
            return;
        }

        var settings = _settingsProvider();
        var nowMs = _clock();
        var stationaryMs = nowMs - _trail.LastMovementMs;
        if (stationaryMs > settings.FadeDurationMs + 64)
        {
            _trail.Clear();
            _invalidate();
            _setTimerInterval(_idleInterval);
            return;
        }

        _trail.TrimWhileStationary(RetainedTrailLengthMs);
        _setTimerInterval(_fadeInterval);
        _invalidate();
    }

    public void RefreshLaserAfterSettingsApplied(LaserActivationMode previousActivationMode)
    {
        if (previousActivationMode != _settingsProvider().GetLaserActivationMode())
        {
            _trail.Clear();
            _hasLastCursor = false;
        }
        else
        {
            _trail.TrimWhileMoving(_clock(), RetainedTrailLengthMs);
        }
    }

    private bool RemoveExpiredCursorClickPulses(double nowMs)
    {
        var removed = false;
        for (var i = _cursorClickPulses.Count - 1; i >= 0; i--)
        {
            if (nowMs - _cursorClickPulses[i].StartedAtMs >= CursorPulseDurationMs)
            {
                _cursorClickPulses.RemoveAt(i);
                removed = true;
            }
        }

        return removed;
    }

    private bool IsCursorHighlightVisuallyActive()
    {
        var settings = _settingsProvider();
        return settings.GetCursorHighlightActivationMode() == LaserActivationMode.Always
            || _cursorHighlightHoldShortcut.IsPressed();
    }

    private void OnMouseHookClicked(object? sender, MouseHookClickEventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => OnMouseHookClicked(sender, e));
            return;
        }

        if (_isDisposed()
            || !_settingsProvider().ClickPulseEnabled)
        {
            return;
        }

        _cursorClickPulses.Add(new CursorClickPulse(e.Point, e.Button, _clock()));
        while (_cursorClickPulses.Count > MaximumCursorClickPulses)
        {
            _cursorClickPulses.RemoveAt(0);
        }

        _setTimerInterval(_fadeInterval);
        _invalidate();
    }

    private int RetainedTrailLengthMs => _settingsProvider().TrailLengthMs;

    public void Dispose()
    {
        if (_mouseHook is not null)
        {
            _mouseHook.Clicked -= OnMouseHookClicked;
            _mouseHook.Dispose();
        }
    }
}
