using System.Text.Json.Serialization;
using System.Windows.Media;
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

    public string Color { get; set; } = "#FFFF2020";
    public List<string> LaserColorPresets { get; set; } = [.. DefaultColorSlots];
    public double PointSize { get; set; } = 12;
    public int TrailLengthMs { get; set; } = 400;
    public int FadeDurationMs { get; set; } = 500;
    public bool GlowEnabled { get; set; } = true;
    public string LaserActivationMode { get; set; } = string.Empty;
    public string LaserHoldShortcut { get; set; } = "XButton2";
    [JsonIgnore]
    public bool CursorHighlightEnabled { get; set; }
    public string CursorHighlightColor { get; set; } = "#BFFFD400";
    public double CursorHighlightRadius { get; set; } = 30;
    public double CursorHighlightThickness { get; set; } = 3;
    public string CursorHighlightActivationMode { get; set; } = FocusTool.Win.Models.LaserActivationMode.Always.ToString();
    public string CursorHighlightHoldShortcut { get; set; } = "XButton1";
    public bool CursorHighlightClickPulseEnabled { get; set; } = true;
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
    public double AnnotationFontSize { get; set; } = 28;
    public string AnnotationTool { get; set; } = "Pencil";
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
        CursorHighlightRadius = CursorHighlightRadius,
        CursorHighlightThickness = CursorHighlightThickness,
        CursorHighlightActivationMode = CursorHighlightActivationMode,
        CursorHighlightHoldShortcut = CursorHighlightHoldShortcut,
        CursorHighlightClickPulseEnabled = CursorHighlightClickPulseEnabled,
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
        AnnotationFontSize = AnnotationFontSize,
        AnnotationTool = AnnotationTool,
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
        CursorHighlightRadius = other.CursorHighlightRadius;
        CursorHighlightThickness = other.CursorHighlightThickness;
        CursorHighlightActivationMode = other.CursorHighlightActivationMode;
        CursorHighlightHoldShortcut = other.CursorHighlightHoldShortcut;
        CursorHighlightClickPulseEnabled = other.CursorHighlightClickPulseEnabled;
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
        AnnotationFontSize = other.AnnotationFontSize;
        AnnotationTool = other.AnnotationTool;
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
        AnnotationColorPresets = NormalizeColorPresets(AnnotationColorPresets, DefaultColorSlots);
        RegionMaskColorPresets = NormalizeColorPresets(RegionMaskColorPresets, DefaultRegionMaskColorSlots);
        EnsureColorInPresets(Color, LaserColorPresets, fallbackIndex: 4);

        if (!TryParseColor(AnnotationColor, out _))
        {
            AnnotationColor = Color;
        }
        EnsureColorInPresets(AnnotationColor, AnnotationColorPresets, fallbackIndex: 4);

        if (!Enum.TryParse<AnnotationTool>(AnnotationTool, true, out _))
        {
            AnnotationTool = FocusTool.Win.Models.AnnotationTool.Pencil.ToString();
        }

        if (!Enum.TryParse<LaserActivationMode>(LaserActivationMode, true, out var laserActivationMode))
        {
            laserActivationMode = IsLaserHoldShortcutDisabled(LaserHoldShortcut)
                ? FocusTool.Win.Models.LaserActivationMode.Always
                : FocusTool.Win.Models.LaserActivationMode.Hold;
        }

        LaserActivationMode = laserActivationMode.ToString();
        if (laserActivationMode == FocusTool.Win.Models.LaserActivationMode.Hold
            && IsLaserHoldShortcutDisabled(LaserHoldShortcut))
        {
            LaserHoldShortcut = "XButton2";
        }

        if (!TryParseColor(CursorHighlightColor, out _))
        {
            CursorHighlightColor = "#BFFFD400";
        }

        if (!Enum.TryParse<LaserActivationMode>(CursorHighlightActivationMode, true, out var cursorHighlightActivationMode))
        {
            cursorHighlightActivationMode = FocusTool.Win.Models.LaserActivationMode.Always;
        }

        CursorHighlightActivationMode = cursorHighlightActivationMode.ToString();
        if (cursorHighlightActivationMode == FocusTool.Win.Models.LaserActivationMode.Hold
            && IsLaserHoldShortcutDisabled(CursorHighlightHoldShortcut))
        {
            CursorHighlightHoldShortcut = "XButton1";
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
        AnnotationThickness = Math.Clamp(AnnotationThickness, 1, 32);
        AnnotationFontSize = Math.Clamp(AnnotationFontSize, 8, 96);
        FadingAnnotationVisibleMs = Math.Clamp(FadingAnnotationVisibleMs, 500, 60000);
        FadingAnnotationFadeMs = Math.Clamp(FadingAnnotationFadeMs, 100, 10000);
        Shortcuts ??= new ShortcutSettings();
        Shortcuts.Normalize();
        Timer ??= new TimerSettings();
        Timer.Normalize();
    }

    public static string[] DefaultLaserColorPresets() => [.. DefaultColorSlots];

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

    internal AnnotationTool GetAnnotationTool()
    {
        return Enum.TryParse<AnnotationTool>(AnnotationTool, true, out var tool)
            ? tool
            : FocusTool.Win.Models.AnnotationTool.Pencil;
    }

    internal void SetAnnotationTool(AnnotationTool tool)
    {
        AnnotationTool = tool.ToString();
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
            : FocusTool.Win.Models.LaserActivationMode.Always;
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

public sealed class ShortcutSettings
{
    public const string DisabledShortcut = "None";

    public string ToggleLaserActivation { get; set; } = "Ctrl+Alt+L";
    public string ToggleAnnotate { get; set; } = "Ctrl+Alt+D";
    public string PushToAnnotate { get; set; } = "Ctrl+Space";
    public string ToggleCursorHighlight { get; set; } = "Ctrl+Alt+U";
    public string ToggleSpotlight { get; set; } = "Ctrl+Alt+S";
    public string ToggleMagnifier { get; set; } = "Ctrl+Alt+M";
    public string TogglePinnedLens { get; set; } = "Ctrl+Alt+P";
    public string ToggleRegionMask { get; set; } = "Ctrl+Alt+H";
    public string ClearRegionMasks { get; set; } = "Ctrl+Alt+Shift+H";
    public string ToggleRegionSpotlight { get; set; } = "Ctrl+Alt+Shift+S";
    public string ClearRegionSpotlights { get; set; } = "Ctrl+Alt+Shift+X";
    public string ToggleFadingAnnotations { get; set; } = "Ctrl+Alt+F";
    public string ToggleTimer { get; set; } = "Ctrl+Alt+N";
    public string ToggleToolbar { get; set; } = "Ctrl+Alt+T";
    public string TakeScreenshot { get; set; } = "Ctrl+Alt+C";
    public string TakeRegionScreenshot { get; set; } = "Ctrl+Alt+Shift+C";
    public string ToggleScreenBoard { get; set; } = "Ctrl+Alt+G";
    public string ToggleBlackScreen { get; set; } = "Ctrl+Alt+B";
    public string ToggleWhiteScreen { get; set; } = "Ctrl+Alt+W";
    public string ExitApp { get; set; } = "Ctrl+Alt+Q";
    public string ToolArrow { get; set; } = "A";
    public string ToolRectangle { get; set; } = "R";
    public string ToolEllipse { get; set; } = "C";
    public string ToolLine { get; set; } = "L";
    public string ToolPencil { get; set; } = "P";
    public string ToolHighlighter { get; set; } = "H";
    public string ToolText { get; set; } = "T";
    public string ToolMove { get; set; } = "M";
    public string ToolStep { get; set; } = "N";
    public string Color1 { get; set; } = "1";
    public string Color2 { get; set; } = "2";
    public string Color3 { get; set; } = "3";
    public string Color4 { get; set; } = "4";
    public string Color5 { get; set; } = "5";
    public string ThicknessDown { get; set; } = "[";
    public string ThicknessUp { get; set; } = "]";
    public string Undo { get; set; } = "Ctrl+Z";
    public string Redo { get; set; } = "Ctrl+Y";
    public string DeleteSelection { get; set; } = "Backspace";
    public string Clear { get; set; } = "Delete";
    public string ClearAlternate { get; set; } = "E";
    public string ExitAnnotate { get; set; } = "Esc";

    public ShortcutSettings Clone() => new()
    {
        ToggleLaserActivation = ToggleLaserActivation,
        ToggleAnnotate = ToggleAnnotate,
        PushToAnnotate = PushToAnnotate,
        ToggleCursorHighlight = ToggleCursorHighlight,
        ToggleSpotlight = ToggleSpotlight,
        ToggleMagnifier = ToggleMagnifier,
        TogglePinnedLens = TogglePinnedLens,
        ToggleRegionMask = ToggleRegionMask,
        ClearRegionMasks = ClearRegionMasks,
        ToggleRegionSpotlight = ToggleRegionSpotlight,
        ClearRegionSpotlights = ClearRegionSpotlights,
        ToggleFadingAnnotations = ToggleFadingAnnotations,
        ToggleTimer = ToggleTimer,
        ToggleToolbar = ToggleToolbar,
        TakeScreenshot = TakeScreenshot,
        TakeRegionScreenshot = TakeRegionScreenshot,
        ToggleScreenBoard = ToggleScreenBoard,
        ToggleBlackScreen = ToggleBlackScreen,
        ToggleWhiteScreen = ToggleWhiteScreen,
        ExitApp = ExitApp,
        ToolArrow = ToolArrow,
        ToolRectangle = ToolRectangle,
        ToolEllipse = ToolEllipse,
        ToolLine = ToolLine,
        ToolPencil = ToolPencil,
        ToolHighlighter = ToolHighlighter,
        ToolText = ToolText,
        ToolMove = ToolMove,
        ToolStep = ToolStep,
        Color1 = Color1,
        Color2 = Color2,
        Color3 = Color3,
        Color4 = Color4,
        Color5 = Color5,
        ThicknessDown = ThicknessDown,
        ThicknessUp = ThicknessUp,
        Undo = Undo,
        Redo = Redo,
        DeleteSelection = DeleteSelection,
        Clear = Clear,
        ClearAlternate = ClearAlternate,
        ExitAnnotate = ExitAnnotate
    };

    public void Normalize()
    {
        ToggleLaserActivation = NormalizeShortcut(ToggleLaserActivation, "Ctrl+Alt+L");
        ToggleAnnotate = NormalizeShortcut(ToggleAnnotate, "Ctrl+Alt+D");
        PushToAnnotate = NormalizeShortcut(PushToAnnotate, "Ctrl+Space");
        ToggleCursorHighlight = NormalizeShortcut(ToggleCursorHighlight, "Ctrl+Alt+U");
        ToggleSpotlight = NormalizeShortcut(ToggleSpotlight, "Ctrl+Alt+S");
        ToggleMagnifier = NormalizeShortcut(ToggleMagnifier, "Ctrl+Alt+M");
        TogglePinnedLens = NormalizeShortcut(TogglePinnedLens, "Ctrl+Alt+P");
        ToggleRegionMask = NormalizeShortcut(ToggleRegionMask, "Ctrl+Alt+H");
        ClearRegionMasks = NormalizeShortcut(ClearRegionMasks, "Ctrl+Alt+Shift+H");
        ToggleRegionSpotlight = NormalizeShortcut(ToggleRegionSpotlight, "Ctrl+Alt+Shift+S");
        ClearRegionSpotlights = NormalizeShortcut(ClearRegionSpotlights, "Ctrl+Alt+Shift+X");
        ToggleFadingAnnotations = NormalizeShortcut(ToggleFadingAnnotations, "Ctrl+Alt+F");
        ToggleTimer = NormalizeShortcut(ToggleTimer, "Ctrl+Alt+N");
        ToggleToolbar = NormalizeShortcut(ToggleToolbar, "Ctrl+Alt+T");
        TakeScreenshot = NormalizeShortcut(TakeScreenshot, "Ctrl+Alt+C");
        TakeRegionScreenshot = NormalizeShortcut(TakeRegionScreenshot, "Ctrl+Alt+Shift+C");
        ToggleScreenBoard = NormalizeShortcut(ToggleScreenBoard, "Ctrl+Alt+G");
        ToggleBlackScreen = NormalizeShortcut(ToggleBlackScreen, "Ctrl+Alt+B");
        ToggleWhiteScreen = NormalizeShortcut(ToggleWhiteScreen, "Ctrl+Alt+W");
        ExitApp = NormalizeShortcut(ExitApp, "Ctrl+Alt+Q");
        ToolArrow = NormalizeShortcut(ToolArrow, "A");
        ToolRectangle = NormalizeShortcut(ToolRectangle, "R");
        ToolEllipse = NormalizeShortcut(ToolEllipse, "C");
        ToolLine = NormalizeShortcut(ToolLine, "L");
        ToolPencil = NormalizeShortcut(ToolPencil, "P");
        ToolHighlighter = NormalizeShortcut(ToolHighlighter, "H");
        ToolText = NormalizeShortcut(ToolText, "T");
        ToolMove = NormalizeShortcut(ToolMove, "M");
        ToolStep = NormalizeShortcut(ToolStep, "N");
        Color1 = NormalizeShortcut(Color1, "1");
        Color2 = NormalizeShortcut(Color2, "2");
        Color3 = NormalizeShortcut(Color3, "3");
        Color4 = NormalizeShortcut(Color4, "4");
        Color5 = NormalizeShortcut(Color5, "5");
        ThicknessDown = NormalizeShortcut(ThicknessDown, "[");
        ThicknessUp = NormalizeShortcut(ThicknessUp, "]");
        Undo = NormalizeShortcut(Undo, "Ctrl+Z");
        Redo = NormalizeShortcut(Redo, "Ctrl+Y");
        DeleteSelection = NormalizeShortcut(DeleteSelection, "Backspace");
        Clear = NormalizeShortcut(Clear, "Delete");
        ClearAlternate = NormalizeShortcut(ClearAlternate, "E");
        ExitAnnotate = NormalizeShortcut(ExitAnnotate, "Esc");
    }

    private static string NormalizeShortcut(string? value, string fallback)
    {
        if (value is null)
        {
            return fallback;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0 || IsShortcutDisabled(trimmed))
        {
            return DisabledShortcut;
        }

        return trimmed;
    }

    public static bool IsShortcutDisabled(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            || value.Equals("None", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Off", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
    }
}
