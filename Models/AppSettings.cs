using System.Text.Json.Serialization;
using System.Windows.Media;
using AnnotationToolEnum = FocusTool.Win.Models.AnnotationTool;
using MediaColor = System.Windows.Media.Color;

namespace FocusTool.Win.Models;

public sealed class AppSettings
{
    private static readonly string[] DefaultColorSlots =
    [
        "#FFFF2020",
        "#FF00D26A",
        "#FF2080FF",
        "#FFFFD400",
        "#FFFFFFFF"
    ];

    private static readonly string[] DefaultRegionMaskColorSlots =
    [
        "#FF000000",
        "#FF404040",
        "#FFFFFFFF",
        "#FFB00020",
        "#FF2080FF"
    ];

    private static readonly string[] DefaultCursorHighlightColorSlots =
    [
        "#BFFFD400",
        "#BF20D6FF",
        "#BFFF4FD8",
        "#BFFFFFFF",
        "#BF00D26A"
    ];

    public string Color { get; set; } = "#FFFF2020";
    public List<string> LaserColorPresets { get; set; } = [.. DefaultColorSlots];
    public double PointSize { get; set; } = 12;
    public int TrailLengthMs { get; set; } = 400;
    public int FadeDurationMs { get; set; } = 500;
    public bool GlowEnabled { get; set; } = true;
    public string LaserActivationMode { get; set; } = FocusTool.Win.Models.LaserActivationMode.Hold.ToString();
    public string LaserHoldShortcut { get; set; } = "Alt+Z";
    [JsonIgnore]
    public bool CursorHighlightEnabled { get; set; }
    public string CursorHighlightColor { get; set; } = "#BFFFD400";
    public List<string> CursorHighlightColorPresets { get; set; } = [.. DefaultCursorHighlightColorSlots];
    public double CursorHighlightRadius { get; set; } = 30;
    public double CursorHighlightThickness { get; set; } = 3;
    public string CursorHighlightActivationMode { get; set; } = FocusTool.Win.Models.LaserActivationMode.Hold.ToString();
    public string CursorHighlightHoldShortcut { get; set; } = "Alt+X";
    public bool ClickPulseEnabled { get; set; }
    [JsonIgnore]
    public bool SpotlightEnabled { get; set; }
    public double SpotlightRadius { get; set; } = 160;
    public double SpotlightOpacity { get; set; } = 0.62;
    [JsonIgnore]
    public bool MagnifierEnabled { get; set; }
    public double MagnifierRadius { get; set; } = 150;
    public double MagnifierZoom { get; set; } = 2.0;
    public double PinnedLensZoom { get; set; } = 2.0;
    public int PinnedLensRefreshFps { get; set; } = 30;
    public string RegionMaskColor { get; set; } = "#FF000000";
    public List<string> RegionMaskColorPresets { get; set; } = [.. DefaultRegionMaskColorSlots];
    public double RegionMaskOpacity { get; set; } = 1.0;
    public string RegionMaskStyle { get; set; } = FocusTool.Win.Models.RegionMaskStyle.StripesWithLabel.ToString();
    public string AnnotationColor { get; set; } = "#FFFF2020";
    public List<string> AnnotationColorPresets { get; set; } = [.. DefaultColorSlots];
    public double AnnotationThickness { get; set; } = 4;
    public Dictionary<string, double> AnnotationToolThicknesses { get; set; } = [];
    public double AnnotationFontSize { get; set; } = 28;
    public string AnnotationTool { get; set; } = "Pencil";
    public string StrokeSmoothing { get; set; } = StrokeSmoothingLevel.Balanced.ToString();
    public bool FadingAnnotationsEnabled { get; set; }
    public int FadingAnnotationVisibleMs { get; set; } = 6000;
    public int FadingAnnotationFadeMs { get; set; } = 1200;
    public ShortcutSettings Shortcuts { get; set; } = new();
    public TimerSettings Timer { get; set; } = new();

