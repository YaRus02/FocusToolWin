using System.Windows;
using System.Windows.Controls;
using FocusTool.Win.Models;

namespace FocusTool.Win;

public partial class SettingsWindow : Window
{
    private readonly Action<AppSettings> _applySettings;
    private readonly string _settingsPath;
    private AppSettings _settings;
    private bool _loading = true;

    private readonly SettingsColorSlotBinder _laserSlot;
    private readonly SettingsColorSlotBinder _cursorHighlightSlot;
    private readonly SettingsColorSlotBinder _annotationSlot;
    private readonly SettingsColorSlotBinder _regionMaskSlot;
    private readonly SettingsShortcutFieldsBinder _shortcuts;

    public SettingsWindow(AppSettings settings, Action<AppSettings> applySettings, string settingsPath)
    {
        _settings = settings;
        _applySettings = applySettings;
        _settingsPath = settingsPath;
        InitializeComponent();

        _laserSlot = new SettingsColorSlotBinder(
            [LaserPreset0Button, LaserPreset1Button, LaserPreset2Button, LaserPreset3Button, LaserPreset4Button],
            LaserColorBox, LaserColorPreview, fallbackIndex: 4, () => _loading, v => _loading = v);
        _cursorHighlightSlot = new SettingsColorSlotBinder(
            [CursorHighlightPreset0Button, CursorHighlightPreset1Button, CursorHighlightPreset2Button, CursorHighlightPreset3Button, CursorHighlightPreset4Button],
            CursorHighlightColorBox, CursorHighlightColorPreview, fallbackIndex: 0, () => _loading, v => _loading = v);
        _annotationSlot = new SettingsColorSlotBinder(
            [AnnotationPreset0Button, AnnotationPreset1Button, AnnotationPreset2Button, AnnotationPreset3Button, AnnotationPreset4Button],
            AnnotationPreset5Box, preview: null, fallbackIndex: 4, () => _loading, v => _loading = v);
        _regionMaskSlot = new SettingsColorSlotBinder(
            [RegionMaskPreset0Button, RegionMaskPreset1Button, RegionMaskPreset2Button, RegionMaskPreset3Button, RegionMaskPreset4Button],
            RegionMaskColorBox, RegionMaskColorPreview, fallbackIndex: 4, () => _loading, v => _loading = v);
        _shortcuts = new SettingsShortcutFieldsBinder(
            BuildShortcutBindings(),
            LaserHoldBox,
            CursorHighlightHoldBox,
            (title, message) => System.Windows.MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning));

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
        SelectCursorHighlightActivationMode(settings.GetCursorHighlightActivationMode());
        CursorHighlightRadiusSlider.Value = settings.CursorHighlightRadius;
        CursorHighlightThicknessSlider.Value = settings.CursorHighlightThickness;
        CursorHighlightClickPulseCheckBox.IsChecked = settings.ClickPulseEnabled;
        SpotlightRadiusSlider.Value = settings.SpotlightRadius;
        SpotlightOpacitySlider.Value = settings.SpotlightOpacity * 100;
        MagnifierRadiusSlider.Value = settings.MagnifierRadius;
        MagnifierZoomSlider.Value = settings.MagnifierZoom;
        PinnedLensZoomSlider.Value = settings.PinnedLensZoom;
        PinnedLensRefreshFpsSlider.Value = settings.PinnedLensRefreshFps;
        RegionMaskOpacitySlider.Value = settings.RegionMaskOpacity * 100;
        SelectRegionMaskStyle(settings.RegionMaskStyle);
        FadingAnnotationsCheckBox.IsChecked = settings.FadingAnnotationsEnabled;
        FadingAnnotationVisibleSlider.Value = settings.FadingAnnotationVisibleMs / 1000.0;
        FadingAnnotationFadeSlider.Value = settings.FadingAnnotationFadeMs / 1000.0;

        _laserSlot.Load(settings.Color, settings.LaserColorPresets, AppSettings.DefaultLaserColorPresets());
        _cursorHighlightSlot.Load(settings.CursorHighlightColor, settings.CursorHighlightColorPresets, AppSettings.DefaultCursorHighlightColorPresets());
        _regionMaskSlot.Load(settings.RegionMaskColor, settings.RegionMaskColorPresets, AppSettings.DefaultRegionMaskColorPresets());
        _annotationSlot.Load(settings.AnnotationColor, settings.AnnotationColorPresets, AppSettings.DefaultAnnotationColorPresets());

