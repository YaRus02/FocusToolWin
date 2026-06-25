using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FocusTool.Win.Models;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace FocusTool.Win;

public partial class SettingsWindow : Window
{
    private const string ReservedVisualExitShortcut = "Esc";

    private readonly Action<AppSettings> _applySettings;
    private readonly string _settingsPath;
    private AppSettings _settings;
    private bool _loading = true;
    private readonly string[] _laserPresets = new string[5];
    private readonly string[] _annotationPresets = new string[5];
    private readonly string[] _regionMaskPresets = new string[5];
    private string _laserColor = "#FFFF2020";
    private string _annotationColor = "#FFFF2020";
    private string _regionMaskColor = "#FF000000";
    private int _selectedLaserPresetIndex;
    private int _selectedAnnotationPresetIndex;
    private int _selectedRegionMaskPresetIndex;

    public SettingsWindow(AppSettings settings, Action<AppSettings> applySettings, string settingsPath)
    {
        _settings = settings;
        _applySettings = applySettings;
        _settingsPath = settingsPath;
        InitializeComponent();
        LoadSettings(settings, settingsPath);
    }

    private void LoadSettings(AppSettings settings, string settingsPath)
    {
        _loading = true;

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
        RegionMaskOpacitySlider.Value = settings.RegionMaskOpacity * 100;
        FadingAnnotationsCheckBox.IsChecked = settings.FadingAnnotationsEnabled;
        FadingAnnotationVisibleSlider.Value = settings.FadingAnnotationVisibleMs / 1000.0;
        FadingAnnotationFadeSlider.Value = settings.FadingAnnotationFadeMs / 1000.0;

        var laserPresets = GetPresetValues(settings.LaserColorPresets, AppSettings.DefaultLaserColorPresets());
        Array.Copy(laserPresets, _laserPresets, _laserPresets.Length);
        SelectColorSlot(settings.Color, _laserPresets, ref _selectedLaserPresetIndex, fallbackIndex: 4);
        _laserColor = _laserPresets[_selectedLaserPresetIndex];
        LaserColorBox.Text = _laserPresets[_selectedLaserPresetIndex];

        var regionMaskPresets = GetPresetValues(settings.RegionMaskColorPresets, AppSettings.DefaultRegionMaskColorPresets());
        Array.Copy(regionMaskPresets, _regionMaskPresets, _regionMaskPresets.Length);
        SelectColorSlot(settings.RegionMaskColor, _regionMaskPresets, ref _selectedRegionMaskPresetIndex, fallbackIndex: 4);
        _regionMaskColor = _regionMaskPresets[_selectedRegionMaskPresetIndex];
        RegionMaskColorBox.Text = _regionMaskPresets[_selectedRegionMaskPresetIndex];

        var annotationPresets = GetPresetValues(settings.AnnotationColorPresets, AppSettings.DefaultAnnotationColorPresets());
        Array.Copy(annotationPresets, _annotationPresets, _annotationPresets.Length);
        SelectColorSlot(settings.AnnotationColor, _annotationPresets, ref _selectedAnnotationPresetIndex, fallbackIndex: 4);
        _annotationColor = _annotationPresets[_selectedAnnotationPresetIndex];
        AnnotationPreset5Box.Text = _annotationPresets[_selectedAnnotationPresetIndex];
        AnnotationThicknessSlider.Value = settings.AnnotationThickness;
        AnnotationFontSizeSlider.Value = settings.AnnotationFontSize;
        SettingsPathText.Text = $"JSON: {settingsPath}";
        LoadShortcutFields(settings);

        ApplyPresetBrushes(GetLaserPresetButtons(), _laserPresets);
        ApplyPresetBrushes(GetRegionMaskPresetButtons(), _regionMaskPresets);
        ApplyPresetBrushes(GetAnnotationPresetButtons(), _annotationPresets);

        _loading = false;
        UpdateLabels();
        UpdateSelectedLaserSwatch();
        UpdateSelectedAnnotationSwatch();
        UpdateSelectedRegionMaskSwatch();
        UpdateLaserPresetSelection();
        UpdateAnnotationPresetSelection();
        UpdateRegionMaskPresetSelection();
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

    private void RestoreDefaults_OnClick(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            "Reset all settings (including shortcuts) to their defaults?\n\nThis only fills the form - nothing is applied until you click OK or Apply.",
            "Restore defaults",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.OK)
        {
            return;
        }

        var defaults = new AppSettings();
        defaults.Normalize();
        LoadSettings(defaults, _settingsPath);
    }

