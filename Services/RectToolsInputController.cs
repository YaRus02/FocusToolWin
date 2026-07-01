using System.Windows.Input;
using FocusTool.Win.Models;
using FocusTool.Win.Overlay;
using Shortcut = FocusTool.Win.Native.Shortcut;

namespace FocusTool.Win.Services;

internal sealed class RectToolsInputController
{
    private const string ExitVisualShortcut = "Esc";

    private readonly RectSelectionController _selection;
    private readonly RegionMaskController _masks;
    private readonly RegionSpotlightController _spotlights;
    private readonly Func<InteractionMode> _modeProvider;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Action<AppSettings> _applySettings;
    private readonly Action<InteractionMode> _setMode;
    private readonly Action _invalidateOverlay;
    private readonly Action _notifyStateChanged;
    private readonly Action _registerHotKeys;
    private readonly Action<ScreenRect> _openPinnedLens;
    private readonly Action<ScreenRect, bool> _takeRegionScreenshot;
    private readonly Action<ScreenPoint, int> _showMaskContextMenu;

    public RectToolsInputController(
        RectSelectionController selection,
        RegionMaskController masks,
        RegionSpotlightController spotlights,
        Func<InteractionMode> modeProvider,
        Func<AppSettings> settingsProvider,
        Action<AppSettings> applySettings,
        Action<InteractionMode> setMode,
        Action invalidateOverlay,
        Action notifyStateChanged,
        Action registerHotKeys,
        Action<ScreenRect> openPinnedLens,
        Action<ScreenRect, bool> takeRegionScreenshot,
        Action<ScreenPoint, int> showMaskContextMenu)
    {
        _selection = selection;
        _masks = masks;
        _spotlights = spotlights;
        _modeProvider = modeProvider;
        _settingsProvider = settingsProvider;
        _applySettings = applySettings;
        _setMode = setMode;
        _invalidateOverlay = invalidateOverlay;
        _notifyStateChanged = notifyStateChanged;
        _registerHotKeys = registerHotKeys;
        _openPinnedLens = openPinnedLens;
        _takeRegionScreenshot = takeRegionScreenshot;
        _showMaskContextMenu = showMaskContextMenu;
    }

    public void HandleMouseDown(ScreenPoint point, MouseButton button)
    {
        switch (_modeProvider())
        {
            case InteractionMode.PinnedLensSelect:
                HandlePinnedLensMouseDown(point, button);
                break;
            case InteractionMode.ScreenshotRegionSelect:
                HandleScreenshotRegionMouseDown(point, button);
                break;
            case InteractionMode.RegionSpotlightSelect:
                HandleRegionSpotlightMouseDown(point, button);
                break;
            case InteractionMode.RegionMaskSelect:
                HandleRegionMaskMouseDown(point, button);
                break;
        }
    }

    public void HandleMouseMove(ScreenPoint point)
    {
        switch (_modeProvider())
        {
            case InteractionMode.PinnedLensSelect:
                InvalidateIf(_selection.UpdateDraft(point));
                break;
            case InteractionMode.ScreenshotRegionSelect:
                if (_selection.UpdateScreenshotEdit(point))
                {
                    _invalidateOverlay();
                    return;
                }

                InvalidateIf(_selection.UpdateDraft(point));
                break;
            case InteractionMode.RegionSpotlightSelect:
                if (_spotlights.UpdateEdit(point))
                {
                    _invalidateOverlay();
                    return;
                }

                InvalidateIf(_selection.UpdateDraft(point));
                break;
            case InteractionMode.RegionMaskSelect:
                if (_masks.UpdateEdit(point))
                {
                    _invalidateOverlay();
                    return;
                }

                InvalidateIf(_selection.UpdateDraft(point));
                break;
        }
    }

