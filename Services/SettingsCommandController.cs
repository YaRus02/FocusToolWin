using FocusTool.Win.Models;

namespace FocusTool.Win.Services;

internal sealed class SettingsCommandController
{
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Action<AppSettings> _applySettings;
    private readonly Func<InteractionMode> _modeProvider;
    private readonly Action<InteractionMode> _setMode;
    private readonly AnnotationDocument _annotations;
    private readonly RegionMaskController _regionMasks;
    private readonly VisualEffectsController _visualEffects;
    private readonly Action _invalidateOverlay;
    private AnnotationTool _lastStepTool = AnnotationTool.StepOval;

    public SettingsCommandController(
        Func<AppSettings> settingsProvider,
        Action<AppSettings> applySettings,
        Func<InteractionMode> modeProvider,
        Action<InteractionMode> setMode,
        AnnotationDocument annotations,
        RegionMaskController regionMasks,
        VisualEffectsController visualEffects,
        Action invalidateOverlay)
    {
        _settingsProvider = settingsProvider;
        _applySettings = applySettings;
        _modeProvider = modeProvider;
        _setMode = setMode;
        _annotations = annotations;
        _regionMasks = regionMasks;
        _visualEffects = visualEffects;
        _invalidateOverlay = invalidateOverlay;

        var currentTool = _settingsProvider().GetAnnotationTool();
        if (IsStepTool(currentTool))
        {
            _lastStepTool = currentTool;
        }
    }

    public void SetCursorHighlightEnabled(bool enabled)
    {
        SetCursorHighlightActivationMode(enabled ? LaserActivationMode.Always : LaserActivationMode.Hold);
    }

    public void SetCursorHighlightActivationMode(LaserActivationMode mode)
    {
        var settings = _settingsProvider();
        if (settings.GetCursorHighlightActivationMode() == mode)
        {
            return;
        }

        Update(updated => updated.SetCursorHighlightActivationMode(mode));
    }

    public void SetCursorHighlightPresetColor(int index)
    {
        var settings = _settingsProvider();
        if (index < 0 || index >= settings.CursorHighlightColorPresets.Count)
        {
            return;
        }

        Update(updated => updated.CursorHighlightColor = settings.CursorHighlightColorPresets[index]);
    }

    public void SetClickPulseEnabled(bool enabled)
    {
        var settings = _settingsProvider();
        if (settings.ClickPulseEnabled == enabled)
        {
            return;
        }

        Update(updated => updated.ClickPulseEnabled = enabled);
    }

    public void AdjustCursorHighlightRadius(double delta)
    {
        Update(updated => updated.CursorHighlightRadius += delta);
    }

    public void AdjustCursorHighlightThickness(double delta)
    {
        Update(updated => updated.CursorHighlightThickness += delta);
    }

    public void SetMagnifierEnabled(bool enabled)
    {
        var settings = _settingsProvider();
        if (settings.MagnifierEnabled == enabled)
        {
            return;
        }

        Update(updated => updated.MagnifierEnabled = enabled);
    }

    public void SetPresetColor(string color)
    {
        Update(updated => updated.Color = color);
    }

    public void SetLaserPresetColor(int index)
    {
        var settings = _settingsProvider();
        if (index < 0 || index >= settings.LaserColorPresets.Count)
        {
            return;
        }

        SetPresetColor(settings.LaserColorPresets[index]);
    }

    public void SetAnnotationColor(string color)
    {
        Update(updated => updated.AnnotationColor = color);
        _annotations.ApplyColorToSelection(color);
    }

    public void SetAnnotationPresetColor(int index)
    {
        var settings = _settingsProvider();
        if (index < 0 || index >= settings.AnnotationColorPresets.Count)
        {
            return;
        }

        SetAnnotationColor(settings.AnnotationColorPresets[index]);
    }

    public void SetAnnotationTool(AnnotationTool tool)
    {
        if (_annotations.IsEditingText)
        {
            _annotations.CommitTextInput();
        }

        if (IsStepTool(tool))
        {
            _lastStepTool = tool;
        }

        Update(updated => updated.SetAnnotationTool(tool));

        if (tool != AnnotationTool.Move)
        {
            _annotations.ClearSelection();
        }
    }

