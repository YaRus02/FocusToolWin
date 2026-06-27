using FocusTool.Win.Models;
using FocusTool.Win.Native;

namespace FocusTool.Win.Services;

internal sealed class InteractionModeTransitionController
{
    private readonly Func<InteractionMode> _modeProvider;
    private readonly Action<InteractionMode> _setModeCore;
    private readonly Func<LaserActivationMode> _activationModeProvider;
    private readonly Func<InteractionMode, bool> _exitVisualHotKeyNeededProvider;
    private readonly Action<InteractionMode> _cancelPushToAnnotateIfLeavingAnnotate;
    private readonly Action _onLeavingAnnotationInput;
    private readonly Action<InteractionMode> _resetRectStateForMode;
    private readonly Action _restoreToolbarAfterRectSelection;
    private readonly Action _resetSpotlightRegionEditState;
    private readonly Action _onLeavingRegionMaskSelection;
    private readonly Action _saveScreenBoardSnapshot;
    private readonly Action _clearScreenBoardFrame;
    private readonly Action _showOverlay;
    private readonly Action<InteractionMode> _setOverlayInteractionMode;
    private readonly Action _invalidateOverlay;
    private readonly Func<bool> _magnifierEnabledProvider;
    private readonly Action _closeMagnifierHost;
    private readonly Action _refreshMagnifierAfterVisualBoard;
    private readonly Action _hidePinnedLensesForBoard;
    private readonly Action _restorePinnedLensesAfterBoard;
    private readonly Action<bool> _setLaserVisualActive;
    private readonly Action _registerHotKeys;
    private readonly Action _notifyStateChanged;
    private IntPtr _previousForegroundWindow = IntPtr.Zero;
    private IntPtr _lastExternalForegroundWindow = IntPtr.Zero;

    public InteractionModeTransitionController(
        Func<InteractionMode> modeProvider,
        Action<InteractionMode> setModeCore,
        Func<LaserActivationMode> activationModeProvider,
        Func<InteractionMode, bool> exitVisualHotKeyNeededProvider,
        Action<InteractionMode> cancelPushToAnnotateIfLeavingAnnotate,
        Action onLeavingAnnotationInput,
        Action<InteractionMode> resetRectStateForMode,
        Action restoreToolbarAfterRectSelection,
        Action resetSpotlightRegionEditState,
        Action onLeavingRegionMaskSelection,
        Action saveScreenBoardSnapshot,
        Action clearScreenBoardFrame,
        Action showOverlay,
        Action<InteractionMode> setOverlayInteractionMode,
        Action invalidateOverlay,
        Func<bool> magnifierEnabledProvider,
        Action closeMagnifierHost,
        Action refreshMagnifierAfterVisualBoard,
        Action hidePinnedLensesForBoard,
        Action restorePinnedLensesAfterBoard,
        Action<bool> setLaserVisualActive,
        Action registerHotKeys,
        Action notifyStateChanged)
    {
        _modeProvider = modeProvider;
        _setModeCore = setModeCore;
        _activationModeProvider = activationModeProvider;
        _exitVisualHotKeyNeededProvider = exitVisualHotKeyNeededProvider;
        _cancelPushToAnnotateIfLeavingAnnotate = cancelPushToAnnotateIfLeavingAnnotate;
        _onLeavingAnnotationInput = onLeavingAnnotationInput;
        _resetRectStateForMode = resetRectStateForMode;
        _restoreToolbarAfterRectSelection = restoreToolbarAfterRectSelection;
        _resetSpotlightRegionEditState = resetSpotlightRegionEditState;
        _onLeavingRegionMaskSelection = onLeavingRegionMaskSelection;
        _saveScreenBoardSnapshot = saveScreenBoardSnapshot;
        _clearScreenBoardFrame = clearScreenBoardFrame;
        _showOverlay = showOverlay;
        _setOverlayInteractionMode = setOverlayInteractionMode;
        _invalidateOverlay = invalidateOverlay;
        _magnifierEnabledProvider = magnifierEnabledProvider;
        _closeMagnifierHost = closeMagnifierHost;
        _refreshMagnifierAfterVisualBoard = refreshMagnifierAfterVisualBoard;
        _hidePinnedLensesForBoard = hidePinnedLensesForBoard;
        _restorePinnedLensesAfterBoard = restorePinnedLensesAfterBoard;
        _setLaserVisualActive = setLaserVisualActive;
        _registerHotKeys = registerHotKeys;
        _notifyStateChanged = notifyStateChanged;
    }

