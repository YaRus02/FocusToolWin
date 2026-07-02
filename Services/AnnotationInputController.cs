using System.Threading;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using FocusTool.Win.Models;
using FocusTool.Win.Native;
using FocusTool.Win.Overlay;
using DrawingPoint = System.Drawing.Point;
using Forms = System.Windows.Forms;
using Shortcut = FocusTool.Win.Native.Shortcut;

namespace FocusTool.Win.Services;

internal sealed class AnnotationInputController
{
    private readonly AnnotationDocument _annotations;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Func<bool> _pushToAnnotateActiveProvider;
    private readonly Func<Shortcut> _pushToAnnotateShortcutProvider;
    private readonly TryGetScreenPoint _tryGetCursor;
    private readonly Action _exitAnnotationMode;
    private readonly Action _tryCompletePushToAnnotateExit;
    private readonly Action _undo;
    private readonly Action _redo;
    private readonly Action _clear;
    private readonly Action _deleteSelection;
    private readonly Action<double> _adjustThickness;
    private readonly Action<AnnotationTool> _setTool;
    private readonly Action _selectStepTool;
    private readonly Action<int> _setPresetColor;

    public AnnotationInputController(
        AnnotationDocument annotations,
        Func<AppSettings> settingsProvider,
        Func<bool> pushToAnnotateActiveProvider,
        Func<Shortcut> pushToAnnotateShortcutProvider,
        TryGetScreenPoint tryGetCursor,
        Action exitAnnotationMode,
        Action tryCompletePushToAnnotateExit,
        Action undo,
        Action redo,
        Action clear,
        Action deleteSelection,
        Action<double> adjustThickness,
        Action<AnnotationTool> setTool,
        Action selectStepTool,
        Action<int> setPresetColor)
    {
        _annotations = annotations;
        _settingsProvider = settingsProvider;
        _pushToAnnotateActiveProvider = pushToAnnotateActiveProvider;
        _pushToAnnotateShortcutProvider = pushToAnnotateShortcutProvider;
        _tryGetCursor = tryGetCursor;
        _exitAnnotationMode = exitAnnotationMode;
        _tryCompletePushToAnnotateExit = tryCompletePushToAnnotateExit;
        _undo = undo;
        _redo = redo;
        _clear = clear;
        _deleteSelection = deleteSelection;
        _adjustThickness = adjustThickness;
        _setTool = setTool;
        _selectStepTool = selectStepTool;
        _setPresetColor = setPresetColor;
    }

    public bool HandleKeyDown(Key key, ModifierKeys modifiers)
    {
        var shortcuts = _settingsProvider().Shortcuts;

        if (key == Key.V && modifiers == ModifierKeys.Control)
        {
            return TryPasteClipboardAnnotation();
        }

        var annotationModifiers = GetAnnotationShortcutModifiers(modifiers);

        if (_annotations.HasTextInput)
        {
            if (key == Key.Enter && annotationModifiers == ModifierKeys.Shift)
            {
                _annotations.AppendText("\n");
                return true;
            }

            if (MatchesAnnotationShortcut(key, modifiers, "Enter"))
            {
                _annotations.CommitTextInput();
                _tryCompletePushToAnnotateExit();
                return true;
            }

            if (MatchesAnnotationShortcut(key, modifiers, "Back"))
            {
                _annotations.BackspaceText();
                return true;
            }

            if (key == Key.Delete && annotationModifiers == ModifierKeys.None)
            {
                _annotations.DeleteText();
                return true;
            }

            if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ExitAnnotate))
            {
                if (_annotations.IsEditingText)
                {
                    _annotations.CancelTextEdit();
                    _tryCompletePushToAnnotateExit();
                }
                else
                {
                    _exitAnnotationMode();
                }

                return true;
            }

