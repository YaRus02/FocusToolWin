using System.Windows.Forms;
using FocusTool.Win.Models;
using FocusTool.Win.Services;

namespace FocusTool.Win.Tray;

internal sealed class TrayMenuBuilder
{
    private readonly FocusToolController _controller;
    private readonly Func<bool> _isUpdating;
    private readonly Dictionary<AnnotationTool, ToolStripMenuItem> _toolItems = [];
    private readonly List<ToolStripMenuItem> _laserColorItems = [];
    private readonly List<ToolStripMenuItem> _highlightColorItems = [];
    private readonly List<ToolStripMenuItem> _annotationColorItems = [];
    private readonly List<ToolStripMenuItem> _regionMaskColorItems = [];

    private TrayMenuBuilder(FocusToolController controller, Func<bool> isUpdating)
    {
        _controller = controller;
        _isUpdating = isUpdating;
    }

    public static TrayMenuItems Build(FocusToolController controller, Func<bool> isUpdating)
    {
        return new TrayMenuBuilder(controller, isUpdating).Build();
    }

    private TrayMenuItems Build()
    {
        var modeItem = CreateCheckedItem("Annotate mode", item =>
            _controller.SetInteractionMode(item.Checked ? InteractionMode.Annotate : InteractionMode.Passthrough));
        var statusItem = new ToolStripMenuItem("Mode: Passthrough") { Enabled = false };

        var laserAlwaysModeItem = CreateCheckedItem("Always on", _ => _controller.SetLaserActivationMode(LaserActivationMode.Always));
        var laserHoldModeItem = CreateCheckedItem("Hold key / mouse button", _ => _controller.SetLaserActivationMode(LaserActivationMode.Hold));
        var cursorHighlightAlwaysModeItem = CreateCheckedItem("Always on", _ => _controller.SetCursorHighlightActivationMode(LaserActivationMode.Always));
        var cursorHighlightHoldModeItem = CreateCheckedItem("Hold key / mouse button", _ => _controller.SetCursorHighlightActivationMode(LaserActivationMode.Hold));
        var cursorHighlightPulseItem = CreateCheckedItem("Click pulse", item => _controller.SetClickPulseEnabled(item.Checked));
        var spotlightItem = CreateCheckedItem("Spotlight", item => _controller.SetSpotlightEnabled(item.Checked));
        var regionSpotlightMenuItem = new ToolStripMenuItem("Region spotlight");
        var regionSpotlightItem = CreateItem("Select area", _controller.ToggleRegionSpotlight);
        var clearRegionSpotlightsItem = new ToolStripMenuItem("Clear region spotlights", null, (_, _) => _controller.ClearRegionSpotlights());
        var magnifierItem = CreateCheckedItem("Zoom", item => _controller.SetMagnifierEnabled(item.Checked));
        var pinnedLensItem = CreateItem("New pinned lens", _controller.TogglePinnedLens);
        var closePinnedLensesItem = new ToolStripMenuItem("Close all pinned lenses", null, (_, _) => _controller.ClosePinnedLenses());
        var regionMaskItem = CreateItem("Mask", _controller.ToggleRegionMask);
        var clearRegionMasksItem = new ToolStripMenuItem("Clear masks", null, (_, _) => _controller.ClearRegionMasks());
        var fadingAnnotationsItem = CreateCheckedItem("Fading annotations", item => _controller.SetFadingAnnotationsEnabled(item.Checked));
        var toolbarItem = CreateCheckedItem("Toolbar", _ => _controller.ToggleToolbar());
        var screenshotItem = new ToolStripMenuItem("Current monitor", null, (_, _) => _controller.TakeScreenshot());
        var regionScreenshotItem = new ToolStripMenuItem("Region screenshot", null, (_, _) => _controller.TakeRegionScreenshot());
        var newTimerItem = CreateItem("New timer", _controller.NewTimer);
        var closeTimersItem = new ToolStripMenuItem("Close all timers", null, (_, _) => _controller.CloseAllTimers());
        var screenBoardItem = CreateCheckedItem("Screen board", _ => _controller.ToggleScreenBoard());
        var blackScreenItem = CreateCheckedItem("Black board", item =>
            _controller.SetInteractionMode(item.Checked ? InteractionMode.BlackScreen : InteractionMode.Passthrough));
        var whiteScreenItem = CreateCheckedItem("White board", item =>
            _controller.SetInteractionMode(item.Checked ? InteractionMode.WhiteScreen : InteractionMode.Passthrough));
        var glowItem = CreateCheckedItem("Laser glow", item => _controller.SetGlowEnabled(item.Checked));
        var undoItem = new ToolStripMenuItem("Undo", null, (_, _) => _controller.UndoAnnotation());
        var redoItem = new ToolStripMenuItem("Redo", null, (_, _) => _controller.RedoAnnotation());
        var clearItem = new ToolStripMenuItem("Clear annotations", null, (_, _) => _controller.ClearAnnotations());
        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => _controller.Exit());

