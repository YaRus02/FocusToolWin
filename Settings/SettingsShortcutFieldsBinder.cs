using FocusTool.Win.Models;
using NativeShortcut = FocusTool.Win.Native.Shortcut;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace FocusTool.Win;

internal sealed record SettingsShortcutFieldBinding(
    WpfTextBox Box,
    string Label,
    Func<ShortcutSettings, string> Get,
    Action<ShortcutSettings, string> Set,
    bool Global);

// Settings-only binder for the shortcuts tab. It preserves the old validation
// semantics while keeping field load/read logic out of SettingsWindow.
internal sealed class SettingsShortcutFieldsBinder
{
    private const string ReservedVisualExitShortcut = "Esc";

    private readonly IReadOnlyList<SettingsShortcutFieldBinding> _fields;
    private readonly WpfTextBox _laserHoldBox;
    private readonly WpfTextBox _cursorHighlightHoldBox;
    private readonly Action<string, string> _showWarning;

    public SettingsShortcutFieldsBinder(
        IReadOnlyList<SettingsShortcutFieldBinding> fields,
        WpfTextBox laserHoldBox,
        WpfTextBox cursorHighlightHoldBox,
        Action<string, string> showWarning)
    {
        _fields = fields;
        _laserHoldBox = laserHoldBox;
        _cursorHighlightHoldBox = cursorHighlightHoldBox;
        _showWarning = showWarning;
    }

    public void Load(AppSettings settings)
    {
        _laserHoldBox.Text = settings.LaserHoldShortcut;
        _cursorHighlightHoldBox.Text = settings.CursorHighlightHoldShortcut;
        foreach (var field in _fields)
        {
            field.Box.Text = field.Get(settings.Shortcuts);
        }
    }

    public bool TryRead(AppSettings updated)
    {
        var shortcuts = updated.Shortcuts.Clone();

        updated.LaserHoldShortcut = ReadShortcutText(_laserHoldBox);
        updated.CursorHighlightHoldShortcut = ReadShortcutText(_cursorHighlightHoldBox);
        foreach (var field in _fields)
        {
            field.Set(shortcuts, ReadShortcutText(field.Box));
        }

        if (updated.GetLaserActivationMode() == LaserActivationMode.Hold
            && !ValidateShortcut("Hold laser", updated.LaserHoldShortcut, allowMouseButton: true, allowDisabled: false))
        {
            return false;
        }

        if (updated.GetCursorHighlightActivationMode() == LaserActivationMode.Hold
            && !ValidateShortcut("Hold cursor highlight", updated.CursorHighlightHoldShortcut, allowMouseButton: true, allowDisabled: false))
        {
            return false;
        }

        foreach (var field in _fields)
        {
            if (!ValidateShortcut(field.Label, field.Get(shortcuts)))
            {
                return false;
            }
        }

        if (!ValidateReservedVisualExitShortcut(shortcuts))
        {
            return false;
        }

        if (!ValidateShortcutConflicts(updated, shortcuts))
        {
            return false;
        }

        updated.Shortcuts = shortcuts;
        return true;
    }

    private static string ReadShortcutText(WpfTextBox textBox)
    {
        var text = textBox.Text.Trim();
        return text.Length == 0 ? ShortcutSettings.DisabledShortcut : text;
    }

    private bool ValidateShortcut(string label, string shortcut, bool allowMouseButton = false, bool allowDisabled = true)
    {
        if (allowDisabled && ShortcutSettings.IsShortcutDisabled(shortcut))
        {
            return true;
        }

        if (NativeShortcut.TryParse(shortcut, out var parsed)
            && (allowMouseButton || !parsed.IsMouseButton))
        {
            return true;
        }

        _showWarning("Invalid shortcut", $"Invalid shortcut for {label}: {shortcut}");
        return false;
    }

    private bool ValidateReservedVisualExitShortcut(ShortcutSettings shortcuts)
    {
        foreach (var field in _fields)
        {
            if (!field.Global)
            {
                continue;
            }

            var text = field.Get(shortcuts);
            if (ShortcutSettings.IsShortcutDisabled(text))
            {
                continue;
            }

            if (HasSameShortcut(text, ReservedVisualExitShortcut))
            {
                _showWarning(
                    "Shortcut conflict",
                    $"{field.Label} cannot use {ReservedVisualExitShortcut} because {ReservedVisualExitShortcut} is reserved for closing visual modes.");
                return false;
            }
        }

        return true;
    }

    private bool ValidateShortcutConflicts(AppSettings updated, ShortcutSettings shortcuts)
    {
        var entries = new List<(string Label, string Text)>();
        if (updated.GetCursorHighlightActivationMode() == LaserActivationMode.Hold)
        {
            entries.Add(("Hold cursor highlight", updated.CursorHighlightHoldShortcut));
        }

        if (updated.GetLaserActivationMode() == LaserActivationMode.Hold)
        {
            entries.Add(("Hold laser", updated.LaserHoldShortcut));
        }

        foreach (var field in _fields)
        {
            entries.Add((field.Label, field.Get(shortcuts)));
        }

        var used = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (label, text) in entries)
        {
            if (ShortcutSettings.IsShortcutDisabled(text))
            {
                continue;
            }

            if (!NativeShortcut.TryParse(text, out var shortcut))
            {
                continue;
            }

            var key = $"{(int)shortcut.Modifiers}:{shortcut.VirtualKey}";
            if (used.TryGetValue(key, out var existingLabel))
            {
                _showWarning(
                    "Duplicate shortcut",
                    $"Shortcut conflict: {existingLabel} and {label} both use {shortcut.DisplayText}.");
                return false;
            }

            used[key] = label;
        }

        return true;
    }

    private static bool HasSameShortcut(string left, string right)
    {
        return NativeShortcut.TryParse(left, out var leftShortcut)
            && NativeShortcut.TryParse(right, out var rightShortcut)
            && leftShortcut.Modifiers == rightShortcut.Modifiers
            && leftShortcut.VirtualKey == rightShortcut.VirtualKey;
    }
}
