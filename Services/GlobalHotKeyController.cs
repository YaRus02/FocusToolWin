using FocusTool.Win.Models;
using FocusTool.Win.Native;

namespace FocusTool.Win.Services;

internal sealed class GlobalHotKeyController : IDisposable
{
    private readonly Action<string, string> _showMessage;
    private readonly Action _toggleLaserActivation;
    private readonly Action _toggleAnnotate;
    private readonly Action _startPushToAnnotate;
    private readonly Action _toggleCursorHighlight;
    private readonly Action _toggleSpotlight;
    private readonly Action _toggleMagnifier;
    private readonly Action _togglePinnedLens;
    private readonly Action _toggleRegionMask;
    private readonly Action _clearRegionMasks;
    private readonly Action _toggleRegionSpotlight;
    private readonly Action _clearRegionSpotlights;
    private readonly Action _toggleFadingAnnotations;
    private readonly Action _newTimer;
    private readonly Action _toggleToolbar;
    private readonly Action _takeScreenshot;
    private readonly Action _takeRegionScreenshot;
    private readonly Action _toggleScreenBoard;
    private readonly Action _toggleBlackScreen;
    private readonly Action _toggleWhiteScreen;
    private readonly Action _exitApp;
    private readonly Action _exitVisualEffects;
    private HotKeyManager? _manager;

    public GlobalHotKeyController(
        Action<string, string> showMessage,
        Action toggleLaserActivation,
        Action toggleAnnotate,
        Action startPushToAnnotate,
        Action toggleCursorHighlight,
        Action toggleSpotlight,
        Action toggleMagnifier,
        Action togglePinnedLens,
        Action toggleRegionMask,
        Action clearRegionMasks,
        Action toggleRegionSpotlight,
        Action clearRegionSpotlights,
        Action toggleFadingAnnotations,
        Action newTimer,
        Action toggleToolbar,
        Action takeScreenshot,
        Action takeRegionScreenshot,
        Action toggleScreenBoard,
        Action toggleBlackScreen,
        Action toggleWhiteScreen,
        Action exitApp,
        Action exitVisualEffects)
    {
        _showMessage = showMessage;
        _toggleLaserActivation = toggleLaserActivation;
        _toggleAnnotate = toggleAnnotate;
        _startPushToAnnotate = startPushToAnnotate;
        _toggleCursorHighlight = toggleCursorHighlight;
        _toggleSpotlight = toggleSpotlight;
        _toggleMagnifier = toggleMagnifier;
        _togglePinnedLens = togglePinnedLens;
        _toggleRegionMask = toggleRegionMask;
        _clearRegionMasks = clearRegionMasks;
        _toggleRegionSpotlight = toggleRegionSpotlight;
        _clearRegionSpotlights = clearRegionSpotlights;
        _toggleFadingAnnotations = toggleFadingAnnotations;
        _newTimer = newTimer;
        _toggleToolbar = toggleToolbar;
        _takeScreenshot = takeScreenshot;
        _takeRegionScreenshot = takeRegionScreenshot;
        _toggleScreenBoard = toggleScreenBoard;
        _toggleBlackScreen = toggleBlackScreen;
        _toggleWhiteScreen = toggleWhiteScreen;
        _exitApp = exitApp;
        _exitVisualEffects = exitVisualEffects;
    }

