using System.Windows.Media;
using FocusTool.Win.Models;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using WpfBorder = System.Windows.Controls.Border;
using WpfButton = System.Windows.Controls.Button;
using WpfControl = System.Windows.Controls.Control;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace FocusTool.Win;

// Settings-only binder for a color group: preset swatches, editable hex input,
// optional preview, and the selected preset slot.
internal sealed class SettingsColorSlotBinder
{
    private readonly WpfButton[] _buttons;
    private readonly WpfTextBox _hexBox;
    private readonly WpfBorder? _preview;
    private readonly int _fallbackIndex;
    private readonly Func<bool> _isLoading;
    private readonly Action<bool> _setLoading;
    private readonly string[] _presets = new string[5];
    private int _selectedIndex;

    public SettingsColorSlotBinder(
        WpfButton[] buttons,
        WpfTextBox hexBox,
        WpfBorder? preview,
        int fallbackIndex,
        Func<bool> isLoading,
        Action<bool> setLoading)
    {
        _buttons = buttons;
        _hexBox = hexBox;
        _preview = preview;
        _fallbackIndex = fallbackIndex;
        _isLoading = isLoading;
        _setLoading = setLoading;
    }

    public bool IsValid() => AppSettings.TryParseColor(_hexBox.Text.Trim(), out _);

    public void Load(string settingsColor, IReadOnlyList<string> settingsPresets, string[] defaults)
    {
        var values = GetPresetValues(settingsPresets, defaults);
        Array.Copy(values, _presets, _presets.Length);
        SelectColorSlot(settingsColor, _presets, ref _selectedIndex, _fallbackIndex);
        SetHexTextSilently(_presets[_selectedIndex]);
        ApplyPresetBrushes(_buttons, _presets);
    }

    public void RefreshSwatches()
    {
        UpdateSelectedSwatch();
        UpdatePresetSelection();
    }

    public void SelectPreset(int index)
    {
        if (index < 0 || index >= _presets.Length)
        {
            return;
        }

        _selectedIndex = index;
        SetHexTextSilently(_presets[index]);
        UpdateSelectedSwatch();
        UpdatePresetSelection();
    }

    public void OnHexChanged()
    {
        if (_isLoading())
        {
            return;
        }

        var hex = _hexBox.Text.Trim();
        if (AppSettings.TryParseColor(hex, out _))
        {
            _presets[_selectedIndex] = hex;
        }

        UpdateSelectedSwatch();
    }

    public void Commit(Action<string> setColor, List<string> presetTarget)
    {
        var color = _hexBox.Text.Trim();
        _presets[_selectedIndex] = color;
        setColor(color);
        WritePresetValues(presetTarget, _presets);
    }

    private void SetHexTextSilently(string text)
    {
        var wasLoading = _isLoading();
        _setLoading(true);
        _hexBox.Text = text;
        _setLoading(wasLoading);
    }

    private void UpdateSelectedSwatch()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _buttons.Length)
        {
            return;
        }

        if (AppSettings.TryParseColor(_hexBox.Text.Trim(), out var color))
        {
            _buttons[_selectedIndex].Background = new SolidColorBrush(color);
            if (_preview is not null)
            {
                _preview.Background = new SolidColorBrush(color);
            }

            _hexBox.ClearValue(WpfControl.BorderBrushProperty);
        }
        else
        {
            if (_preview is not null)
            {
                _preview.Background = MediaBrushes.Transparent;
            }

            _hexBox.BorderBrush = MediaBrushes.Firebrick;
        }
    }

    private void UpdatePresetSelection()
    {
        var selected = new SolidColorBrush(MediaColor.FromRgb(32, 32, 32));
        var normal = new SolidColorBrush(MediaColor.FromArgb(0x66, 0, 0, 0));

        for (var i = 0; i < _buttons.Length; i++)
        {
            _buttons[i].BorderBrush = i == _selectedIndex ? selected : normal;
            _buttons[i].Opacity = i == _selectedIndex ? 1.0 : 0.92;
        }
    }

    private static void ApplyPresetBrushes(IReadOnlyList<WpfButton> buttons, IReadOnlyList<string> colors)
    {
        for (var i = 0; i < buttons.Count; i++)
        {
            if (i < colors.Count && AppSettings.TryParseColor(colors[i], out var color))
            {
                buttons[i].Background = new SolidColorBrush(color);
            }
        }
    }

    private static string[] GetPresetValues(IReadOnlyList<string> settingsPresets, string[] defaults)
    {
        var values = (string[])defaults.Clone();

        for (var i = 0; i < values.Length && i < settingsPresets.Count; i++)
        {
            if (AppSettings.TryParseColor(settingsPresets[i], out _))
            {
                values[i] = settingsPresets[i];
            }
        }

        return values;
    }

    private static void SelectColorSlot(string color, string[] presets, ref int selectedIndex, int fallbackIndex)
    {
        if (!TryFindPreset(color, presets, out selectedIndex))
        {
            selectedIndex = Math.Clamp(fallbackIndex, 0, presets.Length - 1);
            if (AppSettings.TryParseColor(color, out _))
            {
                presets[selectedIndex] = color;
            }
        }
    }

    private static bool TryFindPreset(string color, IReadOnlyList<string> presets, out int index)
    {
        for (var i = 0; i < presets.Count; i++)
        {
            if (string.Equals(color, presets[i], StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static void WritePresetValues(List<string> target, IReadOnlyList<string> source)
    {
        while (target.Count < source.Count)
        {
            target.Add("#FFFFFFFF");
        }

        for (var i = 0; i < source.Count; i++)
        {
            target[i] = source[i];
        }
    }
}