        var tools = new ToolStripMenuItem("Tool");
        AddTool(tools, AnnotationTool.Arrow, "Arrow");
        AddTool(tools, AnnotationTool.Rectangle, "Rectangle");
        AddTool(tools, AnnotationTool.Ellipse, "Ellipse / Circle");
        AddTool(tools, AnnotationTool.Line, "Line");
        AddTool(tools, AnnotationTool.Pencil, "Pencil");
        AddTool(tools, AnnotationTool.Highlighter, "Highlighter");
        AddTool(tools, AnnotationTool.Text, "Text");
        AddTool(tools, AnnotationTool.Move, "Move selection");
        AddTool(tools, AnnotationTool.StepOval, "Step oval");
        AddTool(tools, AnnotationTool.StepRect, "Step rectangle");

        var annotationColors = new ToolStripMenuItem("Color");
        var laserPresets = new ToolStripMenuItem("Color");
        var highlightPresets = new ToolStripMenuItem("Color");
        var maskColors = new ToolStripMenuItem("Color");
        for (var i = 0; i < 5; i++)
        {
            AddAnnotationPreset(annotationColors, i);
            AddLaserPreset(laserPresets, i);
            AddHighlightPreset(highlightPresets, i);
            AddMaskColor(maskColors, i);
        }

        var laserMenu = new ToolStripMenuItem("Laser");
        laserMenu.DropDownItems.Add(laserAlwaysModeItem);
        laserMenu.DropDownItems.Add(laserHoldModeItem);
        laserMenu.DropDownItems.Add(new ToolStripSeparator());
        laserMenu.DropDownItems.Add(laserPresets);
        laserMenu.DropDownItems.Add(glowItem);

        var cursorHighlightMenu = new ToolStripMenuItem("Cursor highlight");
        cursorHighlightMenu.DropDownItems.Add(cursorHighlightAlwaysModeItem);
        cursorHighlightMenu.DropDownItems.Add(cursorHighlightHoldModeItem);
        cursorHighlightMenu.DropDownItems.Add(new ToolStripSeparator());
        cursorHighlightMenu.DropDownItems.Add(highlightPresets);
        cursorHighlightMenu.DropDownItems.Add(cursorHighlightPulseItem);

        regionSpotlightMenuItem.DropDownItems.Add(regionSpotlightItem);
        regionSpotlightMenuItem.DropDownItems.Add(clearRegionSpotlightsItem);

        var drawMenu = new ToolStripMenuItem("Draw");
        drawMenu.DropDownItems.Add(modeItem);
        drawMenu.DropDownItems.Add(new ToolStripSeparator());
        drawMenu.DropDownItems.Add(tools);
        drawMenu.DropDownItems.Add(annotationColors);
        drawMenu.DropDownItems.Add(fadingAnnotationsItem);
        drawMenu.DropDownItems.Add(new ToolStripSeparator());
        drawMenu.DropDownItems.Add(undoItem);
        drawMenu.DropDownItems.Add(redoItem);
        drawMenu.DropDownItems.Add(clearItem);

        var pinnedLensMenu = new ToolStripMenuItem("Pinned lens");
        pinnedLensMenu.DropDownItems.Add(pinnedLensItem);
        pinnedLensMenu.DropDownItems.Add(closePinnedLensesItem);

        var regionMaskMenu = new ToolStripMenuItem("Mask");
        regionMaskMenu.DropDownItems.Add(regionMaskItem);
        regionMaskMenu.DropDownItems.Add(clearRegionMasksItem);
        regionMaskMenu.DropDownItems.Add(new ToolStripSeparator());
        regionMaskMenu.DropDownItems.Add(maskColors);

        var boardMenu = new ToolStripMenuItem("Board");
        boardMenu.DropDownItems.Add(screenBoardItem);
        boardMenu.DropDownItems.Add(blackScreenItem);
        boardMenu.DropDownItems.Add(whiteScreenItem);

        var screenshotMenu = new ToolStripMenuItem("Screenshot");
        screenshotMenu.DropDownItems.Add(screenshotItem);
        screenshotMenu.DropDownItems.Add(regionScreenshotItem);

        var timerMenu = new ToolStripMenuItem("Timer");
        timerMenu.DropDownItems.Add(newTimerItem);
        timerMenu.DropDownItems.Add(closeTimersItem);