    public void Register(ShortcutSettings shortcuts, bool includeExitVisualHotKey, string exitVisualShortcut)
    {
        var registrations = new List<HotKeyRegistration>();
        AddIfEnabled(registrations, shortcuts.ToggleLaserActivation, _toggleLaserActivation);
        AddIfEnabled(registrations, shortcuts.ToggleAnnotate, _toggleAnnotate);
        AddIfEnabled(registrations, shortcuts.PushToAnnotate, _startPushToAnnotate);
        AddIfEnabled(registrations, shortcuts.ToggleCursorHighlight, _toggleCursorHighlight);
        AddIfEnabled(registrations, shortcuts.ToggleSpotlight, _toggleSpotlight);
        AddIfEnabled(registrations, shortcuts.ToggleMagnifier, _toggleMagnifier);
        AddIfEnabled(registrations, shortcuts.TogglePinnedLens, _togglePinnedLens);
        AddIfEnabled(registrations, shortcuts.ToggleRegionMask, _toggleRegionMask);
        AddIfEnabled(registrations, shortcuts.ClearRegionMasks, _clearRegionMasks);
        AddIfEnabled(registrations, shortcuts.ToggleRegionSpotlight, _toggleRegionSpotlight);
        AddIfEnabled(registrations, shortcuts.ClearRegionSpotlights, _clearRegionSpotlights);
        AddIfEnabled(registrations, shortcuts.ToggleFadingAnnotations, _toggleFadingAnnotations);
        AddIfEnabled(registrations, shortcuts.ToggleTimer, _newTimer);
        AddIfEnabled(registrations, shortcuts.ToggleToolbar, _toggleToolbar);
        AddIfEnabled(registrations, shortcuts.TakeScreenshot, _takeScreenshot);
        AddIfEnabled(registrations, shortcuts.TakeRegionScreenshot, _takeRegionScreenshot);
        AddIfEnabled(registrations, shortcuts.ToggleScreenBoard, _toggleScreenBoard);
        AddIfEnabled(registrations, shortcuts.ToggleBlackScreen, _toggleBlackScreen);
        AddIfEnabled(registrations, shortcuts.ToggleWhiteScreen, _toggleWhiteScreen);
        AddIfEnabled(registrations, shortcuts.ExitApp, _exitApp);

        if (includeExitVisualHotKey)
        {
            registrations.Add(new HotKeyRegistration(exitVisualShortcut, _exitVisualEffects));
        }

        _manager ??= new HotKeyManager();
        _manager.SetRegistrations(registrations);

        if (_manager.RegistrationErrors.Count > 0)
        {
            _showMessage("Global hotkey was not registered", string.Join(Environment.NewLine, _manager.RegistrationErrors));
        }
    }

    public static bool HaveSameGlobalHotKeys(ShortcutSettings left, ShortcutSettings right)
    {
        return string.Equals(left.ToggleLaserActivation, right.ToggleLaserActivation, StringComparison.Ordinal)
            && string.Equals(left.ToggleAnnotate, right.ToggleAnnotate, StringComparison.Ordinal)
            && string.Equals(left.PushToAnnotate, right.PushToAnnotate, StringComparison.Ordinal)
            && string.Equals(left.ToggleCursorHighlight, right.ToggleCursorHighlight, StringComparison.Ordinal)
            && string.Equals(left.ToggleSpotlight, right.ToggleSpotlight, StringComparison.Ordinal)
            && string.Equals(left.ToggleMagnifier, right.ToggleMagnifier, StringComparison.Ordinal)
            && string.Equals(left.TogglePinnedLens, right.TogglePinnedLens, StringComparison.Ordinal)
            && string.Equals(left.ToggleRegionMask, right.ToggleRegionMask, StringComparison.Ordinal)
            && string.Equals(left.ClearRegionMasks, right.ClearRegionMasks, StringComparison.Ordinal)
            && string.Equals(left.ToggleRegionSpotlight, right.ToggleRegionSpotlight, StringComparison.Ordinal)
            && string.Equals(left.ClearRegionSpotlights, right.ClearRegionSpotlights, StringComparison.Ordinal)
            && string.Equals(left.ToggleFadingAnnotations, right.ToggleFadingAnnotations, StringComparison.Ordinal)
            && string.Equals(left.ToggleTimer, right.ToggleTimer, StringComparison.Ordinal)
            && string.Equals(left.ToggleToolbar, right.ToggleToolbar, StringComparison.Ordinal)
            && string.Equals(left.TakeScreenshot, right.TakeScreenshot, StringComparison.Ordinal)
            && string.Equals(left.TakeRegionScreenshot, right.TakeRegionScreenshot, StringComparison.Ordinal)
            && string.Equals(left.ToggleScreenBoard, right.ToggleScreenBoard, StringComparison.Ordinal)
            && string.Equals(left.ToggleBlackScreen, right.ToggleBlackScreen, StringComparison.Ordinal)
            && string.Equals(left.ToggleWhiteScreen, right.ToggleWhiteScreen, StringComparison.Ordinal)
            && string.Equals(left.ExitApp, right.ExitApp, StringComparison.Ordinal);
    }

    private static void AddIfEnabled(List<HotKeyRegistration> registrations, string shortcutText, Action action)
    {
        if (!ShortcutSettings.IsShortcutDisabled(shortcutText))
        {
            registrations.Add(new HotKeyRegistration(shortcutText, action));
        }
    }

    public void Dispose()
    {
        _manager?.Dispose();
    }
}