    public AppSettings Clone() => new()
    {
        Color = Color,
        LaserColorPresets = [.. LaserColorPresets],
        PointSize = PointSize,
        TrailLengthMs = TrailLengthMs,
        FadeDurationMs = FadeDurationMs,
        GlowEnabled = GlowEnabled,
        LaserActivationMode = LaserActivationMode,
        LaserHoldShortcut = LaserHoldShortcut,
        CursorHighlightEnabled = CursorHighlightEnabled,
        CursorHighlightColor = CursorHighlightColor,
        CursorHighlightColorPresets = [.. CursorHighlightColorPresets],
        CursorHighlightRadius = CursorHighlightRadius,
        CursorHighlightThickness = CursorHighlightThickness,
        CursorHighlightActivationMode = CursorHighlightActivationMode,
        CursorHighlightHoldShortcut = CursorHighlightHoldShortcut,
        ClickPulseEnabled = ClickPulseEnabled,
        SpotlightEnabled = SpotlightEnabled,
        SpotlightRadius = SpotlightRadius,
        SpotlightOpacity = SpotlightOpacity,
        MagnifierEnabled = MagnifierEnabled,
        MagnifierRadius = MagnifierRadius,
        MagnifierZoom = MagnifierZoom,
        PinnedLensZoom = PinnedLensZoom,
        PinnedLensRefreshFps = PinnedLensRefreshFps,
        RegionMaskColor = RegionMaskColor,
        RegionMaskColorPresets = [.. RegionMaskColorPresets],
        RegionMaskOpacity = RegionMaskOpacity,
        RegionMaskStyle = RegionMaskStyle,
        AnnotationColor = AnnotationColor,
        AnnotationColorPresets = [.. AnnotationColorPresets],
        AnnotationThickness = AnnotationThickness,
        AnnotationToolThicknesses = AnnotationToolThicknesses is null ? [] : new Dictionary<string, double>(AnnotationToolThicknesses),
        AnnotationFontSize = AnnotationFontSize,
        AnnotationTool = AnnotationTool,
        StrokeSmoothing = StrokeSmoothing,
        FadingAnnotationsEnabled = FadingAnnotationsEnabled,
        FadingAnnotationVisibleMs = FadingAnnotationVisibleMs,
        FadingAnnotationFadeMs = FadingAnnotationFadeMs,
        Shortcuts = Shortcuts.Clone(),
        Timer = Timer.Clone()
    };

    public void CopyFrom(AppSettings other)
    {
        Color = other.Color;
        LaserColorPresets = [.. other.LaserColorPresets];
        PointSize = other.PointSize;
        TrailLengthMs = other.TrailLengthMs;
        FadeDurationMs = other.FadeDurationMs;
        GlowEnabled = other.GlowEnabled;
        LaserActivationMode = other.LaserActivationMode;
        LaserHoldShortcut = other.LaserHoldShortcut;
        CursorHighlightEnabled = other.CursorHighlightEnabled;
        CursorHighlightColor = other.CursorHighlightColor;
        CursorHighlightColorPresets = [.. other.CursorHighlightColorPresets];
        CursorHighlightRadius = other.CursorHighlightRadius;
        CursorHighlightThickness = other.CursorHighlightThickness;
        CursorHighlightActivationMode = other.CursorHighlightActivationMode;
        CursorHighlightHoldShortcut = other.CursorHighlightHoldShortcut;
        ClickPulseEnabled = other.ClickPulseEnabled;
        SpotlightEnabled = other.SpotlightEnabled;
        SpotlightRadius = other.SpotlightRadius;
        SpotlightOpacity = other.SpotlightOpacity;
        MagnifierEnabled = other.MagnifierEnabled;
        MagnifierRadius = other.MagnifierRadius;
        MagnifierZoom = other.MagnifierZoom;
        PinnedLensZoom = other.PinnedLensZoom;
        PinnedLensRefreshFps = other.PinnedLensRefreshFps;
        RegionMaskColor = other.RegionMaskColor;
        RegionMaskColorPresets = [.. other.RegionMaskColorPresets];
        RegionMaskOpacity = other.RegionMaskOpacity;
        RegionMaskStyle = other.RegionMaskStyle;
        AnnotationColor = other.AnnotationColor;
        AnnotationColorPresets = [.. other.AnnotationColorPresets];
        AnnotationThickness = other.AnnotationThickness;
        AnnotationToolThicknesses = other.AnnotationToolThicknesses is null ? [] : new Dictionary<string, double>(other.AnnotationToolThicknesses);
        AnnotationFontSize = other.AnnotationFontSize;
        AnnotationTool = other.AnnotationTool;
        StrokeSmoothing = other.StrokeSmoothing;
        FadingAnnotationsEnabled = other.FadingAnnotationsEnabled;
        FadingAnnotationVisibleMs = other.FadingAnnotationVisibleMs;
        FadingAnnotationFadeMs = other.FadingAnnotationFadeMs;
        Shortcuts = other.Shortcuts.Clone();
        Timer = other.Timer.Clone();
        Normalize();
    }

