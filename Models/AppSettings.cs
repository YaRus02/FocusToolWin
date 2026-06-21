using System.Text.Json.Serialization;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace FocusTool.Win.Models;

public sealed class AppSettings
{
    public string Color { get; set; } = "#FFFF2020";
    public double PointSize { get; set; } = 12;
    public int TrailLengthMs { get; set; } = 400;
    public int FadeDurationMs { get; set; } = 500;
    public bool GlowEnabled { get; set; } = true;
    public string LaserActivationMode { get; set; } = string.Empty;
    public string LaserHoldShortcut { get; set; } = "XButton2";
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
    public double RegionMaskOpacity { get; set; } = 1.0;
    public string AnnotationColor { get; set; } = "#FFFF2020";
    public List<string> AnnotationColorPresets { get; set; } =
    [
        "#FFFF2020",
        "#FF00D26A",
        "#FF2080FF",
        "#FFFFD400",
        "#FFFFFFFF"
    ];
    public double AnnotationThickness { get; set; } = 4;
    public double AnnotationFontSize { get; set; } = 28;
    public string AnnotationTool { get; set; } = "Pencil";
    public ShortcutSettings Shortcuts { get; set; } = new();

    public AppSettings Clone() => new()
    {
        Color = Color,
        PointSize = PointSize,
        TrailLengthMs = TrailLengthMs,
        FadeDurationMs = FadeDurationMs,
        GlowEnabled = GlowEnabled,
        LaserActivationMode = LaserActivationMode,
        LaserHoldShortcut = LaserHoldShortcut,
        SpotlightEnabled = SpotlightEnabled,
        SpotlightRadius = SpotlightRadius,
        SpotlightOpacity = SpotlightOpacity,
        MagnifierEnabled = MagnifierEnabled,
        MagnifierRadius = MagnifierRadius,
        MagnifierZoom = MagnifierZoom,
        PinnedLensZoom = PinnedLensZoom,
        PinnedLensRefreshFps = PinnedLensRefreshFps,
        RegionMaskColor = RegionMaskColor,
        RegionMaskOpacity = RegionMaskOpacity,
        AnnotationColor = AnnotationColor,
        AnnotationColorPresets = [.. AnnotationColorPresets],
        AnnotationThickness = AnnotationThickness,
        AnnotationFontSize = AnnotationFontSize,
        AnnotationTool = AnnotationTool,
        Shortcuts = Shortcuts.Clone()
    };

    public void CopyFrom(AppSettings other)
    {
        Color = other.Color;
        PointSize = other.PointSize;
        TrailLengthMs = other.TrailLengthMs;
        FadeDurationMs = other.FadeDurationMs;
        GlowEnabled = other.GlowEnabled;
        LaserActivationMode = other.LaserActivationMode;
        LaserHoldShortcut = other.LaserHoldShortcut;
        SpotlightEnabled = other.SpotlightEnabled;
        SpotlightRadius = other.SpotlightRadius;
        SpotlightOpacity = other.SpotlightOpacity;
        MagnifierEnabled = other.MagnifierEnabled;
        MagnifierRadius = other.MagnifierRadius;
        MagnifierZoom = other.MagnifierZoom;
        PinnedLensZoom = other.PinnedLensZoom;
        PinnedLensRefreshFps = other.PinnedLensRefreshFps;
        RegionMaskColor = other.RegionMaskColor;
        RegionMaskOpacity = other.RegionMaskOpacity;
        AnnotationColor = other.AnnotationColor;
        AnnotationColorPresets = [.. other.AnnotationColorPresets];
        AnnotationThickness = other.AnnotationThickness;
        AnnotationFontSize = other.AnnotationFontSize;
        AnnotationTool = other.AnnotationTool;
        Shortcuts = other.Shortcuts.Clone();
        Normalize();
    }

    public void Normalize()
    {
        if (!TryParseColor(Color, out _))
        {
            Color = "#FFFF2020";
        }

        if (!TryParseColor(AnnotationColor, out _))
        {
            AnnotationColor = Color;
        }

        if (AnnotationColorPresets is null || AnnotationColorPresets.Count == 0)
        {
            AnnotationColorPresets =
            [
                "#FFFF2020",
                "#FF00D26A",
                "#FF2080FF",
                "#FFFFD400",
                "#FFFFFFFF"
            ];
        }

        for (var i = 0; i < AnnotationColorPresets.Count; i++)
        {
            if (!TryParseColor(AnnotationColorPresets[i], out _))
            {
                AnnotationColorPresets[i] = "#FFFF2020";
            }
        }

        while (AnnotationColorPresets.Count < 5)
        {
            AnnotationColorPresets.Add(AnnotationColorPresets.Count == 4 ? "#FFFFFFFF" : "#FFFF2020");
        }

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

        PointSize = Math.Clamp(PointSize, 4, 64);
        TrailLengthMs = Math.Clamp(TrailLengthMs, 80, 3000);
        FadeDurationMs = Math.Clamp(FadeDurationMs, 80, 3000);
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

        RegionMaskOpacity = Math.Clamp(RegionMaskOpacity, 0.1, 1.0);
        AnnotationThickness = Math.Clamp(AnnotationThickness, 1, 32);
        AnnotationFontSize = Math.Clamp(AnnotationFontSize, 8, 96);
        Shortcuts ??= new ShortcutSettings();
        Shortcuts.Normalize();
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
    public string ToggleSpotlight { get; set; } = "Ctrl+Alt+S";
    public string ToggleMagnifier { get; set; } = "Ctrl+Alt+M";
    public string TogglePinnedLens { get; set; } = "Ctrl+Alt+P";
    public string ToggleRegionMask { get; set; } = "Ctrl+Alt+H";
    public string ClearRegionMasks { get; set; } = "Ctrl+Alt+Shift+H";
    public string ToggleToolbar { get; set; } = "Ctrl+Alt+T";
    public string TakeScreenshot { get; set; } = "Ctrl+Alt+C";
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
        ToggleSpotlight = ToggleSpotlight,
        ToggleMagnifier = ToggleMagnifier,
        TogglePinnedLens = TogglePinnedLens,
        ToggleRegionMask = ToggleRegionMask,
        ClearRegionMasks = ClearRegionMasks,
        ToggleToolbar = ToggleToolbar,
        TakeScreenshot = TakeScreenshot,
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
        ToggleSpotlight = NormalizeShortcut(ToggleSpotlight, "Ctrl+Alt+S");
        ToggleMagnifier = NormalizeShortcut(ToggleMagnifier, "Ctrl+Alt+M");
        TogglePinnedLens = NormalizeShortcut(TogglePinnedLens, "Ctrl+Alt+P");
        ToggleRegionMask = NormalizeShortcut(ToggleRegionMask, "Ctrl+Alt+H");
        ClearRegionMasks = NormalizeShortcut(ClearRegionMasks, "Ctrl+Alt+Shift+H");
        ToggleToolbar = NormalizeShortcut(ToggleToolbar, "Ctrl+Alt+T");
        TakeScreenshot = NormalizeShortcut(TakeScreenshot, "Ctrl+Alt+C");
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
