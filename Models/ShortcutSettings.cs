namespace FocusTool.Win.Models;

public sealed class ShortcutSettings
{
    public const string DisabledShortcut = "None";
    public const int CurrentLayoutVersion = 2;

    public int LayoutVersion { get; set; } = CurrentLayoutVersion;
    public string ToggleLaserActivation { get; set; } = "Ctrl+Alt+Z";
    public string ToggleAnnotate { get; set; } = "Ctrl+Alt+A";
    public string PushToAnnotate { get; set; } = "Alt+A";
    public string ToggleCursorHighlight { get; set; } = "Ctrl+Alt+X";
    public string ToggleClickPulse { get; set; } = "Ctrl+Alt+C";
    public string ToggleSpotlight { get; set; } = "Ctrl+Alt+S";
    public string HoldSpotlight { get; set; } = "Alt+S";
    public string ToggleMagnifier { get; set; } = "Ctrl+Alt+V";
    public string TogglePinnedLens { get; set; } = "Ctrl+Alt+F";
    public string ToggleRegionMask { get; set; } = "Ctrl+Alt+R";
    public string ClearRegionMasks { get; set; } = DisabledShortcut;
    public string ToggleRegionSpotlight { get; set; } = "Ctrl+Alt+Shift+S";
    public string ClearRegionSpotlights { get; set; } = DisabledShortcut;
    public string ToggleFadingAnnotations { get; set; } = "Ctrl+Alt+D";
    public string ToggleTimer { get; set; } = "Ctrl+Alt+T";
    public string ToggleToolbar { get; set; } = "Ctrl+Alt+G";
    public string TakeScreenshot { get; set; } = "Ctrl+Alt+E";
    public string TakeRegionScreenshot { get; set; } = "Ctrl+Alt+Shift+E";
    public string ToggleScreenBoard { get; set; } = "Ctrl+Alt+B";
    public string ToggleBlackScreen { get; set; } = "Ctrl+Alt+Shift+B";
    public string ToggleWhiteScreen { get; set; } = "Ctrl+Alt+W";
    public string ExitApp { get; set; } = "Ctrl+Alt+Shift+Q";
    public string ToolArrow { get; set; } = "A";
    public string ToolRectangle { get; set; } = "R";
    public string ToolEllipse { get; set; } = "C";
    public string ToolLine { get; set; } = "S";
    public string ToolPencil { get; set; } = "W";
    public string ToolHighlighter { get; set; } = "F";
    public string ToolEraser { get; set; } = "E";
    public string ToolText { get; set; } = "T";
    public string ToolMove { get; set; } = "Q";
    public string ToolStep { get; set; } = "D";
    public string Color1 { get; set; } = "1";
    public string Color2 { get; set; } = "2";
    public string Color3 { get; set; } = "3";
    public string Color4 { get; set; } = "4";
    public string Color5 { get; set; } = "5";
    public string ThicknessDown { get; set; } = "Shift+Z";
    public string ThicknessUp { get; set; } = "Shift+X";
    public string Undo { get; set; } = "Ctrl+Z";
    public string Redo { get; set; } = "Ctrl+Shift+Z";
    public string DeleteSelection { get; set; } = "Backspace";
    public string Clear { get; set; } = "Delete";
    public string ClearAlternate { get; set; } = "Shift+E";
    public string ExitAnnotate { get; set; } = "Esc";

