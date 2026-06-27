using FocusTool.Win.Models;
using Shortcut = FocusTool.Win.Native.Shortcut;

namespace FocusTool.Win.Services;

internal sealed class PushToAnnotateController
{
    private readonly HashSet<string> _polledShortcutDown = new(StringComparer.Ordinal);
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Func<InteractionMode> _modeProvider;
    private readonly Action<InteractionMode> _setMode;
    private readonly Action<TimeSpan> _setTimerInterval;
    private readonly Func<bool> _hasTextInput;
    private readonly TimeSpan _activeInterval;
    private readonly Action _clearAnnotations;
    private readonly Action<AnnotationTool> _setAnnotationTool;
    private readonly Action _selectStepTool;
    private readonly Action<int> _setAnnotationPresetColor;
    private Shortcut _shortcut;
    private bool _active;
    private bool _exitPending;

    public PushToAnnotateController(
        Func<AppSettings> settingsProvider,
        Func<InteractionMode> modeProvider,
        Action<InteractionMode> setMode,
        Action<TimeSpan> setTimerInterval,
        Func<bool> hasTextInput,
        TimeSpan activeInterval,
        Action clearAnnotations,
        Action<AnnotationTool> setAnnotationTool,
        Action selectStepTool,
        Action<int> setAnnotationPresetColor)
    {
        _settingsProvider = settingsProvider;
        _modeProvider = modeProvider;
        _setMode = setMode;
        _setTimerInterval = setTimerInterval;
        _hasTextInput = hasTextInput;
        _activeInterval = activeInterval;
        _clearAnnotations = clearAnnotations;
        _setAnnotationTool = setAnnotationTool;
        _selectStepTool = selectStepTool;
        _setAnnotationPresetColor = setAnnotationPresetColor;
    }

    public bool Active => _active;
    public Shortcut Shortcut => _shortcut;

    public void ConfigureShortcut()
    {
        var settings = _settingsProvider();
        if (ShortcutSettings.IsShortcutDisabled(settings.Shortcuts.PushToAnnotate))
        {
            _shortcut = default;
            return;
        }

        if (!Shortcut.TryParse(settings.Shortcuts.PushToAnnotate, out _shortcut)
            || _shortcut.IsMouseButton)
        {
            settings.Shortcuts.PushToAnnotate = "Alt+A";
            Shortcut.TryParse(settings.Shortcuts.PushToAnnotate, out _shortcut);
        }
    }

    public void Start(bool disposed)
    {
        var settings = _settingsProvider();
        var mode = _modeProvider();
        if (disposed
            || _active
            || IsAnnotationMode(mode)
            || mode != InteractionMode.Passthrough
            || ShortcutSettings.IsShortcutDisabled(settings.Shortcuts.PushToAnnotate))
        {
            return;
        }

        _active = true;
        _exitPending = false;
        _polledShortcutDown.Clear();
        _setTimerInterval(_activeInterval);
        _setMode(InteractionMode.Annotate);
    }

    public void CancelIfLeavingAnnotate(InteractionMode nextMode)
    {
        if (_active && nextMode != InteractionMode.Annotate)
        {
            Cancel();
        }
    }

    public void Update(bool canExit)
    {
        if (!_active)
        {
            return;
        }

        if (!_exitPending)
        {
            PollShortcuts();
        }

        if (!_exitPending && _shortcut.IsPressed())
        {
            _setTimerInterval(_activeInterval);
            return;
        }

        _polledShortcutDown.Clear();
        _exitPending = true;
        TryCompleteExit(canExit);
        if (_active)
        {
            _setTimerInterval(_activeInterval);
        }
    }

    public void TryCompleteExit(bool canExit)
    {
        if (!_active || !_exitPending || !canExit)
        {
            return;
        }

        Cancel();
        if (_modeProvider() == InteractionMode.Annotate)
        {
            _setMode(InteractionMode.Passthrough);
        }
    }

    private void Cancel()
    {
        _active = false;
        _exitPending = false;
        _polledShortcutDown.Clear();
    }

    private void PollShortcuts()
    {
        if (_modeProvider() != InteractionMode.Annotate || _hasTextInput())
        {
            _polledShortcutDown.Clear();
            return;
        }

        var shortcuts = _settingsProvider().Shortcuts;
        PollShortcut("clear-alt", shortcuts.ClearAlternate, _clearAnnotations);
        PollShortcut("tool-arrow", shortcuts.ToolArrow, () => _setAnnotationTool(AnnotationTool.Arrow));
        PollShortcut("tool-rectangle", shortcuts.ToolRectangle, () => _setAnnotationTool(AnnotationTool.Rectangle));
        PollShortcut("tool-ellipse", shortcuts.ToolEllipse, () => _setAnnotationTool(AnnotationTool.Ellipse));
        PollShortcut("tool-line", shortcuts.ToolLine, () => _setAnnotationTool(AnnotationTool.Line));
        PollShortcut("tool-pencil", shortcuts.ToolPencil, () => _setAnnotationTool(AnnotationTool.Pencil));
        PollShortcut("tool-highlighter", shortcuts.ToolHighlighter, () => _setAnnotationTool(AnnotationTool.Highlighter));
        PollShortcut("tool-text", shortcuts.ToolText, () => _setAnnotationTool(AnnotationTool.Text));
        PollShortcut("tool-move", shortcuts.ToolMove, () => _setAnnotationTool(AnnotationTool.Move));
        PollShortcut("tool-step", shortcuts.ToolStep, _selectStepTool);
        PollShortcut("color-1", shortcuts.Color1, () => _setAnnotationPresetColor(0));
        PollShortcut("color-2", shortcuts.Color2, () => _setAnnotationPresetColor(1));
        PollShortcut("color-3", shortcuts.Color3, () => _setAnnotationPresetColor(2));
        PollShortcut("color-4", shortcuts.Color4, () => _setAnnotationPresetColor(3));
        PollShortcut("color-5", shortcuts.Color5, () => _setAnnotationPresetColor(4));
    }

    private void PollShortcut(string id, string shortcutText, Action action)
    {
        if (ShortcutSettings.IsShortcutDisabled(shortcutText) || !Shortcut.TryParse(shortcutText, out var shortcut))
        {
            _polledShortcutDown.Remove(id);
            return;
        }

        if (!shortcut.IsPressed())
        {
            _polledShortcutDown.Remove(id);
            return;
        }

        if (_polledShortcutDown.Add(id))
        {
            action();
        }
    }

    private static bool IsAnnotationMode(InteractionMode mode)
    {
        return mode is InteractionMode.Annotate or InteractionMode.ScreenBoard or InteractionMode.BlackScreen or InteractionMode.WhiteScreen;
    }
}
