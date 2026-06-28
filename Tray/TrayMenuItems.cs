using System.Windows.Forms;
using FocusTool.Win.Models;

namespace FocusTool.Win.Tray;

internal sealed class TrayMenuItems
{
    public required ContextMenuStrip ContextMenu { get; init; }
    public required ToolStripMenuItem StatusItem { get; init; }
    public required ToolStripMenuItem ModeItem { get; init; }
    public required ToolStripMenuItem LaserAlwaysModeItem { get; init; }
    public required ToolStripMenuItem LaserHoldModeItem { get; init; }
    public required ToolStripMenuItem CursorHighlightAlwaysModeItem { get; init; }
    public required ToolStripMenuItem CursorHighlightHoldModeItem { get; init; }
    public required ToolStripMenuItem CursorHighlightPulseItem { get; init; }
    public required ToolStripMenuItem SpotlightItem { get; init; }
    public required ToolStripMenuItem RegionSpotlightItem { get; init; }
    public required ToolStripMenuItem ClearRegionSpotlightsItem { get; init; }
    public required ToolStripMenuItem MagnifierItem { get; init; }
    public required ToolStripMenuItem PinnedLensItem { get; init; }
    public required ToolStripMenuItem ClosePinnedLensesItem { get; init; }
    public required ToolStripMenuItem RegionMaskItem { get; init; }
    public required ToolStripMenuItem ClearRegionMasksItem { get; init; }
    public required ToolStripMenuItem FadingAnnotationsItem { get; init; }
    public required ToolStripMenuItem ToolbarItem { get; init; }
    public required ToolStripMenuItem ScreenshotItem { get; init; }
    public required ToolStripMenuItem RegionScreenshotItem { get; init; }
    public required ToolStripMenuItem NewTimerItem { get; init; }
    public required ToolStripMenuItem CloseTimersItem { get; init; }
    public required ToolStripMenuItem ScreenBoardItem { get; init; }
    public required ToolStripMenuItem BlackScreenItem { get; init; }
    public required ToolStripMenuItem WhiteScreenItem { get; init; }
    public required ToolStripMenuItem GlowItem { get; init; }
    public required ToolStripMenuItem UndoItem { get; init; }
    public required ToolStripMenuItem RedoItem { get; init; }
    public required ToolStripMenuItem ClearItem { get; init; }
    public required ToolStripMenuItem ExitItem { get; init; }
    public required IReadOnlyDictionary<AnnotationTool, ToolStripMenuItem> ToolItems { get; init; }
    public required IReadOnlyList<ToolStripMenuItem> LaserColorItems { get; init; }
    public required IReadOnlyList<ToolStripMenuItem> HighlightColorItems { get; init; }
    public required IReadOnlyList<ToolStripMenuItem> AnnotationColorItems { get; init; }
    public required IReadOnlyList<ToolStripMenuItem> RegionMaskColorItems { get; init; }
}