    public ShortcutSettings Clone() => new()
    {
        LayoutVersion = LayoutVersion,
        ToggleLaserActivation = ToggleLaserActivation,
        ToggleAnnotate = ToggleAnnotate,
        PushToAnnotate = PushToAnnotate,
        ToggleCursorHighlight = ToggleCursorHighlight,
        ToggleClickPulse = ToggleClickPulse,
        ToggleSpotlight = ToggleSpotlight,
        HoldSpotlight = HoldSpotlight,
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
        ToolEraser = ToolEraser,
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

    public void Normalize(bool allowLegacyDefaultMigration = true)
    {
        MigrateLegacyLayout(allowLegacyDefaultMigration);
        ToggleLaserActivation = NormalizeShortcut(ToggleLaserActivation, "Ctrl+Alt+Z");
        ToggleAnnotate = NormalizeShortcut(ToggleAnnotate, "Ctrl+Alt+A");
        PushToAnnotate = NormalizeShortcut(PushToAnnotate, "Alt+A");
        ToggleCursorHighlight = NormalizeShortcut(ToggleCursorHighlight, "Ctrl+Alt+X");
        ToggleClickPulse = NormalizeShortcut(ToggleClickPulse, "Ctrl+Alt+C");
        ToggleSpotlight = NormalizeShortcut(ToggleSpotlight, "Ctrl+Alt+S");
        HoldSpotlight = NormalizeShortcut(HoldSpotlight, "Alt+S");
        ToggleMagnifier = NormalizeShortcut(ToggleMagnifier, "Ctrl+Alt+V");
        TogglePinnedLens = NormalizeShortcut(TogglePinnedLens, "Ctrl+Alt+F");
        ToggleRegionMask = NormalizeShortcut(ToggleRegionMask, "Ctrl+Alt+R");
        ClearRegionMasks = NormalizeShortcut(ClearRegionMasks, DisabledShortcut);
        ToggleRegionSpotlight = NormalizeShortcut(ToggleRegionSpotlight, "Ctrl+Alt+Shift+S");
        ClearRegionSpotlights = NormalizeShortcut(ClearRegionSpotlights, DisabledShortcut);
        ToggleFadingAnnotations = NormalizeShortcut(ToggleFadingAnnotations, "Ctrl+Alt+D");
        ToggleTimer = NormalizeShortcut(ToggleTimer, "Ctrl+Alt+T");
        ToggleToolbar = NormalizeShortcut(ToggleToolbar, "Ctrl+Alt+G");
        TakeScreenshot = NormalizeShortcut(TakeScreenshot, "Ctrl+Alt+E");
        TakeRegionScreenshot = NormalizeShortcut(TakeRegionScreenshot, "Ctrl+Alt+Shift+E");
        ToggleScreenBoard = NormalizeShortcut(ToggleScreenBoard, "Ctrl+Alt+B");
        ToggleBlackScreen = NormalizeShortcut(ToggleBlackScreen, "Ctrl+Alt+Shift+B");
        ToggleWhiteScreen = NormalizeShortcut(ToggleWhiteScreen, "Ctrl+Alt+W");
        ExitApp = NormalizeShortcut(ExitApp, "Ctrl+Alt+Shift+Q");
        ToolArrow = NormalizeShortcut(ToolArrow, "A");
        ToolRectangle = NormalizeShortcut(ToolRectangle, "R");
        ToolEllipse = NormalizeShortcut(ToolEllipse, "C");
        ToolLine = NormalizeShortcut(ToolLine, "S");
        ToolPencil = NormalizeShortcut(ToolPencil, "W");
        ToolHighlighter = NormalizeShortcut(ToolHighlighter, "F");
        ToolEraser = NormalizeShortcut(ToolEraser, "E");
        ToolText = NormalizeShortcut(ToolText, "T");
        ToolMove = NormalizeShortcut(ToolMove, "Q");
        ToolStep = NormalizeShortcut(ToolStep, "D");
        Color1 = NormalizeShortcut(Color1, "1");
        Color2 = NormalizeShortcut(Color2, "2");
        Color3 = NormalizeShortcut(Color3, "3");
        Color4 = NormalizeShortcut(Color4, "4");
        Color5 = NormalizeShortcut(Color5, "5");
        ThicknessDown = NormalizeShortcut(ThicknessDown, "Shift+Z");
        ThicknessUp = NormalizeShortcut(ThicknessUp, "Shift+X");
        Undo = NormalizeShortcut(Undo, "Ctrl+Z");
        Redo = NormalizeShortcut(Redo, "Ctrl+Shift+Z");
        DeleteSelection = NormalizeShortcut(DeleteSelection, "Backspace");
        Clear = NormalizeShortcut(Clear, "Delete");
        ClearAlternate = NormalizeShortcut(ClearAlternate, "Shift+E");
        ExitAnnotate = NormalizeShortcut(ExitAnnotate, "Esc");
    }

    private void MigrateLegacyLayout(bool allowLegacyDefaultMigration)
    {
        if (LayoutVersion < 1)
        {
            if (allowLegacyDefaultMigration && UsesLegacyDefaultLayout())
            {
                ToggleLaserActivation = "Ctrl+Alt+Z";
                ToggleAnnotate = "Ctrl+Alt+A";
                ToggleCursorHighlight = "Ctrl+Alt+X";
                ToggleClickPulse = "Ctrl+Alt+C";
                HoldSpotlight = "Alt+S";
                ToggleMagnifier = "Ctrl+Alt+V";
                TogglePinnedLens = "Ctrl+Alt+F";
                ToggleRegionMask = "Ctrl+Alt+R";
                ClearRegionMasks = DisabledShortcut;
                ClearRegionSpotlights = DisabledShortcut;
                ToggleFadingAnnotations = "Ctrl+Alt+D";
                ToggleTimer = "Ctrl+Alt+T";
                ToggleToolbar = "Ctrl+Alt+G";
                TakeScreenshot = "Ctrl+Alt+E";
                TakeRegionScreenshot = "Ctrl+Alt+Shift+E";
                ToggleScreenBoard = "Ctrl+Alt+B";
                ToggleBlackScreen = "Ctrl+Alt+Shift+B";
                ExitApp = "Ctrl+Alt+Shift+Q";
                ToolLine = "S";
                ToolPencil = "W";
                ToolHighlighter = "F";
                ToolMove = "Q";
                ToolStep = "D";
                ThicknessDown = "Shift+Z";
                ThicknessUp = "Shift+X";
                Redo = "Ctrl+Shift+Z";
                ClearAlternate = "Shift+E";
            }
            else
            {
                ToggleClickPulse = DisabledShortcut;
                HoldSpotlight = DisabledShortcut;
            }

            LayoutVersion = 1;
        }

        if (LayoutVersion < 2)
        {
            ToolEraser = UsesExistingShortcut("E") ? DisabledShortcut : "E";
            LayoutVersion = 2;
        }
    }

    private bool UsesExistingShortcut(string shortcut)
    {
        return new[]
        {
            ToolArrow, ToolRectangle, ToolEllipse, ToolLine, ToolPencil, ToolHighlighter,
            ToolText, ToolMove, ToolStep, Color1, Color2, Color3, Color4, Color5,
            ThicknessDown, ThicknessUp, Undo, Redo, DeleteSelection, Clear, ClearAlternate,
            ExitAnnotate
        }.Any(value => string.Equals(value?.Trim(), shortcut, StringComparison.OrdinalIgnoreCase));
    }

    private bool UsesLegacyDefaultLayout()
    {
        return ToggleLaserActivation == "Ctrl+Alt+L"
            && ToggleAnnotate == "Ctrl+Alt+D"
            && PushToAnnotate == "Alt+A"
            && ToggleCursorHighlight == "Ctrl+Alt+U"
            && ToggleSpotlight == "Ctrl+Alt+S"
            && ToggleMagnifier == "Ctrl+Alt+M"
            && TogglePinnedLens == "Ctrl+Alt+P"
            && ToggleRegionMask == "Ctrl+Alt+H"
            && ClearRegionMasks == "Ctrl+Alt+Shift+H"
            && ToggleRegionSpotlight == "Ctrl+Alt+Shift+S"
            && ClearRegionSpotlights == "Ctrl+Alt+Shift+X"
            && ToggleFadingAnnotations == "Ctrl+Alt+F"
            && ToggleTimer == "Ctrl+Alt+N"
            && ToggleToolbar == "Ctrl+Alt+T"
            && TakeScreenshot == "Ctrl+Alt+C"
            && TakeRegionScreenshot == "Ctrl+Alt+Shift+C"
            && ToggleScreenBoard == "Ctrl+Alt+G"
            && ToggleBlackScreen == "Ctrl+Alt+B"
            && ToggleWhiteScreen == "Ctrl+Alt+W"
            && ExitApp == "Ctrl+Alt+Q"
            && ToolArrow == "A"
            && ToolRectangle == "R"
            && ToolEllipse == "C"
            && ToolLine == "L"
            && ToolPencil == "P"
            && ToolHighlighter == "H"
            && ToolText == "T"
            && ToolMove == "M"
            && ToolStep == "N"
            && Color1 == "1"
            && Color2 == "2"
            && Color3 == "3"
            && Color4 == "4"
            && Color5 == "5"
            && ThicknessDown == "["
            && ThicknessUp == "]"
            && Undo == "Ctrl+Z"
            && Redo == "Ctrl+Y"
            && DeleteSelection == "Backspace"
            && Clear == "Delete"
            && ClearAlternate == "E"
            && ExitAnnotate == "Esc";
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