    public void HandleMouseUp(ScreenPoint point, MouseButton button)
    {
        switch (_modeProvider())
        {
            case InteractionMode.PinnedLensSelect:
                HandlePinnedLensMouseUp(point, button);
                break;
            case InteractionMode.ScreenshotRegionSelect:
                HandleScreenshotRegionMouseUp(point, button);
                break;
            case InteractionMode.RegionSpotlightSelect:
                HandleRegionSpotlightMouseUp(point, button);
                break;
            case InteractionMode.RegionMaskSelect:
                HandleRegionMaskMouseUp(point, button);
                break;
        }
    }

    public bool HandleMouseWheel(ScreenPoint point, int delta, ModifierKeys modifiers)
    {
        if (_modeProvider() != InteractionMode.RegionMaskSelect
            || delta == 0
            || (modifiers & ModifierKeys.Control) == 0
            || (modifiers & ~(ModifierKeys.Control | ModifierKeys.Shift)) != 0)
        {
            return false;
        }

        if (!_masks.TryGetSelectedOrHit(point, out var mask))
        {
            return false;
        }

        var step = (modifiers & ModifierKeys.Shift) != 0 ? 0.01 : 0.05;
        var nextOpacity = Math.Clamp(mask.Opacity + Math.Sign(delta) * step, 0.1, 1.0);
        if (Math.Abs(mask.Opacity - nextOpacity) < 0.001)
        {
            return true;
        }

        var updated = _settingsProvider().Clone();
        updated.RegionMaskOpacity = nextOpacity;
        _applySettings(updated);
        mask.SetOpacity(_settingsProvider().RegionMaskOpacity);
        _invalidateOverlay();
        return true;
    }

    public void HandleCaptureLost()
    {
        switch (_modeProvider())
        {
            case InteractionMode.PinnedLensSelect:
                _setMode(InteractionMode.Passthrough);
                break;
            case InteractionMode.ScreenshotRegionSelect:
                _selection.CancelScreenshotPointerState();
                _invalidateOverlay();
                break;
            case InteractionMode.RegionSpotlightSelect:
                _spotlights.CancelEdit();
                if (_selection.IsDraftActive)
                {
                    _selection.CancelDraft();
                }

                _invalidateOverlay();
                break;
            case InteractionMode.RegionMaskSelect:
                if (_masks.IsMoving || _masks.IsResizing)
                {
                    _masks.CancelEdit();
                    _invalidateOverlay();
                }

                if (_selection.IsDraftActive)
                {
                    _selection.CancelDraft();
                    _invalidateOverlay();
                }

                break;
        }
    }

    public bool HandleKeyDown(Key key, ModifierKeys modifiers)
    {
        var mode = _modeProvider();
        if (Matches(key, modifiers, ExitVisualShortcut)
            || Matches(key, modifiers, _settingsProvider().Shortcuts.ExitAnnotate))
        {
            _setMode(InteractionMode.Passthrough);
            return true;
        }

        if (mode == InteractionMode.RegionMaskSelect)
        {
            if ((key == Key.Back || key == Key.Delete) && modifiers == ModifierKeys.None)
            {
                DeleteSelectedRegionMask();
                return true;
            }

            return false;
        }

        if (mode == InteractionMode.ScreenshotRegionSelect)
        {
            if ((key == Key.Back || key == Key.Delete) && modifiers == ModifierKeys.None)
            {
                DeletePendingScreenshotRegion();
                return true;
            }

            if (key == Key.Enter && modifiers == ModifierKeys.None)
            {
                CommitPendingScreenshotRegion();
                return true;
            }

            return TryNudgeScreenshotRegion(key, modifiers);
        }

        if (mode == InteractionMode.RegionSpotlightSelect)
        {
            if ((key == Key.Back || key == Key.Delete) && modifiers == ModifierKeys.None)
            {
                DeleteSelectedSpotlightRegion();
                return true;
            }

            if (key == Key.Enter && modifiers == ModifierKeys.None)
            {
                _setMode(InteractionMode.Passthrough);
                return true;
            }

            return TryNudgeSelectedSpotlightRegion(key, modifiers);
        }

        return false;
    }