    public void Normalize()
    {
        if (!TryParseColor(Color, out _))
        {
            Color = "#FFFF2020";
        }

        LaserColorPresets = NormalizeColorPresets(LaserColorPresets, DefaultColorSlots);
        CursorHighlightColorPresets = NormalizeColorPresets(CursorHighlightColorPresets, DefaultCursorHighlightColorSlots);
        AnnotationColorPresets = NormalizeColorPresets(AnnotationColorPresets, DefaultColorSlots);
        RegionMaskColorPresets = NormalizeColorPresets(RegionMaskColorPresets, DefaultRegionMaskColorSlots);
        EnsureColorInPresets(Color, LaserColorPresets, fallbackIndex: 4);

        if (!TryParseColor(AnnotationColor, out _))
        {
            AnnotationColor = Color;
        }
        EnsureColorInPresets(AnnotationColor, AnnotationColorPresets, fallbackIndex: 4);

        if (!Enum.TryParse<AnnotationToolEnum>(AnnotationTool, true, out var annotationTool))
        {
            annotationTool = AnnotationToolEnum.Pencil;
        }

        AnnotationTool = annotationTool.ToString();
        if (!Enum.TryParse<StrokeSmoothingLevel>(StrokeSmoothing, true, out var smoothingLevel))
        {
            smoothingLevel = StrokeSmoothingLevel.Balanced;
        }

        StrokeSmoothing = smoothingLevel.ToString();

        if (!Enum.TryParse<LaserActivationMode>(LaserActivationMode, true, out var laserActivationMode))
        {
            laserActivationMode = FocusTool.Win.Models.LaserActivationMode.Hold;
        }

        LaserActivationMode = laserActivationMode.ToString();
        if (laserActivationMode == FocusTool.Win.Models.LaserActivationMode.Hold
            && IsLaserHoldShortcutDisabled(LaserHoldShortcut))
        {
            LaserHoldShortcut = "Alt+Z";
        }

        if (!TryParseColor(CursorHighlightColor, out _))
        {
            CursorHighlightColor = "#BFFFD400";
        }
        EnsureColorInPresets(CursorHighlightColor, CursorHighlightColorPresets, fallbackIndex: 0);

        if (!Enum.TryParse<LaserActivationMode>(CursorHighlightActivationMode, true, out var cursorHighlightActivationMode))
        {
            cursorHighlightActivationMode = FocusTool.Win.Models.LaserActivationMode.Hold;
        }

        CursorHighlightActivationMode = cursorHighlightActivationMode.ToString();
        if (cursorHighlightActivationMode == FocusTool.Win.Models.LaserActivationMode.Hold
            && IsLaserHoldShortcutDisabled(CursorHighlightHoldShortcut))
        {
            CursorHighlightHoldShortcut = "Alt+X";
        }

        PointSize = Math.Clamp(PointSize, 4, 64);
        TrailLengthMs = Math.Clamp(TrailLengthMs, 80, 3000);
        FadeDurationMs = Math.Clamp(FadeDurationMs, 80, 3000);
        CursorHighlightRadius = Math.Clamp(CursorHighlightRadius, 12, 96);
        CursorHighlightThickness = Math.Clamp(CursorHighlightThickness, 1, 12);
        SpotlightRadius = Math.Clamp(SpotlightRadius, 48, 480);
        SpotlightOpacity = Math.Clamp(SpotlightOpacity, 0.2, 0.88);
        MagnifierRadius = Math.Clamp(MagnifierRadius, 80, 360);
        MagnifierZoom = Math.Clamp(MagnifierZoom, 1.25, 4.0);
        PinnedLensZoom = Math.Clamp(PinnedLensZoom, 1.0, 4.0);
        PinnedLensRefreshFps = Math.Clamp(PinnedLensRefreshFps, 10, 60);
        if (!TryParseColor(RegionMaskColor, out _))
        {
            RegionMaskColor = "#FF000000";
        }
        EnsureColorInPresets(RegionMaskColor, RegionMaskColorPresets, fallbackIndex: 4);

        RegionMaskOpacity = Math.Clamp(RegionMaskOpacity, 0.1, 1.0);
        if (!Enum.TryParse<RegionMaskStyle>(RegionMaskStyle, true, out var regionMaskStyle))
        {
            regionMaskStyle = FocusTool.Win.Models.RegionMaskStyle.StripesWithLabel;
        }

        RegionMaskStyle = regionMaskStyle.ToString();
        AnnotationThickness = NormalizeThickness(AnnotationThickness, fallback: 4);
        NormalizeAnnotationToolThicknesses(AnnotationThickness);
        if (UsesAnnotationThickness(annotationTool))
        {
            AnnotationThickness = GetAnnotationThickness(annotationTool);
        }

        AnnotationFontSize = Math.Clamp(AnnotationFontSize, 8, 96);
        FadingAnnotationVisibleMs = Math.Clamp(FadingAnnotationVisibleMs, 500, 60000);
        FadingAnnotationFadeMs = Math.Clamp(FadingAnnotationFadeMs, 100, 10000);
        Shortcuts ??= new ShortcutSettings();
        var holdShortcutsUseLegacyDefaults = string.Equals(LaserHoldShortcut, "Alt+Z", StringComparison.OrdinalIgnoreCase)
            && string.Equals(CursorHighlightHoldShortcut, "Alt+X", StringComparison.OrdinalIgnoreCase);
        Shortcuts.Normalize(holdShortcutsUseLegacyDefaults);
        Timer ??= new TimerSettings();
        Timer.Normalize();
    }