    public void SetMode(InteractionMode mode)
    {
        var currentMode = _modeProvider();
        if (currentMode == mode)
        {
            return;
        }

        _cancelPushToAnnotateIfLeavingAnnotate(mode);

        var leavingAnnotationInput = IsAnnotationMode(currentMode) && !IsAnnotationMode(mode);
        var enteringAnnotationInput = !IsAnnotationMode(currentMode) && IsAnnotationMode(mode);
        var leavingRectSelection = IsRectSelectionMode(currentMode) && currentMode != mode;
        var leavingRegionMaskSelection = currentMode == InteractionMode.RegionMaskSelect && mode != InteractionMode.RegionMaskSelect;
        var leavingScreenBoard = currentMode == InteractionMode.ScreenBoard && mode != InteractionMode.ScreenBoard;
        var leavingVisualBoard = IsVisualBoardMode(currentMode);
        var enteringVisualBoard = IsVisualBoardMode(mode);
        var exitVisualHotKeyWasNeeded = _exitVisualHotKeyNeededProvider(currentMode);

        if (enteringAnnotationInput)
        {
            var foreground = NativeMethods.GetForegroundWindow();
            _ = NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
            _previousForegroundWindow = processId != 0 && processId != (uint)Environment.ProcessId
                ? foreground
                : _lastExternalForegroundWindow;
        }

        if (leavingAnnotationInput)
        {
            _onLeavingAnnotationInput();
        }

        if (leavingRectSelection)
        {
            _resetRectStateForMode(currentMode);
            if (currentMode == InteractionMode.RegionSpotlightSelect)
            {
                _resetSpotlightRegionEditState();
            }

            _restoreToolbarAfterRectSelection();
        }

        if (leavingRegionMaskSelection)
        {
            _onLeavingRegionMaskSelection();
        }

        if (leavingScreenBoard)
        {
            _saveScreenBoardSnapshot();
        }

        _setModeCore(mode);
        if (mode != InteractionMode.ScreenBoard)
        {
            _clearScreenBoardFrame();
        }

        _showOverlay();
        _setOverlayInteractionMode(mode);
        if (_magnifierEnabledProvider())
        {
            if (enteringVisualBoard)
            {
                _closeMagnifierHost();
            }
            else if (leavingVisualBoard)
            {
                _refreshMagnifierAfterVisualBoard();
            }
        }

        if (enteringVisualBoard)
        {
            _hidePinnedLensesForBoard();
        }
        else if (leavingVisualBoard)
        {
            _restorePinnedLensesAfterBoard();
        }

        _invalidateOverlay();

        if (leavingAnnotationInput && _previousForegroundWindow != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(_previousForegroundWindow);
            _previousForegroundWindow = IntPtr.Zero;
        }

        _setLaserVisualActive(_activationModeProvider() == LaserActivationMode.Always);
        var exitVisualHotKeyIsNeeded = _exitVisualHotKeyNeededProvider(mode);
        if (exitVisualHotKeyWasNeeded != exitVisualHotKeyIsNeeded)
        {
            _registerHotKeys();
        }

        _notifyStateChanged();
    }

    public void TrackExternalForegroundWindow()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return;
        }

        _ = NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        if (processId != 0 && processId != (uint)Environment.ProcessId)
        {
            _lastExternalForegroundWindow = foreground;
        }
    }

    private static bool IsAnnotationMode(InteractionMode mode)
    {
        return mode is InteractionMode.Annotate or InteractionMode.ScreenBoard or InteractionMode.BlackScreen or InteractionMode.WhiteScreen;
    }

    private static bool IsRectSelectionMode(InteractionMode mode)
    {
        return mode is InteractionMode.PinnedLensSelect
            or InteractionMode.RegionMaskSelect
            or InteractionMode.ScreenshotRegionSelect
            or InteractionMode.RegionSpotlightSelect;
    }

    private static bool IsVisualBoardMode(InteractionMode mode)
    {
        return mode is InteractionMode.ScreenBoard or InteractionMode.BlackScreen or InteractionMode.WhiteScreen;
    }
}