        var captureStageMenu = new ToolStripMenuItem("Capture Stage");
        captureStageMenu.DropDownItems.Add("Pick source...", null, async (_, _) => await _controller.StartCaptureStageWithPickerAsync());
        captureStageMenu.DropDownItems.Add("Mirror focused window", null, (_, _) => _controller.StartCaptureStageForLastWindow());
        captureStageMenu.DropDownItems.Add("Close all", null, (_, _) => _controller.CloseCaptureStages());

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(statusItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(laserMenu);
        contextMenu.Items.Add(cursorHighlightMenu);
        contextMenu.Items.Add(spotlightItem);
        contextMenu.Items.Add(regionSpotlightMenuItem);
        contextMenu.Items.Add(magnifierItem);
        contextMenu.Items.Add(pinnedLensMenu);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(drawMenu);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(regionMaskMenu);
        contextMenu.Items.Add(boardMenu);
        contextMenu.Items.Add(screenshotMenu);
        contextMenu.Items.Add(timerMenu);
        contextMenu.Items.Add(captureStageMenu);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(toolbarItem);
        contextMenu.Items.Add("Settings...", null, (_, _) => _controller.ShowSettingsWindow());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        return new TrayMenuItems
        {
            ContextMenu = contextMenu,
            StatusItem = statusItem,
            ModeItem = modeItem,
            LaserAlwaysModeItem = laserAlwaysModeItem,
            LaserHoldModeItem = laserHoldModeItem,
            CursorHighlightAlwaysModeItem = cursorHighlightAlwaysModeItem,
            CursorHighlightHoldModeItem = cursorHighlightHoldModeItem,
            CursorHighlightPulseItem = cursorHighlightPulseItem,
            SpotlightItem = spotlightItem,
            RegionSpotlightMenuItem = regionSpotlightMenuItem,
            RegionSpotlightItem = regionSpotlightItem,
            ClearRegionSpotlightsItem = clearRegionSpotlightsItem,
            MagnifierItem = magnifierItem,
            PinnedLensItem = pinnedLensItem,
            ClosePinnedLensesItem = closePinnedLensesItem,
            RegionMaskItem = regionMaskItem,
            ClearRegionMasksItem = clearRegionMasksItem,
            FadingAnnotationsItem = fadingAnnotationsItem,
            ToolbarItem = toolbarItem,
            ScreenshotItem = screenshotItem,
            RegionScreenshotItem = regionScreenshotItem,
            NewTimerItem = newTimerItem,
            CloseTimersItem = closeTimersItem,
            ScreenBoardItem = screenBoardItem,
            BlackScreenItem = blackScreenItem,
            WhiteScreenItem = whiteScreenItem,
            GlowItem = glowItem,
            UndoItem = undoItem,
            RedoItem = redoItem,
            ClearItem = clearItem,
            ExitItem = exitItem,
            ToolItems = _toolItems,
            LaserColorItems = _laserColorItems,
            HighlightColorItems = _highlightColorItems,
            AnnotationColorItems = _annotationColorItems,
            RegionMaskColorItems = _regionMaskColorItems
        };
    }

    private ToolStripMenuItem CreateItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) =>
        {
            if (!_isUpdating())
            {
                action();
            }
        };

        return item;
    }

    private ToolStripMenuItem CreateCheckedItem(string text, Action<ToolStripMenuItem> action)
    {
        var item = new ToolStripMenuItem(text) { CheckOnClick = true };
        item.Click += (_, _) =>
        {
            if (!_isUpdating())
            {
                action(item);
            }
        };

        return item;
    }

    private void AddTool(ToolStripMenuItem parent, AnnotationTool tool, string title)
    {
        var item = CreateCheckedItem(title, _ => _controller.SetAnnotationTool(tool));
        _toolItems[tool] = item;
        parent.DropDownItems.Add(item);
    }

    private void AddAnnotationPreset(ToolStripMenuItem parent, int index)
    {
        var item = CreateItem($"Color {index + 1}", () => _controller.SetAnnotationPresetColor(index));
        _annotationColorItems.Add(item);
        parent.DropDownItems.Add(item);
    }

    private void AddLaserPreset(ToolStripMenuItem parent, int index)
    {
        var item = CreateItem($"Color {index + 1}", () => _controller.SetLaserPresetColor(index));
        _laserColorItems.Add(item);
        parent.DropDownItems.Add(item);
    }

    private void AddHighlightPreset(ToolStripMenuItem parent, int index)
    {
        var item = CreateItem($"Color {index + 1}", () => _controller.SetCursorHighlightPresetColor(index));
        _highlightColorItems.Add(item);
        parent.DropDownItems.Add(item);
    }

    private void AddMaskColor(ToolStripMenuItem parent, int index)
    {
        var item = CreateItem($"Color {index + 1}", () => _controller.SetRegionMaskPresetColor(index));
        _regionMaskColorItems.Add(item);
        parent.DropDownItems.Add(item);
    }
}
