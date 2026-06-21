using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FocusTool.Win.Models;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace FocusTool.Win;

public partial class SettingsWindow : Window
{
    private const string ReservedVisualExitShortcut = "Esc";

    private static readonly string[] LaserColorPresets =
    [
        "#FFFF2020",
        "#FF00D26A",
        "#FF2080FF",
        "#FF00D7FF",
        "#FFFF2BD6",
        "#FFFFD400",
        "#FFFFFFFF"
    ];

    private readonly Action<AppSettings> _applySettings;
    private AppSettings _settings;
    private bool _loading = true;
    private string _laserColor = "#FFFF2020";
    private string _annotationColor = "#FFFF2020";
    private int _selectedLaserPresetIndex;
    private int _selectedAnnotationPresetIndex;

    public SettingsWindow(AppSettings settings, Action<AppSettings> applySettings, string settingsPath)
    {
        _settings = settings;
        _applySettings = applySettings;
        InitializeComponent();
        LoadSettings(settings, settingsPath);
    }

    private void LoadSettings(AppSettings settings, string settingsPath)
    {
        _loading = true;

        _laserColor = GetPresetOrDefault(settings.Color, LaserColorPresets, out _selectedLaserPresetIndex);
        PointSizeSlider.Value = settings.PointSize;
        TrailLengthSlider.Value = settings.TrailLengthMs;
        FadeDurationSlider.Value = settings.FadeDurationMs;
        GlowCheckBox.IsChecked = settings.GlowEnabled;
        SelectLaserActivationMode(settings.GetLaserActivationMode());
        SpotlightRadiusSlider.Value = settings.SpotlightRadius;
        SpotlightOpacitySlider.Value = settings.SpotlightOpacity * 100;
        MagnifierRadiusSlider.Value = settings.MagnifierRadius;
        MagnifierZoomSlider.Value = settings.MagnifierZoom;
        PinnedLensZoomSlider.Value = settings.PinnedLensZoom;
        PinnedLensRefreshFpsSlider.Value = settings.PinnedLensRefreshFps;
        RegionMaskColorBox.Text = settings.RegionMaskColor;
        RegionMaskOpacitySlider.Value = settings.RegionMaskOpacity * 100;
        FadingAnnotationsCheckBox.IsChecked = settings.FadingAnnotationsEnabled;
        FadingAnnotationVisibleSlider.Value = settings.FadingAnnotationVisibleMs / 1000.0;
        FadingAnnotationFadeSlider.Value = settings.FadingAnnotationFadeMs / 1000.0;

        var annotationPresets = GetAnnotationPresetValues(settings);
        if (!TryFindPreset(settings.AnnotationColor, annotationPresets, out _selectedAnnotationPresetIndex)
            && AppSettings.TryParseColor(settings.AnnotationColor, out _))
        {
            annotationPresets[4] = settings.AnnotationColor;
            _selectedAnnotationPresetIndex = 4;
        }

        _annotationColor = annotationPresets[_selectedAnnotationPresetIndex];
        AnnotationPreset5Box.Text = annotationPresets[4];
        AnnotationThicknessSlider.Value = settings.AnnotationThickness;
        AnnotationFontSizeSlider.Value = settings.AnnotationFontSize;
        SettingsPathText.Text = $"JSON: {settingsPath}";
        LoadShortcutFields(settings);

        ApplyAnnotationPresetBrushes(annotationPresets);

        _loading = false;
        UpdateLabels();
        UpdateLaserPresetSelection();
        UpdateAnnotationPreset5Preview();
        UpdateRegionMaskColorPreview();
        UpdateAnnotationPresetSelection();
        UpdateLaserHoldFieldState();
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        TryApply();
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryApply())
        {
            Close();
        }
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void LaserPreset_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button
            && button.Tag is string color
            && AppSettings.TryParseColor(color, out _)
            && TryFindPreset(color, LaserColorPresets, out var index))
        {
            _laserColor = color;
            _selectedLaserPresetIndex = index;
            UpdateLaserPresetSelection();
        }
    }

    private void AnnotationPreset_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || !int.TryParse(button.Tag?.ToString(), out var index))
        {
            return;
        }

        // Preset 5 is the editable custom slot, so take its live value from the box.
        var hex = index == 4
            ? AnnotationPreset5Box.Text.Trim()
            : index < _settings.AnnotationColorPresets.Count ? _settings.AnnotationColorPresets[index] : null;

        if (hex is not null && AppSettings.TryParseColor(hex, out _))
        {
            _annotationColor = hex;
            _selectedAnnotationPresetIndex = index;
            UpdateAnnotationPresetSelection();
        }
    }

    private void Slider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loading)
        {
            UpdateLabels();
        }
    }

    private void AnnotationPreset5Box_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading)
        {
            UpdateAnnotationPreset5Preview();
            if (_selectedAnnotationPresetIndex == 4 && AppSettings.TryParseColor(AnnotationPreset5Box.Text.Trim(), out _))
            {
                _annotationColor = AnnotationPreset5Box.Text.Trim();
                UpdateAnnotationPresetSelection();
            }
        }
    }

    private void RegionMaskColorBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading)
        {
            UpdateRegionMaskColorPreview();
        }
    }

    private void LaserActivationMode_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            UpdateLaserHoldFieldState();
        }
    }

    private bool TryApply()
    {
        if (!AppSettings.TryParseColor(AnnotationPreset5Box.Text.Trim(), out _))
        {
            System.Windows.MessageBox.Show(this, "Use #AARRGGBB or #RRGGBB for the color 5 preset.", "Invalid color", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!AppSettings.TryParseColor(RegionMaskColorBox.Text.Trim(), out _))
        {
            System.Windows.MessageBox.Show(this, "Use #AARRGGBB or #RRGGBB for the region mask color.", "Invalid color", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (_selectedAnnotationPresetIndex == 4)
        {
            _annotationColor = AnnotationPreset5Box.Text.Trim();
        }

        var updated = _settings.Clone();
        updated.Color = _laserColor;
        updated.PointSize = PointSizeSlider.Value;
        updated.TrailLengthMs = (int)TrailLengthSlider.Value;
        updated.FadeDurationMs = (int)FadeDurationSlider.Value;
        updated.GlowEnabled = GlowCheckBox.IsChecked == true;
        updated.SetLaserActivationMode(ReadLaserActivationMode());
        updated.SpotlightRadius = SpotlightRadiusSlider.Value;
        updated.SpotlightOpacity = SpotlightOpacitySlider.Value / 100.0;
        updated.MagnifierRadius = MagnifierRadiusSlider.Value;
        updated.MagnifierZoom = MagnifierZoomSlider.Value;
        updated.PinnedLensZoom = PinnedLensZoomSlider.Value;
        updated.PinnedLensRefreshFps = (int)PinnedLensRefreshFpsSlider.Value;
        updated.RegionMaskColor = RegionMaskColorBox.Text.Trim();
        updated.RegionMaskOpacity = RegionMaskOpacitySlider.Value / 100.0;
        updated.FadingAnnotationsEnabled = FadingAnnotationsCheckBox.IsChecked == true;
        updated.FadingAnnotationVisibleMs = (int)Math.Round(FadingAnnotationVisibleSlider.Value * 1000);
        updated.FadingAnnotationFadeMs = (int)Math.Round(FadingAnnotationFadeSlider.Value * 1000);
        updated.AnnotationColor = _annotationColor;
        while (updated.AnnotationColorPresets.Count < 5)
        {
            updated.AnnotationColorPresets.Add("#FFFFFFFF");
        }

        updated.AnnotationColorPresets[4] = AnnotationPreset5Box.Text.Trim();
        updated.AnnotationThickness = AnnotationThicknessSlider.Value;
        updated.AnnotationFontSize = AnnotationFontSizeSlider.Value;

        if (!TryReadShortcuts(updated))
        {
            return false;
        }

        updated.Normalize();

        _settings = updated.Clone();
        _applySettings(updated);
        return true;
    }

    private void LoadShortcutFields(AppSettings settings)
    {
        LaserHoldBox.Text = settings.LaserHoldShortcut;

        ToggleAnnotateBox.Text = settings.Shortcuts.ToggleAnnotate;
        ToggleLaserActivationBox.Text = settings.Shortcuts.ToggleLaserActivation;
        ToggleSpotlightBox.Text = settings.Shortcuts.ToggleSpotlight;
        ToggleMagnifierBox.Text = settings.Shortcuts.ToggleMagnifier;
        TogglePinnedLensBox.Text = settings.Shortcuts.TogglePinnedLens;
        ToggleRegionMaskBox.Text = settings.Shortcuts.ToggleRegionMask;
        ClearRegionMasksBox.Text = settings.Shortcuts.ClearRegionMasks;
        ToggleFadingAnnotationsBox.Text = settings.Shortcuts.ToggleFadingAnnotations;
        ToggleToolbarBox.Text = settings.Shortcuts.ToggleToolbar;
        TakeScreenshotBox.Text = settings.Shortcuts.TakeScreenshot;
        ToggleScreenBoardBox.Text = settings.Shortcuts.ToggleScreenBoard;
        ToggleBlackScreenBox.Text = settings.Shortcuts.ToggleBlackScreen;
        ToggleWhiteScreenBox.Text = settings.Shortcuts.ToggleWhiteScreen;
        ExitAppBox.Text = settings.Shortcuts.ExitApp;
        ToolArrowBox.Text = settings.Shortcuts.ToolArrow;
        ToolRectangleBox.Text = settings.Shortcuts.ToolRectangle;
        ToolEllipseBox.Text = settings.Shortcuts.ToolEllipse;
        ToolLineBox.Text = settings.Shortcuts.ToolLine;
        ToolPencilBox.Text = settings.Shortcuts.ToolPencil;
        ToolHighlighterBox.Text = settings.Shortcuts.ToolHighlighter;
        ToolTextBox.Text = settings.Shortcuts.ToolText;
        ToolMoveBox.Text = settings.Shortcuts.ToolMove;
        Color1Box.Text = settings.Shortcuts.Color1;
        Color2Box.Text = settings.Shortcuts.Color2;
        Color3Box.Text = settings.Shortcuts.Color3;
        Color4Box.Text = settings.Shortcuts.Color4;
        Color5Box.Text = settings.Shortcuts.Color5;
        ThicknessDownBox.Text = settings.Shortcuts.ThicknessDown;
        ThicknessUpBox.Text = settings.Shortcuts.ThicknessUp;
        UndoBox.Text = settings.Shortcuts.Undo;
        RedoBox.Text = settings.Shortcuts.Redo;
        DeleteSelectionBox.Text = settings.Shortcuts.DeleteSelection;
        ClearBox.Text = settings.Shortcuts.Clear;
        ClearAlternateBox.Text = settings.Shortcuts.ClearAlternate;
        ExitAnnotateBox.Text = settings.Shortcuts.ExitAnnotate;
    }

    private bool TryReadShortcuts(AppSettings updated)
    {
        var shortcuts = updated.Shortcuts.Clone();

        updated.LaserHoldShortcut = ReadShortcutText(LaserHoldBox);
        shortcuts.ToggleAnnotate = ReadShortcutText(ToggleAnnotateBox);
        shortcuts.ToggleLaserActivation = ReadShortcutText(ToggleLaserActivationBox);
        shortcuts.ToggleSpotlight = ReadShortcutText(ToggleSpotlightBox);
        shortcuts.ToggleMagnifier = ReadShortcutText(ToggleMagnifierBox);
        shortcuts.TogglePinnedLens = ReadShortcutText(TogglePinnedLensBox);
        shortcuts.ToggleRegionMask = ReadShortcutText(ToggleRegionMaskBox);
        shortcuts.ClearRegionMasks = ReadShortcutText(ClearRegionMasksBox);
        shortcuts.ToggleFadingAnnotations = ReadShortcutText(ToggleFadingAnnotationsBox);
        shortcuts.ToggleToolbar = ReadShortcutText(ToggleToolbarBox);
        shortcuts.TakeScreenshot = ReadShortcutText(TakeScreenshotBox);
        shortcuts.ToggleScreenBoard = ReadShortcutText(ToggleScreenBoardBox);
        shortcuts.ToggleBlackScreen = ReadShortcutText(ToggleBlackScreenBox);
        shortcuts.ToggleWhiteScreen = ReadShortcutText(ToggleWhiteScreenBox);
        shortcuts.ExitApp = ReadShortcutText(ExitAppBox);
        shortcuts.ToolArrow = ReadShortcutText(ToolArrowBox);
        shortcuts.ToolRectangle = ReadShortcutText(ToolRectangleBox);
        shortcuts.ToolEllipse = ReadShortcutText(ToolEllipseBox);
        shortcuts.ToolLine = ReadShortcutText(ToolLineBox);
        shortcuts.ToolPencil = ReadShortcutText(ToolPencilBox);
        shortcuts.ToolHighlighter = ReadShortcutText(ToolHighlighterBox);
        shortcuts.ToolText = ReadShortcutText(ToolTextBox);
        shortcuts.ToolMove = ReadShortcutText(ToolMoveBox);
        shortcuts.Color1 = ReadShortcutText(Color1Box);
        shortcuts.Color2 = ReadShortcutText(Color2Box);
        shortcuts.Color3 = ReadShortcutText(Color3Box);
        shortcuts.Color4 = ReadShortcutText(Color4Box);
        shortcuts.Color5 = ReadShortcutText(Color5Box);
        shortcuts.ThicknessDown = ReadShortcutText(ThicknessDownBox);
        shortcuts.ThicknessUp = ReadShortcutText(ThicknessUpBox);
        shortcuts.Undo = ReadShortcutText(UndoBox);
        shortcuts.Redo = ReadShortcutText(RedoBox);
        shortcuts.DeleteSelection = ReadShortcutText(DeleteSelectionBox);
        shortcuts.Clear = ReadShortcutText(ClearBox);
        shortcuts.ClearAlternate = ReadShortcutText(ClearAlternateBox);
        shortcuts.ExitAnnotate = ReadShortcutText(ExitAnnotateBox);

        if ((updated.GetLaserActivationMode() == LaserActivationMode.Hold && !ValidateShortcut("Hold laser", updated.LaserHoldShortcut, allowMouseButton: true, allowDisabled: false))
            || !ValidateShortcut("Toggle annotate mode", shortcuts.ToggleAnnotate)
            || !ValidateShortcut("Toggle laser mode", shortcuts.ToggleLaserActivation)
            || !ValidateShortcut("Toggle spotlight", shortcuts.ToggleSpotlight)
            || !ValidateShortcut("Toggle magnifier", shortcuts.ToggleMagnifier)
            || !ValidateShortcut("Pinned lens", shortcuts.TogglePinnedLens)
            || !ValidateShortcut("Region mask", shortcuts.ToggleRegionMask)
            || !ValidateShortcut("Clear region masks", shortcuts.ClearRegionMasks)
            || !ValidateShortcut("Fading annotations", shortcuts.ToggleFadingAnnotations)
            || !ValidateShortcut("Toggle toolbar", shortcuts.ToggleToolbar)
            || !ValidateShortcut("Screenshot", shortcuts.TakeScreenshot)
            || !ValidateShortcut("Screen board", shortcuts.ToggleScreenBoard)
            || !ValidateShortcut("Black board", shortcuts.ToggleBlackScreen)
            || !ValidateShortcut("White board", shortcuts.ToggleWhiteScreen)
            || !ValidateShortcut("Exit FocusTool", shortcuts.ExitApp)
            || !ValidateShortcut("Arrow", shortcuts.ToolArrow)
            || !ValidateShortcut("Rectangle", shortcuts.ToolRectangle)
            || !ValidateShortcut("Ellipse / Circle", shortcuts.ToolEllipse)
            || !ValidateShortcut("Line", shortcuts.ToolLine)
            || !ValidateShortcut("Pencil", shortcuts.ToolPencil)
            || !ValidateShortcut("Highlighter", shortcuts.ToolHighlighter)
            || !ValidateShortcut("Text", shortcuts.ToolText)
            || !ValidateShortcut("Move selection", shortcuts.ToolMove)
            || !ValidateShortcut("Color 1", shortcuts.Color1)
            || !ValidateShortcut("Color 2", shortcuts.Color2)
            || !ValidateShortcut("Color 3", shortcuts.Color3)
            || !ValidateShortcut("Color 4", shortcuts.Color4)
            || !ValidateShortcut("Color 5", shortcuts.Color5)
            || !ValidateShortcut("Thickness down", shortcuts.ThicknessDown)
            || !ValidateShortcut("Thickness up", shortcuts.ThicknessUp)
            || !ValidateShortcut("Undo", shortcuts.Undo)
            || !ValidateShortcut("Redo", shortcuts.Redo)
            || !ValidateShortcut("Delete selection", shortcuts.DeleteSelection)
            || !ValidateShortcut("Clear", shortcuts.Clear)
            || !ValidateShortcut("Clear alternate", shortcuts.ClearAlternate)
            || !ValidateShortcut("Exit annotate", shortcuts.ExitAnnotate))
        {
            return false;
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

        if (FocusTool.Win.Native.Shortcut.TryParse(shortcut, out var parsed)
            && (allowMouseButton || !parsed.IsMouseButton))
        {
            return true;
        }

        System.Windows.MessageBox.Show(this, $"Invalid shortcut for {label}: {shortcut}", "Invalid shortcut", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private bool ValidateReservedVisualExitShortcut(ShortcutSettings shortcuts)
    {
        var globalEntries = new List<(string Label, string Text)>
        {
            ("Toggle annotate mode", shortcuts.ToggleAnnotate),
            ("Toggle laser mode", shortcuts.ToggleLaserActivation),
            ("Toggle spotlight", shortcuts.ToggleSpotlight),
            ("Toggle magnifier", shortcuts.ToggleMagnifier),
            ("Pinned lens", shortcuts.TogglePinnedLens),
            ("Region mask", shortcuts.ToggleRegionMask),
            ("Clear region masks", shortcuts.ClearRegionMasks),
            ("Fading annotations", shortcuts.ToggleFadingAnnotations),
            ("Toggle toolbar", shortcuts.ToggleToolbar),
            ("Screenshot", shortcuts.TakeScreenshot),
            ("Screen board", shortcuts.ToggleScreenBoard),
            ("Black board", shortcuts.ToggleBlackScreen),
            ("White board", shortcuts.ToggleWhiteScreen),
            ("Exit FocusTool", shortcuts.ExitApp)
        };

        foreach (var (label, text) in globalEntries)
        {
            if (ShortcutSettings.IsShortcutDisabled(text))
            {
                continue;
            }

            if (HasSameShortcut(text, ReservedVisualExitShortcut))
            {
                System.Windows.MessageBox.Show(
                    this,
                    $"{label} cannot use {ReservedVisualExitShortcut} because {ReservedVisualExitShortcut} is reserved for closing visual modes.",
                    "Shortcut conflict",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
        }

        return true;
    }

    private bool ValidateShortcutConflicts(AppSettings updated, ShortcutSettings shortcuts)
    {
        var entries = new List<(string Label, string Text)>
        {
            ("Toggle annotate mode", shortcuts.ToggleAnnotate),
            ("Toggle laser mode", shortcuts.ToggleLaserActivation),
            ("Toggle spotlight", shortcuts.ToggleSpotlight),
            ("Toggle magnifier", shortcuts.ToggleMagnifier),
            ("Pinned lens", shortcuts.TogglePinnedLens),
            ("Region mask", shortcuts.ToggleRegionMask),
            ("Clear region masks", shortcuts.ClearRegionMasks),
            ("Fading annotations", shortcuts.ToggleFadingAnnotations),
            ("Toggle toolbar", shortcuts.ToggleToolbar),
            ("Screenshot", shortcuts.TakeScreenshot),
            ("Screen board", shortcuts.ToggleScreenBoard),
            ("Black board", shortcuts.ToggleBlackScreen),
            ("White board", shortcuts.ToggleWhiteScreen),
            ("Exit FocusTool", shortcuts.ExitApp),
            ("Arrow", shortcuts.ToolArrow),
            ("Rectangle", shortcuts.ToolRectangle),
            ("Ellipse / Circle", shortcuts.ToolEllipse),
            ("Line", shortcuts.ToolLine),
            ("Pencil", shortcuts.ToolPencil),
            ("Highlighter", shortcuts.ToolHighlighter),
            ("Text", shortcuts.ToolText),
            ("Move selection", shortcuts.ToolMove),
            ("Color 1", shortcuts.Color1),
            ("Color 2", shortcuts.Color2),
            ("Color 3", shortcuts.Color3),
            ("Color 4", shortcuts.Color4),
            ("Color 5", shortcuts.Color5),
            ("Thickness down", shortcuts.ThicknessDown),
            ("Thickness up", shortcuts.ThicknessUp),
            ("Undo", shortcuts.Undo),
            ("Redo", shortcuts.Redo),
            ("Delete selection", shortcuts.DeleteSelection),
            ("Clear", shortcuts.Clear),
            ("Clear alternate", shortcuts.ClearAlternate),
            ("Exit annotate", shortcuts.ExitAnnotate)
        };

        if (updated.GetLaserActivationMode() == LaserActivationMode.Hold)
        {
            entries.Insert(0, ("Hold laser", updated.LaserHoldShortcut));
        }

        var used = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (label, text) in entries)
        {
            if (ShortcutSettings.IsShortcutDisabled(text))
            {
                continue;
            }

            if (!FocusTool.Win.Native.Shortcut.TryParse(text, out var shortcut))
            {
                continue;
            }

            var key = $"{(int)shortcut.Modifiers}:{shortcut.VirtualKey}";
            if (used.TryGetValue(key, out var existingLabel))
            {
                System.Windows.MessageBox.Show(
                    this,
                    $"Shortcut conflict: {existingLabel} and {label} both use {shortcut.DisplayText}.",
                    "Duplicate shortcut",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            used[key] = label;
        }

        return true;
    }

    private static bool HasSameShortcut(string left, string right)
    {
        return FocusTool.Win.Native.Shortcut.TryParse(left, out var leftShortcut)
            && FocusTool.Win.Native.Shortcut.TryParse(right, out var rightShortcut)
            && leftShortcut.Modifiers == rightShortcut.Modifiers
            && leftShortcut.VirtualKey == rightShortcut.VirtualKey;
    }

    private void UpdateLabels()
    {
        if (PointSizeValue is null
            || TrailLengthValue is null
            || FadeDurationValue is null
            || SpotlightRadiusValue is null
            || SpotlightOpacityValue is null
            || MagnifierRadiusValue is null
            || MagnifierZoomValue is null
            || PinnedLensZoomValue is null
            || PinnedLensRefreshFpsValue is null
            || RegionMaskOpacityValue is null
            || FadingAnnotationVisibleValue is null
            || FadingAnnotationFadeValue is null
            || AnnotationThicknessValue is null
            || AnnotationFontSizeValue is null)
        {
            return;
        }

        PointSizeValue.Text = $"{PointSizeSlider.Value:0}px";
        TrailLengthValue.Text = $"{TrailLengthSlider.Value:0} ms";
        FadeDurationValue.Text = $"{FadeDurationSlider.Value:0} ms";
        SpotlightRadiusValue.Text = $"{SpotlightRadiusSlider.Value:0}px";
        SpotlightOpacityValue.Text = $"{SpotlightOpacitySlider.Value:0}%";
        MagnifierRadiusValue.Text = $"{MagnifierRadiusSlider.Value:0}px";
        MagnifierZoomValue.Text = $"{MagnifierZoomSlider.Value:0.##}x";
        PinnedLensZoomValue.Text = $"{PinnedLensZoomSlider.Value:0.##}x";
        PinnedLensRefreshFpsValue.Text = $"{PinnedLensRefreshFpsSlider.Value:0} fps";
        RegionMaskOpacityValue.Text = $"{RegionMaskOpacitySlider.Value:0}%";
        FadingAnnotationVisibleValue.Text = $"{FadingAnnotationVisibleSlider.Value:0.#}s";
        FadingAnnotationFadeValue.Text = $"{FadingAnnotationFadeSlider.Value:0.#}s";
        AnnotationThicknessValue.Text = $"{AnnotationThicknessSlider.Value:0}px";
        AnnotationFontSizeValue.Text = $"{AnnotationFontSizeSlider.Value:0}px";
    }

    private void UpdateAnnotationPreset5Preview()
    {
        if (AnnotationPreset5Box is null || AnnotationPreset4Button is null)
        {
            return;
        }

        if (AppSettings.TryParseColor(AnnotationPreset5Box.Text.Trim(), out var color))
        {
            AnnotationPreset4Button.Background = new SolidColorBrush(color);
            AnnotationPreset5Box.ClearValue(BorderBrushProperty);
        }
        else
        {
            AnnotationPreset4Button.Background = System.Windows.Media.Brushes.Transparent;
            AnnotationPreset5Box.BorderBrush = System.Windows.Media.Brushes.Firebrick;
        }
    }

    private void UpdateRegionMaskColorPreview()
    {
        if (RegionMaskColorBox is null || RegionMaskColorPreview is null)
        {
            return;
        }

        if (AppSettings.TryParseColor(RegionMaskColorBox.Text.Trim(), out var color))
        {
            RegionMaskColorPreview.Background = new SolidColorBrush(color);
            RegionMaskColorBox.ClearValue(BorderBrushProperty);
        }
        else
        {
            RegionMaskColorPreview.Background = System.Windows.Media.Brushes.Transparent;
            RegionMaskColorBox.BorderBrush = System.Windows.Media.Brushes.Firebrick;
        }
    }

    private void UpdateLaserPresetSelection()
    {
        UpdateSwatchSelection(GetLaserPresetButtons(), _selectedLaserPresetIndex);
    }

    private void UpdateAnnotationPresetSelection()
    {
        UpdateSwatchSelection(GetAnnotationPresetButtons(), _selectedAnnotationPresetIndex);
    }

    private static void UpdateSwatchSelection(IReadOnlyList<System.Windows.Controls.Button> buttons, int selectedIndex)
    {
        var selected = new SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 32, 32));
        var normal = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, 0, 0, 0));

        for (var i = 0; i < buttons.Count; i++)
        {
            buttons[i].BorderBrush = i == selectedIndex ? selected : normal;
            buttons[i].Opacity = i == selectedIndex ? 1.0 : 0.92;
        }
    }

    private System.Windows.Controls.Button[] GetLaserPresetButtons() =>
    [
        LaserPreset0Button,
        LaserPreset1Button,
        LaserPreset2Button,
        LaserPreset3Button,
        LaserPreset4Button,
        LaserPreset5Button,
        LaserPreset6Button
    ];

    private System.Windows.Controls.Button[] GetAnnotationPresetButtons() =>
    [
        AnnotationPreset0Button,
        AnnotationPreset1Button,
        AnnotationPreset2Button,
        AnnotationPreset3Button,
        AnnotationPreset4Button
    ];

    private static string GetPresetOrDefault(string color, IReadOnlyList<string> presets, out int index)
    {
        if (TryFindPreset(color, presets, out index))
        {
            return presets[index];
        }

        if (AppSettings.TryParseColor(color, out _))
        {
            index = -1;
            return color;
        }

        index = 0;
        return presets[0];
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

    private static string[] GetAnnotationPresetValues(AppSettings settings)
    {
        var values = new[]
        {
            "#FFFF2020",
            "#FF00D26A",
            "#FF2080FF",
            "#FFFFD400",
            "#FFFFFFFF"
        };

        for (var i = 0; i < values.Length && i < settings.AnnotationColorPresets.Count; i++)
        {
            if (AppSettings.TryParseColor(settings.AnnotationColorPresets[i], out _))
            {
                values[i] = settings.AnnotationColorPresets[i];
            }
        }

        return values;
    }

    private void SelectLaserActivationMode(LaserActivationMode mode)
    {
        foreach (var item in LaserActivationModeBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag?.ToString()?.Equals(mode.ToString(), StringComparison.OrdinalIgnoreCase) == true)
            {
                LaserActivationModeBox.SelectedItem = item;
                return;
            }
        }

        LaserActivationModeBox.SelectedIndex = 0;
    }

    private LaserActivationMode ReadLaserActivationMode()
    {
        if (LaserActivationModeBox.SelectedItem is ComboBoxItem item
            && Enum.TryParse<LaserActivationMode>(item.Tag?.ToString(), true, out var mode))
        {
            return mode;
        }

        return LaserActivationMode.Hold;
    }

    private void UpdateLaserHoldFieldState()
    {
        if (LaserHoldBox is null)
        {
            return;
        }

        var holdMode = ReadLaserActivationMode() == LaserActivationMode.Hold;
        LaserHoldBox.Opacity = holdMode ? 1 : 0.78;
    }

    private void ApplyAnnotationPresetBrushes(IReadOnlyList<string> colors)
    {
        var buttons = new[]
        {
            AnnotationPreset0Button,
            AnnotationPreset1Button,
            AnnotationPreset2Button,
            AnnotationPreset3Button,
            AnnotationPreset4Button
        };

        for (var i = 0; i < buttons.Length; i++)
        {
            if (i < colors.Count && AppSettings.TryParseColor(colors[i], out var color))
            {
                buttons[i].Background = new SolidColorBrush(color);
            }
        }
    }
}