            return false;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ExitAnnotate))
        {
            if (_settingsProvider().GetAnnotationTool() == AnnotationTool.Move && _annotations.HasSelection)
            {
                _annotations.ClearSelection();
                _tryCompletePushToAnnotateExit();
                return true;
            }

            if (_annotations.IsObjectEditing)
            {
                _annotations.EndObjectEdit();
                _tryCompletePushToAnnotateExit();
                return true;
            }

            _exitAnnotationMode();
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.Undo))
        {
            _undo();
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.Redo))
        {
            _redo();
            return true;
        }

        if ((key == Key.Back || key == Key.Delete) && modifiers == ModifierKeys.None && _annotations.HasSelection)
        {
            _deleteSelection();
            _tryCompletePushToAnnotateExit();
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.DeleteSelection))
        {
            _deleteSelection();
            _tryCompletePushToAnnotateExit();
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.Clear)
            || MatchesAnnotationShortcut(key, modifiers, shortcuts.ClearAlternate))
        {
            _clear();
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ThicknessDown))
        {
            _adjustThickness(-1);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ThicknessUp))
        {
            _adjustThickness(1);
            return true;
        }

        return TrySelectTool(key, modifiers) || TrySelectColor(key, modifiers);
    }

    public void HandleTextInput(string text)
    {
        if (_annotations.HasTextInput)
        {
            _annotations.AppendText(text);
        }
    }

    private bool TryPasteClipboardAnnotation()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsImage())
            {
                var image = System.Windows.Clipboard.GetImage();
                if (image is null)
                {
                    return false;
                }

                var frozen = FreezeClipboardImage(image);
                var rect = CreatePastedImageRect(frozen, GetPasteAnchorPoint());
                return _annotations.AddPastedImage(frozen, rect);
            }

            if (!System.Windows.Clipboard.ContainsText(System.Windows.TextDataFormat.UnicodeText))
            {
                return false;
            }

            var text = System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.UnicodeText);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (_annotations.HasTextInput)
            {
                _annotations.AppendText(text.Replace("\r\n", "\n").Replace('\r', '\n'));
                return true;
            }

            return _annotations.AddPastedText(text, GetPasteAnchorPoint(), _settingsProvider());
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException or InvalidOperationException)
        {
            AppLog.Error("Could not paste clipboard annotation.", ex);
            return false;
        }
    }

    private bool TrySelectTool(Key key, ModifierKeys modifiers)
    {
        var shortcuts = _settingsProvider().Shortcuts;
        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ToolArrow))
        {
            _setTool(AnnotationTool.Arrow);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ToolRectangle))
        {
            _setTool(AnnotationTool.Rectangle);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ToolEllipse))
        {
            _setTool(AnnotationTool.Ellipse);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ToolLine))
        {
            _setTool(AnnotationTool.Line);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ToolPencil))
        {
            _setTool(AnnotationTool.Pencil);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ToolHighlighter))
        {
            _setTool(AnnotationTool.Highlighter);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ToolText))
        {
            _setTool(AnnotationTool.Text);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ToolMove))
        {
            _setTool(AnnotationTool.Move);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ToolStep))
        {
            _selectStepTool();
            return true;
        }

        return false;
    }

    private bool TrySelectColor(Key key, ModifierKeys modifiers)
    {
        var shortcuts = _settingsProvider().Shortcuts;
        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.Color1))
        {
            _setPresetColor(0);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.Color2))
        {
            _setPresetColor(1);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.Color3))
        {
            _setPresetColor(2);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.Color4))
        {
            _setPresetColor(3);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.Color5))
        {
            _setPresetColor(4);
            return true;
        }

        return false;
    }

    private bool MatchesAnnotationShortcut(Key key, ModifierKeys modifiers, string shortcutText)
    {
        if (Matches(key, modifiers, shortcutText))
        {
            return true;
        }

        var pushShortcut = _pushToAnnotateShortcutProvider();
        if (!_pushToAnnotateActiveProvider() || pushShortcut.Modifiers == ModifierKeys.None)
        {
            return false;
        }

        var strippedModifiers = GetAnnotationShortcutModifiers(modifiers);
        return strippedModifiers != modifiers && Matches(key, strippedModifiers, shortcutText);
    }

    private ModifierKeys GetAnnotationShortcutModifiers(ModifierKeys modifiers)
    {
        return _pushToAnnotateActiveProvider()
            ? modifiers & ~_pushToAnnotateShortcutProvider().Modifiers
            : modifiers;
    }

    private ScreenPoint GetPasteAnchorPoint()
    {
        if (_tryGetCursor(out var cursor))
        {
            return cursor;
        }

        var screen = Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
        return new ScreenPoint(
            screen.Bounds.Left + screen.Bounds.Width / 2.0,
            screen.Bounds.Top + screen.Bounds.Height / 2.0);
    }

    private static BitmapSource FreezeClipboardImage(BitmapSource image)
    {
        if (image.IsFrozen)
        {
            return image;
        }

        var clone = image.Clone();
        clone.Freeze();
        return clone;
    }

    private static ScreenRect CreatePastedImageRect(BitmapSource image, ScreenPoint anchor)
    {
        var screen = Forms.Screen.FromPoint(new DrawingPoint((int)Math.Round(anchor.X), (int)Math.Round(anchor.Y)));
        var maxWidth = Math.Max(160, screen.Bounds.Width * 0.62);
        var maxHeight = Math.Max(120, screen.Bounds.Height * 0.62);
        var width = Math.Max(1, image.PixelWidth);
        var height = Math.Max(1, image.PixelHeight);
        var scale = Math.Min(1, Math.Min(maxWidth / width, maxHeight / height));
        var displayWidth = Math.Max(1, width * scale);
        var displayHeight = Math.Max(1, height * scale);
        var left = anchor.X - displayWidth / 2;
        var top = anchor.Y - displayHeight / 2;

        return new ScreenRect(left, top, left + displayWidth, top + displayHeight);
    }

    private static bool Matches(Key key, ModifierKeys modifiers, string shortcutText)
    {
        return Shortcut.TryParse(shortcutText, out var shortcut) && shortcut.Matches(key, modifiers);
    }
}
