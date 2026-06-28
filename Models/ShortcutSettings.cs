namespace FocusTool.Win.Models;

public sealed class ShortcutSettings
{
    public const string DisabledShortcut = "None";

    public string ToggleLaserActivation { get; set; } = "Ctrl+Alt+L";
    public string ToggleAnnotate { get; set; } = "Ctrl+Alt+D";
    public string PushToAnnotate { get; set; } = "Alt+A";
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
        PushToAnnotate = NormalizeShortcut(PushToAnnotate, "Alt+A");
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