    public void SelectStepTool()
    {
        SetAnnotationTool(_lastStepTool);
        if (_modeProvider() == InteractionMode.Passthrough)
        {
            _setMode(InteractionMode.Annotate);
        }
    }

    public void AdjustAnnotationThickness(double delta)
    {
        Update(updated => updated.AnnotationThickness += delta);
        _annotations.AdjustSelectedThickness(delta);
    }

    public void AdjustAnnotationFontSize(double delta)
    {
        Update(updated => updated.AnnotationFontSize += delta);
        _annotations.AdjustSelectedTextFontSize(delta);
    }

    public void AdjustLaserTrailLength(int delta)
    {
        Update(updated => updated.TrailLengthMs += delta);
    }

    public void AdjustSpotlightRadius(double delta)
    {
        Update(updated => updated.SpotlightRadius += delta);
    }

    public void AdjustSpotlightOpacity(double delta)
    {
        Update(updated => updated.SpotlightOpacity += delta);
    }

    public void AdjustMagnifierZoom(double delta)
    {
        Update(updated => updated.MagnifierZoom += delta);
    }

    public void AdjustMagnifierRadius(double delta)
    {
        Update(updated => updated.MagnifierRadius += delta);
    }

    public void AdjustPinnedLensZoom(double delta)
    {
        Update(updated => updated.PinnedLensZoom += delta);
    }

    public void AdjustPinnedLensRefreshFps(int delta)
    {
        Update(updated => updated.PinnedLensRefreshFps += delta);
    }

    public void AdjustRegionMaskOpacity(double delta)
    {
        var settings = _settingsProvider();
        var hasSelectedMask = _regionMasks.TryGetSelected(out var mask);
        Update(updated => updated.RegionMaskOpacity = (hasSelectedMask ? mask.Opacity : settings.RegionMaskOpacity) + delta);
        if (hasSelectedMask)
        {
            mask.SetOpacity(_settingsProvider().RegionMaskOpacity);
            _invalidateOverlay();
        }
    }

    public void SetRegionMaskColor(string color)
    {
        Update(updated => updated.RegionMaskColor = color);
        if (_regionMasks.TryGetSelected(out var mask))
        {
            mask.SetColor(_settingsProvider().RegionMaskColor);
            _invalidateOverlay();
        }
    }

    public void SetRegionMaskPresetColor(int index)
    {
        var settings = _settingsProvider();
        if (index < 0 || index >= settings.RegionMaskColorPresets.Count)
        {
            return;
        }

        SetRegionMaskColor(settings.RegionMaskColorPresets[index]);
    }

    public void SetGlowEnabled(bool enabled)
    {
        Update(updated => updated.GlowEnabled = enabled);
    }

    public void SetFadingAnnotationsEnabled(bool enabled)
    {
        var settings = _settingsProvider();
        if (settings.FadingAnnotationsEnabled == enabled)
        {
            return;
        }

        Update(updated => updated.FadingAnnotationsEnabled = enabled);
    }

    public void AdjustFadingAnnotationVisibleMs(int deltaMs)
    {
        Update(updated => updated.FadingAnnotationVisibleMs += deltaMs);
    }

    public void AdjustFadingAnnotationFadeMs(int deltaMs)
    {
        Update(updated => updated.FadingAnnotationFadeMs += deltaMs);
    }

    public void SetLaserActivationMode(LaserActivationMode mode)
    {
        var settings = _settingsProvider();
        if (settings.GetLaserActivationMode() == mode)
        {
            return;
        }

        Update(updated => updated.SetLaserActivationMode(mode));
    }

    public void SetSpotlightEnabled(bool enabled)
    {
        if (_visualEffects.SpotlightEnabled == enabled)
        {
            return;
        }

        if (enabled && _modeProvider() == InteractionMode.RegionSpotlightSelect)
        {
            _setMode(InteractionMode.Passthrough);
        }

        Update(updated => updated.SpotlightEnabled = enabled);
    }

    private void Update(Action<AppSettings> mutate)
    {
        var updated = _settingsProvider().Clone();
        mutate(updated);
        _applySettings(updated);
    }

    private static bool IsStepTool(AnnotationTool tool)
    {
        return tool is AnnotationTool.StepOval or AnnotationTool.StepRect;
    }
}