    public static string[] DefaultLaserColorPresets() => [.. DefaultColorSlots];

    public static string[] DefaultCursorHighlightColorPresets() => [.. DefaultCursorHighlightColorSlots];

    public static string[] DefaultAnnotationColorPresets() => [.. DefaultColorSlots];

    public static string[] DefaultRegionMaskColorPresets() => [.. DefaultRegionMaskColorSlots];

    private static List<string> NormalizeColorPresets(List<string>? presets, IReadOnlyList<string> defaults)
    {
        if (presets is null || presets.Count == 0)
        {
            presets = [.. defaults];
        }

        for (var i = 0; i < presets.Count; i++)
        {
            if (!TryParseColor(presets[i], out _))
            {
                presets[i] = defaults[Math.Min(i, defaults.Count - 1)];
            }
        }

        while (presets.Count < defaults.Count)
        {
            presets.Add(defaults[presets.Count]);
        }

        return presets;
    }

    private static void EnsureColorInPresets(string color, List<string> presets, int fallbackIndex)
    {
        if (!TryParseColor(color, out _) || ContainsColor(presets, color))
        {
            return;
        }

        presets[Math.Clamp(fallbackIndex, 0, presets.Count - 1)] = color;
    }

    private static bool ContainsColor(IEnumerable<string> presets, string color)
    {
        foreach (var preset in presets)
        {
            if (string.Equals(preset, color, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public MediaColor ToMediaColor()
    {
        if (TryParseColor(Color, out var color))
        {
            return color;
        }

        return Colors.Red;
    }

    internal AnnotationToolEnum GetAnnotationTool()
    {
        return Enum.TryParse<AnnotationToolEnum>(AnnotationTool, true, out var tool)
            ? tool
            : AnnotationToolEnum.Pencil;
    }

    internal StrokeSmoothingLevel GetStrokeSmoothingLevel()
    {
        return Enum.TryParse<StrokeSmoothingLevel>(StrokeSmoothing, true, out var level)
            ? level
            : StrokeSmoothingLevel.Balanced;
    }

    internal void SetAnnotationTool(AnnotationToolEnum tool)
    {
        AnnotationTool = tool.ToString();
        if (UsesAnnotationThickness(tool))
        {
            AnnotationThickness = GetAnnotationThickness(tool);
        }
    }

    internal double GetAnnotationThickness(AnnotationToolEnum tool)
    {
        var fallback = NormalizeThickness(AnnotationThickness, fallback: 4);
        if (!UsesAnnotationThickness(tool))
        {
            return fallback;
        }

        return AnnotationToolThicknesses is not null
            && AnnotationToolThicknesses.TryGetValue(AnnotationThicknessKey(tool), out var value)
            ? NormalizeThickness(value, fallback)
            : fallback;
    }

    internal void SetAnnotationThicknessForTool(AnnotationToolEnum tool, double thickness)
    {
        var clamped = NormalizeThickness(thickness, fallback: GetAnnotationThickness(tool));
        if (UsesAnnotationThickness(tool))
        {
            AnnotationToolThicknesses ??= [];
            AnnotationToolThicknesses[AnnotationThicknessKey(tool)] = clamped;
        }

        AnnotationThickness = clamped;
    }

    internal static bool UsesAnnotationThickness(AnnotationToolEnum tool)
    {
        return tool is AnnotationToolEnum.Arrow
            or AnnotationToolEnum.Rectangle
            or AnnotationToolEnum.Ellipse
            or AnnotationToolEnum.Line
            or AnnotationToolEnum.Pencil
            or AnnotationToolEnum.Highlighter
            or AnnotationToolEnum.StepOval
            or AnnotationToolEnum.StepRect;
    }

    internal LaserActivationMode GetLaserActivationMode()
    {
        return Enum.TryParse<LaserActivationMode>(LaserActivationMode, true, out var mode)
            ? mode
            : FocusTool.Win.Models.LaserActivationMode.Hold;
    }

    internal void SetLaserActivationMode(LaserActivationMode mode)
    {
        LaserActivationMode = mode.ToString();
    }

    internal LaserActivationMode GetCursorHighlightActivationMode()
    {
        return Enum.TryParse<LaserActivationMode>(CursorHighlightActivationMode, true, out var mode)
            ? mode
            : FocusTool.Win.Models.LaserActivationMode.Hold;
    }

    internal void SetCursorHighlightActivationMode(LaserActivationMode mode)
    {
        CursorHighlightActivationMode = mode.ToString();
    }

    public MediaColor ToAnnotationMediaColor()
    {
        if (TryParseColor(AnnotationColor, out var color))
        {
            return color;
        }

        return ToMediaColor();
    }

    private void NormalizeAnnotationToolThicknesses(double fallback)
    {
        AnnotationToolThicknesses ??= [];
        foreach (var tool in AnnotationThicknessTools)
        {
            var key = AnnotationThicknessKey(tool);
            var value = AnnotationToolThicknesses.TryGetValue(key, out var stored)
                ? stored
                : fallback;
            AnnotationToolThicknesses[key] = NormalizeThickness(value, fallback);
        }
    }

    private static readonly AnnotationToolEnum[] AnnotationThicknessTools =
    [
        AnnotationToolEnum.Arrow,
        AnnotationToolEnum.Rectangle,
        AnnotationToolEnum.Ellipse,
        AnnotationToolEnum.Line,
        AnnotationToolEnum.Pencil,
        AnnotationToolEnum.Highlighter,
        AnnotationToolEnum.StepOval,
        AnnotationToolEnum.StepRect
    ];

    private static string AnnotationThicknessKey(AnnotationToolEnum tool) => tool.ToString();

    private static double NormalizeThickness(double value, double fallback)
    {
        return double.IsFinite(value)
            ? AnnotationGeometry.ClampThickness(value)
            : AnnotationGeometry.ClampThickness(fallback);
    }

    public static bool TryParseColor(string value, out MediaColor color)
    {
        try
        {
            var parsed = System.Windows.Media.ColorConverter.ConvertFromString(value);
            if (parsed is MediaColor mediaColor)
            {
                color = mediaColor;
                return true;
            }
        }
        catch
        {
            // Caller decides how to surface invalid input.
        }

        color = Colors.Red;
        return false;
    }

    internal static bool IsLaserHoldShortcutDisabled(string? shortcutText)
    {
        if (string.IsNullOrWhiteSpace(shortcutText))
        {
            return true;
        }

        return shortcutText.Equals("None", StringComparison.OrdinalIgnoreCase)
            || shortcutText.Equals("Off", StringComparison.OrdinalIgnoreCase)
            || shortcutText.Equals("Disabled", StringComparison.OrdinalIgnoreCase)
            || shortcutText.Equals("Always", StringComparison.OrdinalIgnoreCase);
    }
}