    private void LaserPreset_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button
            || !int.TryParse(button.Tag?.ToString(), out var index)
            || index < 0 || index >= _laserPresets.Length)
        {
            return;
        }

        _selectedLaserPresetIndex = index;
        _laserColor = _laserPresets[index];
        _loading = true;
        LaserColorBox.Text = _laserPresets[index];
        _loading = false;
        UpdateSelectedLaserSwatch();
        UpdateLaserPresetSelection();
    }

    private void AnnotationPreset_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button
            || !int.TryParse(button.Tag?.ToString(), out var index)
            || index < 0 || index >= _annotationPresets.Length)
        {
            return;
        }

        // Select this slot as the active colour and load it into the editable hex box.
        _selectedAnnotationPresetIndex = index;
        _annotationColor = _annotationPresets[index];
        _loading = true;
        AnnotationPreset5Box.Text = _annotationPresets[index];
        _loading = false;
        UpdateSelectedAnnotationSwatch();
        UpdateAnnotationPresetSelection();
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
        if (_loading)
        {
            return;
        }

        // The hex box edits whichever palette slot is selected; it is also the active colour.
        var hex = AnnotationPreset5Box.Text.Trim();
        if (AppSettings.TryParseColor(hex, out _))
        {
            _annotationPresets[_selectedAnnotationPresetIndex] = hex;
            _annotationColor = hex;
        }

        UpdateSelectedAnnotationSwatch();
    }

    private void RegionMaskColorBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading)
        {
            var hex = RegionMaskColorBox.Text.Trim();
            if (AppSettings.TryParseColor(hex, out _))
            {
                _regionMaskPresets[_selectedRegionMaskPresetIndex] = hex;
                _regionMaskColor = hex;
            }

            UpdateSelectedRegionMaskSwatch();
        }
    }

    private void LaserColorBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading)
        {
            var hex = LaserColorBox.Text.Trim();
            if (AppSettings.TryParseColor(hex, out _))
            {
                _laserPresets[_selectedLaserPresetIndex] = hex;
                _laserColor = hex;
            }

            UpdateSelectedLaserSwatch();
        }
    }

    private void MaskColorPreset_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button
            || !int.TryParse(button.Tag?.ToString(), out var index)
            || index < 0 || index >= _regionMaskPresets.Length)
        {
            return;
        }

        _selectedRegionMaskPresetIndex = index;
        _regionMaskColor = _regionMaskPresets[index];
        _loading = true;
        RegionMaskColorBox.Text = _regionMaskPresets[index];
        _loading = false;
        UpdateSelectedRegionMaskSwatch();
        UpdateRegionMaskPresetSelection();
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
        if (!AppSettings.TryParseColor(LaserColorBox.Text.Trim(), out _))
        {
            System.Windows.MessageBox.Show(this, "Use #AARRGGBB or #RRGGBB for the laser color.", "Invalid color", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!AppSettings.TryParseColor(AnnotationPreset5Box.Text.Trim(), out _))
        {
            System.Windows.MessageBox.Show(this, "Use #AARRGGBB or #RRGGBB for the annotation color.", "Invalid color", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!AppSettings.TryParseColor(RegionMaskColorBox.Text.Trim(), out _))
        {
            System.Windows.MessageBox.Show(this, "Use #AARRGGBB or #RRGGBB for the region mask color.", "Invalid color", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _laserPresets[_selectedLaserPresetIndex] = LaserColorBox.Text.Trim();
        _laserColor = _laserPresets[_selectedLaserPresetIndex];
        _annotationPresets[_selectedAnnotationPresetIndex] = AnnotationPreset5Box.Text.Trim();
        _annotationColor = _annotationPresets[_selectedAnnotationPresetIndex];
        _regionMaskPresets[_selectedRegionMaskPresetIndex] = RegionMaskColorBox.Text.Trim();
        _regionMaskColor = _regionMaskPresets[_selectedRegionMaskPresetIndex];

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
        updated.RegionMaskColor = _regionMaskColor;
        updated.RegionMaskOpacity = RegionMaskOpacitySlider.Value / 100.0;
        updated.FadingAnnotationsEnabled = FadingAnnotationsCheckBox.IsChecked == true;
        updated.FadingAnnotationVisibleMs = (int)Math.Round(FadingAnnotationVisibleSlider.Value * 1000);
        updated.FadingAnnotationFadeMs = (int)Math.Round(FadingAnnotationFadeSlider.Value * 1000);
        updated.AnnotationColor = _annotationColor;
        WritePresetValues(updated.LaserColorPresets, _laserPresets);
        WritePresetValues(updated.AnnotationColorPresets, _annotationPresets);
        WritePresetValues(updated.RegionMaskColorPresets, _regionMaskPresets);

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
        ToggleTimerBox.Text = settings.Shortcuts.ToggleTimer;
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
        ToolStepBox.Text = settings.Shortcuts.ToolStep;
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
        shortcuts.ToggleTimer = ReadShortcutText(ToggleTimerBox);
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
        shortcuts.ToolStep = ReadShortcutText(ToolStepBox);
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
            || !ValidateShortcut("Timer", shortcuts.ToggleTimer)
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
            || !ValidateShortcut("Step marker", shortcuts.ToolStep)
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
            ("Timer", shortcuts.ToggleTimer),
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
            ("Timer", shortcuts.ToggleTimer),
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
            ("Step marker", shortcuts.ToolStep),
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

    private void UpdateSelectedLaserSwatch()
    {
        UpdateSelectedSwatch(LaserColorBox, LaserColorPreview, GetLaserPresetButtons(), _selectedLaserPresetIndex);
    }

    private void UpdateSelectedAnnotationSwatch()
    {
        UpdateSelectedSwatch(AnnotationPreset5Box, preview: null, GetAnnotationPresetButtons(), _selectedAnnotationPresetIndex);
    }

    private void UpdateSelectedRegionMaskSwatch()
    {
        UpdateSelectedSwatch(RegionMaskColorBox, RegionMaskColorPreview, GetRegionMaskPresetButtons(), _selectedRegionMaskPresetIndex);
    }

    private static void UpdateSelectedSwatch(
        WpfTextBox textBox,
        Border? preview,
        IReadOnlyList<System.Windows.Controls.Button> buttons,
        int selectedIndex)
    {
        if (textBox is null || selectedIndex < 0 || selectedIndex >= buttons.Count)
        {
            return;
        }

        if (AppSettings.TryParseColor(textBox.Text.Trim(), out var color))
        {
            buttons[selectedIndex].Background = new SolidColorBrush(color);
            if (preview is not null)
            {
                preview.Background = new SolidColorBrush(color);
            }

            textBox.ClearValue(BorderBrushProperty);
        }
        else
        {
            if (preview is not null)
            {
                preview.Background = System.Windows.Media.Brushes.Transparent;
            }

            textBox.BorderBrush = System.Windows.Media.Brushes.Firebrick;
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

    private void UpdateRegionMaskPresetSelection()
    {
        UpdateSwatchSelection(GetRegionMaskPresetButtons(), _selectedRegionMaskPresetIndex);
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
        LaserPreset4Button
    ];

    private System.Windows.Controls.Button[] GetAnnotationPresetButtons() =>
    [
        AnnotationPreset0Button,
        AnnotationPreset1Button,
        AnnotationPreset2Button,
        AnnotationPreset3Button,
        AnnotationPreset4Button
    ];

    private System.Windows.Controls.Button[] GetRegionMaskPresetButtons() =>
    [
        RegionMaskPreset0Button,
        RegionMaskPreset1Button,
        RegionMaskPreset2Button,
        RegionMaskPreset3Button,
        RegionMaskPreset4Button
    ];

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

    private static void ApplyPresetBrushes(IReadOnlyList<System.Windows.Controls.Button> buttons, IReadOnlyList<string> colors)
    {
        for (var i = 0; i < buttons.Count; i++)
        {
            if (i < colors.Count && AppSettings.TryParseColor(colors[i], out var color))
            {
                buttons[i].Background = new SolidColorBrush(color);
            }
        }
    }
}