    private void HandlePinnedLensMouseDown(ScreenPoint point, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return;
        }

        _selection.BeginDraft(point);
        _invalidateOverlay();
    }

    private void HandleScreenshotRegionMouseDown(ScreenPoint point, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return;
        }

        if (_selection.PendingScreenshotRegion is not null && _selection.TryBeginScreenshotEdit(point))
        {
            return;
        }

        _selection.BeginDraft(point);
        _invalidateOverlay();
    }

    private void HandleRegionSpotlightMouseDown(ScreenPoint point, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return;
        }

        if (_spotlights.TryHitResizeHandle(point, out var resizeIndex, out var resizeHandle))
        {
            _spotlights.BeginResize(resizeIndex, resizeHandle);
            _selection.CancelDraft();
            _invalidateOverlay();
            return;
        }

        if (_spotlights.TryHit(point, out var moveIndex))
        {
            _spotlights.BeginMove(moveIndex, point);
            _selection.CancelDraft();
            _invalidateOverlay();
            return;
        }

        _spotlights.ClearSelection();
        _selection.BeginDraft(point);
        _invalidateOverlay();
    }

    private void HandleRegionMaskMouseDown(ScreenPoint point, MouseButton button)
    {
        if (button == MouseButton.Right)
        {
            if (_masks.TryHit(point, out var mask))
            {
                _masks.Select(mask.Id);
                _invalidateOverlay();
                _showMaskContextMenu(point, mask.Id);
            }

            return;
        }

        if (button != MouseButton.Left)
        {
            return;
        }

        if (_masks.TryHitResizeHandle(point, out var resizeMask, out var resizeHandle))
        {
            _masks.BeginResize(resizeMask, resizeHandle);
            _selection.CancelDraft();
            _invalidateOverlay();
            return;
        }

        if (_masks.TryHit(point, out var existingMask))
        {
            _masks.BeginMove(existingMask, point);
            _selection.CancelDraft();
            _invalidateOverlay();
            return;
        }

        _masks.ClearSelection();
        _masks.CancelEdit();
        _selection.BeginDraft(point);
        _invalidateOverlay();
    }

    private void HandlePinnedLensMouseUp(ScreenPoint point, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return;
        }

        var sourceRect = _selection.CompleteDraft(point);
        if (sourceRect is null)
        {
            return;
        }

        var completedSourceRect = sourceRect.Value;
        _setMode(InteractionMode.Passthrough);
        if (completedSourceRect.Width >= 16 && completedSourceRect.Height >= 16)
        {
            _openPinnedLens(completedSourceRect);
        }
    }

    private void HandleScreenshotRegionMouseUp(ScreenPoint point, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return;
        }

        if (_selection.IsScreenshotRegionResizing)
        {
            _selection.EndScreenshotPointerAction();
            _invalidateOverlay();
            return;
        }

        if (_selection.IsScreenshotRegionMoving)
        {
            _selection.EndScreenshotPointerAction();
            _invalidateOverlay();
            return;
        }

        var sourceRect = _selection.CompleteDraft(point);
        if (sourceRect is null)
        {
            return;
        }

        var completedSourceRect = sourceRect.Value;
        if (RectGeometry.IsLargeEnough(completedSourceRect))
        {
            _selection.SetPendingScreenshotRegion(completedSourceRect);
            _notifyStateChanged();
        }

        _invalidateOverlay();
    }

    private void HandleRegionSpotlightMouseUp(ScreenPoint point, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return;
        }

        if (_spotlights.IsResizing)
        {
            _spotlights.EndPointerAction();
            _invalidateOverlay();
            return;
        }

        if (_spotlights.IsMoving)
        {
            _spotlights.EndPointerAction();
            _invalidateOverlay();
            return;
        }

        var sourceRect = _selection.CompleteDraft(point);
        if (sourceRect is null)
        {
            return;
        }

        var completedSourceRect = sourceRect.Value;
        if (RectGeometry.IsLargeEnough(completedSourceRect))
        {
            var hadSpotlightRegions = _spotlights.HasRegions;
            _spotlights.Add(completedSourceRect);
            if (!hadSpotlightRegions)
            {
                _registerHotKeys();
            }
        }

        _selection.RestoreToolbarAfterRectSelection();
        _invalidateOverlay();
        _notifyStateChanged();
    }

    private void HandleRegionMaskMouseUp(ScreenPoint point, MouseButton button)
    {
        if (_masks.IsResizing && button == MouseButton.Left)
        {
            _masks.EndPointerAction();
            _invalidateOverlay();
            return;
        }

        if (_masks.IsMoving && button == MouseButton.Left)
        {
            _masks.EndPointerAction();
            _invalidateOverlay();
            return;
        }

        if (button != MouseButton.Left)
        {
            return;
        }

        var maskRect = _selection.CompleteDraft(point);
        if (maskRect is null)
        {
            return;
        }

        var completedMaskRect = maskRect.Value;
        if (RectGeometry.IsLargeEnough(completedMaskRect))
        {
            _masks.Add(completedMaskRect, _settingsProvider());
        }

        _selection.RestoreToolbarAfterRectSelection();
        _invalidateOverlay();
        _notifyStateChanged();
    }

    private void DeleteSelectedRegionMask()
    {
        if (!_masks.DeleteSelected())
        {
            return;
        }

        _selection.CancelDraft();
        _invalidateOverlay();
        _notifyStateChanged();
    }

    private void DeleteSelectedSpotlightRegion()
    {
        var hadRegions = _spotlights.HasRegions;
        if (!_spotlights.DeleteSelected())
        {
            return;
        }

        if (hadRegions && !_spotlights.HasRegions)
        {
            _registerHotKeys();
        }

        _invalidateOverlay();
        _notifyStateChanged();
    }

    private void DeletePendingScreenshotRegion()
    {
        if (!_selection.DeletePendingScreenshotRegion())
        {
            return;
        }

        _selection.CancelDraft();
        _invalidateOverlay();
        _notifyStateChanged();
    }

    private void CommitPendingScreenshotRegion()
    {
        if (!_selection.TryTakePendingScreenshotRegion(out var rect, out var restoreToolbar))
        {
            return;
        }

        _setMode(InteractionMode.Passthrough);
        _takeRegionScreenshot(rect, restoreToolbar);
    }

    private bool TryNudgeScreenshotRegion(Key key, ModifierKeys modifiers)
    {
        if (!TryGetNudgeDelta(key, modifiers, out var dx, out var dy)
            || !_selection.TryNudgePendingScreenshotRegion(dx, dy))
        {
            return false;
        }

        _invalidateOverlay();
        return true;
    }

    private bool TryNudgeSelectedSpotlightRegion(Key key, ModifierKeys modifiers)
    {
        if (!TryGetNudgeDelta(key, modifiers, out var dx, out var dy)
            || !_spotlights.NudgeSelected(dx, dy))
        {
            return false;
        }

        _invalidateOverlay();
        return true;
    }

    private void InvalidateIf(bool shouldInvalidate)
    {
        if (shouldInvalidate)
        {
            _invalidateOverlay();
        }
    }

    private static bool TryGetNudgeDelta(Key key, ModifierKeys modifiers, out double dx, out double dy)
    {
        dx = 0;
        dy = 0;

        if ((modifiers & ~ModifierKeys.Shift) != 0)
        {
            return false;
        }

        var step = (modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
        switch (key)
        {
            case Key.Left:
                dx = -step;
                return true;
            case Key.Right:
                dx = step;
                return true;
            case Key.Up:
                dy = -step;
                return true;
            case Key.Down:
                dy = step;
                return true;
            default:
                return false;
        }
    }

    private static bool Matches(Key key, ModifierKeys modifiers, string shortcutText)
    {
        return Shortcut.TryParse(shortcutText, out var shortcut) && shortcut.Matches(key, modifiers);
    }
}