        AnnotationThicknessSlider.Value = settings.AnnotationThickness;
        AnnotationFontSizeSlider.Value = settings.AnnotationFontSize;
        SettingsPathText.Text = $"JSON: {settingsPath}";
        _shortcuts.Load(settings);

        _loading = false;
        UpdateLabels();
        _laserSlot.RefreshSwatches();
        _cursorHighlightSlot.RefreshSwatches();
        _annotationSlot.RefreshSwatches();
        _regionMaskSlot.RefreshSwatches();
        UpdateLaserHoldFieldState();
        UpdateCursorHighlightHoldFieldState();
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
        if (TryGetPresetIndex(sender, out var index))
        {
            _laserSlot.SelectPreset(index);
        }
    }

    private void AnnotationPreset_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryGetPresetIndex(sender, out var index))
        {
            _annotationSlot.SelectPreset(index);
        }
    }

    private void CursorHighlightPreset_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryGetPresetIndex(sender, out var index))
        {
            _cursorHighlightSlot.SelectPreset(index);
        }
    }

    private void MaskColorPreset_OnClick(object sender, RoutedEventArgs e)
    {
        if (TryGetPresetIndex(sender, out var index))
        {
            _regionMaskSlot.SelectPreset(index);
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
        _annotationSlot.OnHexChanged();
    }

    private void RegionMaskColorBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _regionMaskSlot.OnHexChanged();
    }

    private void LaserColorBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _laserSlot.OnHexChanged();
    }

    private void CursorHighlightColorBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _cursorHighlightSlot.OnHexChanged();
    }

    private void LaserActivationMode_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            UpdateLaserHoldFieldState();
        }
    }

    private void CursorHighlightActivationMode_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            UpdateCursorHighlightHoldFieldState();
        }
    }

    private bool TryApply()
    {
        if (!_laserSlot.IsValid())
        {
            ShowInvalidColor("laser");
            return false;
        }

        if (!_annotationSlot.IsValid())
        {
            ShowInvalidColor("annotation");
            return false;
        }

        if (!_regionMaskSlot.IsValid())
        {
            ShowInvalidColor("region mask");
            return false;
        }

        if (!_cursorHighlightSlot.IsValid())
        {
            ShowInvalidColor("cursor highlight");
            return false;
        }

        var updated = _settings.Clone();
        _laserSlot.Commit(c => updated.Color = c, updated.LaserColorPresets);
        _cursorHighlightSlot.Commit(c => updated.CursorHighlightColor = c, updated.CursorHighlightColorPresets);
        _annotationSlot.Commit(c => updated.AnnotationColor = c, updated.AnnotationColorPresets);
        _regionMaskSlot.Commit(c => updated.RegionMaskColor = c, updated.RegionMaskColorPresets);

        updated.PointSize = PointSizeSlider.Value;
        updated.TrailLengthMs = (int)TrailLengthSlider.Value;
        updated.FadeDurationMs = (int)FadeDurationSlider.Value;
        updated.GlowEnabled = GlowCheckBox.IsChecked == true;
        updated.SetLaserActivationMode(ReadLaserActivationMode());
        updated.SetCursorHighlightActivationMode(ReadCursorHighlightActivationMode());
        updated.CursorHighlightRadius = CursorHighlightRadiusSlider.Value;
        updated.CursorHighlightThickness = CursorHighlightThicknessSlider.Value;
        updated.ClickPulseEnabled = CursorHighlightClickPulseCheckBox.IsChecked == true;
        updated.SpotlightRadius = SpotlightRadiusSlider.Value;
        updated.SpotlightOpacity = SpotlightOpacitySlider.Value / 100.0;
        updated.MagnifierRadius = MagnifierRadiusSlider.Value;
        updated.MagnifierZoom = MagnifierZoomSlider.Value;
        updated.PinnedLensZoom = PinnedLensZoomSlider.Value;
        updated.PinnedLensRefreshFps = (int)PinnedLensRefreshFpsSlider.Value;
        updated.RegionMaskOpacity = RegionMaskOpacitySlider.Value / 100.0;
        updated.RegionMaskStyle = ReadRegionMaskStyle().ToString();
        updated.FadingAnnotationsEnabled = FadingAnnotationsCheckBox.IsChecked == true;
        updated.FadingAnnotationVisibleMs = (int)Math.Round(FadingAnnotationVisibleSlider.Value * 1000);
        updated.FadingAnnotationFadeMs = (int)Math.Round(FadingAnnotationFadeSlider.Value * 1000);
        updated.SetAnnotationThicknessForTool(updated.GetAnnotationTool(), AnnotationThicknessSlider.Value);
        updated.AnnotationFontSize = AnnotationFontSizeSlider.Value;

        if (!_shortcuts.TryRead(updated))
        {
            return false;
        }

        updated.Normalize();

        _settings = updated.Clone();
        _applySettings(updated);
        return true;
    }

    private void ShowInvalidColor(string label)
    {
        System.Windows.MessageBox.Show(
            this,
            $"Use #AARRGGBB or #RRGGBB for the {label} color.",
            "Invalid color",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static bool TryGetPresetIndex(object sender, out int index)
    {
        index = -1;
        return sender is System.Windows.Controls.Button button && int.TryParse(button.Tag?.ToString(), out index);
    }

    private List<SettingsShortcutFieldBinding> BuildShortcutBindings() =>
    [
        new(ToggleAnnotateBox, "Toggle annotate mode", s => s.ToggleAnnotate, (s, v) => s.ToggleAnnotate = v, true),
        new(PushToAnnotateBox, "Push annotate", s => s.PushToAnnotate, (s, v) => s.PushToAnnotate = v, true),
        new(ToggleLaserActivationBox, "Toggle laser mode", s => s.ToggleLaserActivation, (s, v) => s.ToggleLaserActivation = v, true),
        new(ToggleCursorHighlightBox, "Cursor highlight", s => s.ToggleCursorHighlight, (s, v) => s.ToggleCursorHighlight = v, true),
        new(ToggleSpotlightBox, "Toggle spotlight", s => s.ToggleSpotlight, (s, v) => s.ToggleSpotlight = v, true),
        new(ToggleMagnifierBox, "Toggle magnifier", s => s.ToggleMagnifier, (s, v) => s.ToggleMagnifier = v, true),
        new(TogglePinnedLensBox, "Pinned lens", s => s.TogglePinnedLens, (s, v) => s.TogglePinnedLens = v, true),
        new(ToggleRegionMaskBox, "Region mask", s => s.ToggleRegionMask, (s, v) => s.ToggleRegionMask = v, true),
        new(ClearRegionMasksBox, "Clear region masks", s => s.ClearRegionMasks, (s, v) => s.ClearRegionMasks = v, true),
        new(ToggleRegionSpotlightBox, "Region spotlight", s => s.ToggleRegionSpotlight, (s, v) => s.ToggleRegionSpotlight = v, true),
        new(ClearRegionSpotlightsBox, "Clear region spotlights", s => s.ClearRegionSpotlights, (s, v) => s.ClearRegionSpotlights = v, true),
        new(ToggleFadingAnnotationsBox, "Fading annotations", s => s.ToggleFadingAnnotations, (s, v) => s.ToggleFadingAnnotations = v, true),
        new(ToggleTimerBox, "Timer", s => s.ToggleTimer, (s, v) => s.ToggleTimer = v, true),
        new(ToggleToolbarBox, "Toggle toolbar", s => s.ToggleToolbar, (s, v) => s.ToggleToolbar = v, true),
        new(TakeScreenshotBox, "Screenshot", s => s.TakeScreenshot, (s, v) => s.TakeScreenshot = v, true),
        new(TakeRegionScreenshotBox, "Screenshot region", s => s.TakeRegionScreenshot, (s, v) => s.TakeRegionScreenshot = v, true),
        new(ToggleScreenBoardBox, "Screen board", s => s.ToggleScreenBoard, (s, v) => s.ToggleScreenBoard = v, true),
        new(ToggleBlackScreenBox, "Black board", s => s.ToggleBlackScreen, (s, v) => s.ToggleBlackScreen = v, true),
        new(ToggleWhiteScreenBox, "White board", s => s.ToggleWhiteScreen, (s, v) => s.ToggleWhiteScreen = v, true),
        new(ExitAppBox, "Exit FocusTool", s => s.ExitApp, (s, v) => s.ExitApp = v, true),
        new(ToolArrowBox, "Arrow", s => s.ToolArrow, (s, v) => s.ToolArrow = v, false),
        new(ToolRectangleBox, "Rectangle", s => s.ToolRectangle, (s, v) => s.ToolRectangle = v, false),
        new(ToolEllipseBox, "Ellipse / Circle", s => s.ToolEllipse, (s, v) => s.ToolEllipse = v, false),
        new(ToolLineBox, "Line", s => s.ToolLine, (s, v) => s.ToolLine = v, false),
        new(ToolPencilBox, "Pencil", s => s.ToolPencil, (s, v) => s.ToolPencil = v, false),
        new(ToolHighlighterBox, "Highlighter", s => s.ToolHighlighter, (s, v) => s.ToolHighlighter = v, false),
        new(ToolTextBox, "Text", s => s.ToolText, (s, v) => s.ToolText = v, false),
        new(ToolMoveBox, "Move selection", s => s.ToolMove, (s, v) => s.ToolMove = v, false),
        new(ToolStepBox, "Step marker", s => s.ToolStep, (s, v) => s.ToolStep = v, false),
        new(Color1Box, "Color 1", s => s.Color1, (s, v) => s.Color1 = v, false),
        new(Color2Box, "Color 2", s => s.Color2, (s, v) => s.Color2 = v, false),
        new(Color3Box, "Color 3", s => s.Color3, (s, v) => s.Color3 = v, false),
        new(Color4Box, "Color 4", s => s.Color4, (s, v) => s.Color4 = v, false),
        new(Color5Box, "Color 5", s => s.Color5, (s, v) => s.Color5 = v, false),
        new(ThicknessDownBox, "Thickness down", s => s.ThicknessDown, (s, v) => s.ThicknessDown = v, false),
        new(ThicknessUpBox, "Thickness up", s => s.ThicknessUp, (s, v) => s.ThicknessUp = v, false),
        new(UndoBox, "Undo", s => s.Undo, (s, v) => s.Undo = v, false),
        new(RedoBox, "Redo", s => s.Redo, (s, v) => s.Redo = v, false),
        new(DeleteSelectionBox, "Delete selection", s => s.DeleteSelection, (s, v) => s.DeleteSelection = v, false),
        new(ClearBox, "Clear", s => s.Clear, (s, v) => s.Clear = v, false),
        new(ClearAlternateBox, "Clear alternate", s => s.ClearAlternate, (s, v) => s.ClearAlternate = v, false),
        new(ExitAnnotateBox, "Exit annotate", s => s.ExitAnnotate, (s, v) => s.ExitAnnotate = v, false),
    ];

    private void UpdateLabels()
    {
        if (PointSizeValue is null
            || TrailLengthValue is null
            || FadeDurationValue is null
            || CursorHighlightRadiusValue is null
            || CursorHighlightThicknessValue is null
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
        CursorHighlightRadiusValue.Text = $"{CursorHighlightRadiusSlider.Value:0}px";
        CursorHighlightThicknessValue.Text = $"{CursorHighlightThicknessSlider.Value:0}px";
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

    private void SelectCursorHighlightActivationMode(LaserActivationMode mode)
    {
        foreach (var item in CursorHighlightActivationModeBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag?.ToString()?.Equals(mode.ToString(), StringComparison.OrdinalIgnoreCase) == true)
            {
                CursorHighlightActivationModeBox.SelectedItem = item;
                return;
            }
        }

        CursorHighlightActivationModeBox.SelectedIndex = 0;
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

    private LaserActivationMode ReadCursorHighlightActivationMode()
    {
        if (CursorHighlightActivationModeBox.SelectedItem is ComboBoxItem item
            && Enum.TryParse<LaserActivationMode>(item.Tag?.ToString(), true, out var mode))
        {
            return mode;
        }

        return LaserActivationMode.Hold;
    }

    private void SelectRegionMaskStyle(string styleText)
    {
        var style = Enum.TryParse<RegionMaskStyle>(styleText, true, out var parsed)
            ? parsed
            : RegionMaskStyle.StripesWithLabel;
        foreach (var item in RegionMaskStyleBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag?.ToString()?.Equals(style.ToString(), StringComparison.OrdinalIgnoreCase) == true)
            {
                RegionMaskStyleBox.SelectedItem = item;
                return;
            }
        }

        RegionMaskStyleBox.SelectedIndex = 3;
    }

    private RegionMaskStyle ReadRegionMaskStyle()
    {
        if (RegionMaskStyleBox.SelectedItem is ComboBoxItem item
            && Enum.TryParse<RegionMaskStyle>(item.Tag?.ToString(), true, out var style))
        {
            return style;
        }

        return RegionMaskStyle.StripesWithLabel;
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

    private void UpdateCursorHighlightHoldFieldState()
    {
        if (CursorHighlightHoldBox is null)
        {
            return;
        }

        var holdMode = ReadCursorHighlightActivationMode() == LaserActivationMode.Hold;
        CursorHighlightHoldBox.Opacity = holdMode ? 1 : 0.78;
    }
}
