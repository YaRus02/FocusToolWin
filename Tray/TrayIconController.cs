using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FocusTool.Win.Models;
using FocusTool.Win.Services;

namespace FocusTool.Win.Tray;

internal sealed class TrayIconController : IDisposable
{
    private readonly FocusToolController _controller;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _modeItem;
    private readonly ToolStripMenuItem _laserAlwaysModeItem;
    private readonly ToolStripMenuItem _laserHoldModeItem;
    private readonly ToolStripMenuItem _cursorHighlightAlwaysModeItem;
    private readonly ToolStripMenuItem _cursorHighlightHoldModeItem;
    private readonly ToolStripMenuItem _cursorHighlightPulseItem;
    private readonly ToolStripMenuItem _spotlightItem;
    private readonly ToolStripMenuItem _regionSpotlightItem;
    private readonly ToolStripMenuItem _clearRegionSpotlightsItem;
    private readonly ToolStripMenuItem _magnifierItem;
    private readonly ToolStripMenuItem _pinnedLensItem;
    private readonly ToolStripMenuItem _closePinnedLensesItem;
    private readonly ToolStripMenuItem _regionMaskItem;
    private readonly ToolStripMenuItem _clearRegionMasksItem;
    private readonly ToolStripMenuItem _fadingAnnotationsItem;
    private readonly ToolStripMenuItem _toolbarItem;
    private readonly ToolStripMenuItem _screenshotItem;
    private readonly ToolStripMenuItem _regionScreenshotItem;
    private readonly ToolStripMenuItem _newTimerItem;
    private readonly ToolStripMenuItem _closeTimersItem;
    private readonly ToolStripMenuItem _screenBoardItem;
    private readonly ToolStripMenuItem _blackScreenItem;
    private readonly ToolStripMenuItem _whiteScreenItem;
    private readonly ToolStripMenuItem _glowItem;
    private readonly ToolStripMenuItem _undoItem;
    private readonly ToolStripMenuItem _redoItem;
    private readonly ToolStripMenuItem _clearItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly IReadOnlyDictionary<AnnotationTool, ToolStripMenuItem> _toolItems;
    private readonly IReadOnlyList<ToolStripMenuItem> _laserColorItems;
    private readonly IReadOnlyList<ToolStripMenuItem> _highlightColorItems;
    private readonly IReadOnlyList<ToolStripMenuItem> _annotationColorItems;
    private readonly IReadOnlyList<ToolStripMenuItem> _regionMaskColorItems;
    private Icon _trayIconImage;
    private string? _lastIconKey;
    private bool _updating;

    public TrayIconController(FocusToolController controller)
    {
        _controller = controller;
        _controller.StateChanged += OnControllerStateChanged;

        var menuItems = TrayMenuBuilder.Build(_controller, () => _updating);
        _contextMenu = menuItems.ContextMenu;
        _statusItem = menuItems.StatusItem;
        _modeItem = menuItems.ModeItem;
        _laserAlwaysModeItem = menuItems.LaserAlwaysModeItem;
        _laserHoldModeItem = menuItems.LaserHoldModeItem;
        _cursorHighlightAlwaysModeItem = menuItems.CursorHighlightAlwaysModeItem;
        _cursorHighlightHoldModeItem = menuItems.CursorHighlightHoldModeItem;
        _cursorHighlightPulseItem = menuItems.CursorHighlightPulseItem;
        _spotlightItem = menuItems.SpotlightItem;
        _regionSpotlightItem = menuItems.RegionSpotlightItem;
        _clearRegionSpotlightsItem = menuItems.ClearRegionSpotlightsItem;
        _magnifierItem = menuItems.MagnifierItem;
        _pinnedLensItem = menuItems.PinnedLensItem;
        _closePinnedLensesItem = menuItems.ClosePinnedLensesItem;
        _regionMaskItem = menuItems.RegionMaskItem;
        _clearRegionMasksItem = menuItems.ClearRegionMasksItem;
        _fadingAnnotationsItem = menuItems.FadingAnnotationsItem;
        _toolbarItem = menuItems.ToolbarItem;
        _screenshotItem = menuItems.ScreenshotItem;
        _regionScreenshotItem = menuItems.RegionScreenshotItem;
        _newTimerItem = menuItems.NewTimerItem;
        _closeTimersItem = menuItems.CloseTimersItem;
        _screenBoardItem = menuItems.ScreenBoardItem;
        _blackScreenItem = menuItems.BlackScreenItem;
        _whiteScreenItem = menuItems.WhiteScreenItem;
        _glowItem = menuItems.GlowItem;
        _undoItem = menuItems.UndoItem;
        _redoItem = menuItems.RedoItem;
        _clearItem = menuItems.ClearItem;
        _exitItem = menuItems.ExitItem;
        _toolItems = menuItems.ToolItems;
        _laserColorItems = menuItems.LaserColorItems;
        _highlightColorItems = menuItems.HighlightColorItems;
        _annotationColorItems = menuItems.AnnotationColorItems;
        _regionMaskColorItems = menuItems.RegionMaskColorItems;
        _contextMenu.Opening += (_, _) => UpdateMenuState();

        _trayIconImage = CreateTrayIcon(CurrentIconColor(), _controller.LaserVisuallyActive);

        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIconImage,
            Text = "FocusTool",
            ContextMenuStrip = _contextMenu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => _controller.ToggleAnnotateMode();

        UpdateMenuState();
    }

    public void ShowMessage(string title, string text)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        _controller.StateChanged -= OnControllerStateChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip = null;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _trayIconImage.Dispose();
    }

    private void OnControllerStateChanged(object? sender, EventArgs e)
    {
        UpdateMenuState();
    }

    private void UpdateMenuState()
    {
        _updating = true;

        _statusItem.Text = _controller.Mode switch
        {
            InteractionMode.Annotate => "Mode: Annotate",
            InteractionMode.PinnedLensSelect => "Mode: Select pin area",
            InteractionMode.RegionMaskSelect => "Mode: Select mask areas",
            InteractionMode.ScreenshotRegionSelect => "Mode: Select Region Screenshot",
            InteractionMode.RegionSpotlightSelect => "Mode: Select Region Spotlight",
            InteractionMode.ScreenBoard => "Mode: Screen board",
            InteractionMode.BlackScreen => "Mode: Black board",
            InteractionMode.WhiteScreen => "Mode: White board",
            _ => "Mode: Passthrough"
        };
        _modeItem.Checked = _controller.Mode == InteractionMode.Annotate;
        _modeItem.ShortcutKeyDisplayString = ShortcutSettings.IsShortcutDisabled(_controller.PushToAnnotateShortcut)
            ? _controller.Settings.Shortcuts.ToggleAnnotate
            : $"{_controller.Settings.Shortcuts.ToggleAnnotate} / hold {_controller.PushToAnnotateShortcut}";

        _laserAlwaysModeItem.Checked = _controller.ActivationMode == LaserActivationMode.Always;
        _laserHoldModeItem.Checked = _controller.ActivationMode == LaserActivationMode.Hold;
        _laserAlwaysModeItem.ShortcutKeyDisplayString = _controller.Settings.Shortcuts.ToggleLaserActivation;
        _laserHoldModeItem.ShortcutKeyDisplayString = _controller.Settings.LaserHoldShortcut;

        _cursorHighlightAlwaysModeItem.Checked = _controller.Settings.GetCursorHighlightActivationMode() == LaserActivationMode.Always;
        _cursorHighlightAlwaysModeItem.ShortcutKeyDisplayString = _controller.CursorHighlightShortcut;
        _cursorHighlightHoldModeItem.Checked = _controller.Settings.GetCursorHighlightActivationMode() == LaserActivationMode.Hold;
        _cursorHighlightHoldModeItem.ShortcutKeyDisplayString = _controller.Settings.CursorHighlightHoldShortcut;
        _cursorHighlightPulseItem.Checked = _controller.ClickPulseEnabled;

        _spotlightItem.Checked = _controller.SpotlightEnabled;
        _spotlightItem.ShortcutKeyDisplayString = _controller.Settings.Shortcuts.ToggleSpotlight;
        _regionSpotlightItem.Checked = _controller.RegionSpotlightSelectionActive || _controller.RegionSpotlightActive;
        _regionSpotlightItem.Text = _controller.RegionSpotlightSelectionActive
            ? "Region Spotlight: select area"
            : _controller.RegionSpotlightCount > 0
                ? $"Region Spotlight ({_controller.RegionSpotlightCount} active)"
                : "Region Spotlight";
        _regionSpotlightItem.ShortcutKeyDisplayString = _controller.RegionSpotlightShortcut;
        _clearRegionSpotlightsItem.Enabled = _controller.RegionSpotlightActive;
        _clearRegionSpotlightsItem.ShortcutKeyDisplayString = _controller.ClearRegionSpotlightsShortcut;

        _magnifierItem.Checked = _controller.MagnifierEnabled;
        _magnifierItem.ShortcutKeyDisplayString = _controller.MagnifierShortcut;

        _pinnedLensItem.Checked = _controller.PinnedLensActive || _controller.PinnedLensSelectionActive;
        _pinnedLensItem.Text = _controller.PinnedLensSelectionActive
            ? "Pin: select area"
            : _controller.PinnedLensCount > 0
                ? $"New pin ({_controller.PinnedLensCount} active)"
                : "New pin";
        _pinnedLensItem.ShortcutKeyDisplayString = _controller.PinnedLensShortcut;
        _closePinnedLensesItem.Enabled = _controller.PinnedLensActive;

        _regionMaskItem.Checked = _controller.RegionMaskActive || _controller.RegionMaskSelectionActive;
        _regionMaskItem.Text = _controller.RegionMaskSelectionActive
            ? "Mask: select areas"
            : _controller.RegionMaskCount > 0
                ? $"Masks ({_controller.RegionMaskCount} active)"
                : "Mask";
        _regionMaskItem.ShortcutKeyDisplayString = _controller.RegionMaskShortcut;
        _clearRegionMasksItem.Enabled = _controller.RegionMaskActive;
        _clearRegionMasksItem.ShortcutKeyDisplayString = _controller.ClearRegionMasksShortcut;

        _fadingAnnotationsItem.Checked = _controller.FadingAnnotationsEnabled;
        _fadingAnnotationsItem.ShortcutKeyDisplayString = _controller.FadingAnnotationsShortcut;

        _toolbarItem.Checked = _controller.ToolbarVisible;
        _toolbarItem.ShortcutKeyDisplayString = _controller.ToolbarShortcut;

        _screenshotItem.ShortcutKeyDisplayString = _controller.ScreenshotShortcut;
        _regionScreenshotItem.Checked = _controller.ScreenshotRegionSelectionActive;
        _regionScreenshotItem.ShortcutKeyDisplayString = _controller.RegionScreenshotShortcut;
        _newTimerItem.Text = _controller.TimerCount > 0 ? $"New timer ({_controller.TimerCount} active)" : "New timer";
        _newTimerItem.ShortcutKeyDisplayString = _controller.TimerShortcut;
        _closeTimersItem.Enabled = _controller.TimerActive;
        _screenBoardItem.Checked = _controller.ScreenBoardEnabled;
        _screenBoardItem.ShortcutKeyDisplayString = _controller.ScreenBoardShortcut;

        _blackScreenItem.Checked = _controller.BlackScreenEnabled;
        _blackScreenItem.ShortcutKeyDisplayString = _controller.Settings.Shortcuts.ToggleBlackScreen;
        _whiteScreenItem.Checked = _controller.WhiteScreenEnabled;
        _whiteScreenItem.ShortcutKeyDisplayString = _controller.Settings.Shortcuts.ToggleWhiteScreen;

        _glowItem.Checked = _controller.Settings.GlowEnabled;

        UpdateColorMenuItems(_laserColorItems, _controller.Settings.LaserColorPresets, _controller.Settings.Color);
        UpdateColorMenuItems(_highlightColorItems, _controller.Settings.CursorHighlightColorPresets, _controller.Settings.CursorHighlightColor);

        foreach (var (tool, item) in _toolItems)
        {
            item.Checked = _controller.CurrentTool == tool;
            item.ShortcutKeyDisplayString = GetToolShortcut(tool);
        }

        UpdateColorMenuItems(_annotationColorItems, _controller.Settings.AnnotationColorPresets, _controller.Settings.AnnotationColor, includeShortcuts: true);
        UpdateColorMenuItems(_regionMaskColorItems, _controller.Settings.RegionMaskColorPresets, _controller.Settings.RegionMaskColor);

        _undoItem.ShortcutKeyDisplayString = _controller.Settings.Shortcuts.Undo;
        _redoItem.ShortcutKeyDisplayString = _controller.Settings.Shortcuts.Redo;
        _clearItem.ShortcutKeyDisplayString = $"{_controller.Settings.Shortcuts.Clear} / {_controller.Settings.Shortcuts.ClearAlternate}";
        _undoItem.Enabled = _controller.Annotations.CanUndo;
        _redoItem.Enabled = _controller.Annotations.CanRedo;
        _clearItem.Enabled = _controller.Annotations.Shapes.Count > 0 || _controller.Annotations.Draft is not null;
        _exitItem.ShortcutKeyDisplayString = _controller.Settings.Shortcuts.ExitApp;

        var notifyText = _controller.Mode == InteractionMode.Annotate
            ? $"Annotate: {ToolName(_controller.CurrentTool)}"
            : _controller.ScreenBoardEnabled
                ? "FocusTool: Screen board"
            : _controller.MagnifierEnabled
                ? "FocusTool: Zoom"
            : _controller.PinnedLensSelectionActive
                ? "FocusTool: Select pin area"
            : _controller.PinnedLensActive
                ? $"FocusTool: {_controller.PinnedLensCount} pins"
            : _controller.RegionMaskSelectionActive
                ? "FocusTool: Select mask areas"
            : _controller.RegionMaskActive
                ? $"FocusTool: {_controller.RegionMaskCount} region masks"
            : _controller.ScreenshotRegionSelectionActive
                ? "FocusTool: Select Region Screenshot"
            : _controller.RegionSpotlightSelectionActive
                ? "FocusTool: Select Region Spotlight"
            : _controller.RegionSpotlightActive
                ? $"FocusTool: {_controller.RegionSpotlightCount} region spotlights"
            : _controller.BlackScreenEnabled
                ? "FocusTool: Black board"
            : _controller.WhiteScreenEnabled
                ? "FocusTool: White board"
            : _controller.SpotlightEnabled
                ? "FocusTool: Spotlight"
            : _controller.CursorHighlightEnabled
                ? "FocusTool: Highlight"
            : _controller.ClickPulseEnabled
                ? "FocusTool: Click pulse"
            : _controller.ActivationMode == LaserActivationMode.Always
                ? "FocusTool: Always"
                : $"FocusTool: Hold {_controller.Settings.LaserHoldShortcut}";
        _notifyIcon.Text = TrimNotifyText(notifyText);

        UpdateTrayIcon();
        _updating = false;
    }

    private static string ToolName(AnnotationTool tool)
    {
        return tool switch
        {
            AnnotationTool.Arrow => "Arrow",
            AnnotationTool.Rectangle => "Rectangle",
            AnnotationTool.Ellipse => "Ellipse / Circle",
            AnnotationTool.Line => "Line",
            AnnotationTool.Pencil => "Pencil",
            AnnotationTool.Highlighter => "Highlighter",
            AnnotationTool.Text => "Text",
            AnnotationTool.Move => "Move selection",
            AnnotationTool.StepOval => "Step oval",
            AnnotationTool.StepRect => "Step rectangle",
            _ => tool.ToString()
        };
    }

    private string GetToolShortcut(AnnotationTool tool)
    {
        return tool switch
        {
            AnnotationTool.Arrow => _controller.Settings.Shortcuts.ToolArrow,
            AnnotationTool.Rectangle => _controller.Settings.Shortcuts.ToolRectangle,
            AnnotationTool.Ellipse => _controller.Settings.Shortcuts.ToolEllipse,
            AnnotationTool.Line => _controller.Settings.Shortcuts.ToolLine,
            AnnotationTool.Pencil => _controller.Settings.Shortcuts.ToolPencil,
            AnnotationTool.Highlighter => _controller.Settings.Shortcuts.ToolHighlighter,
            AnnotationTool.Text => _controller.Settings.Shortcuts.ToolText,
            AnnotationTool.Move => _controller.Settings.Shortcuts.ToolMove,
            AnnotationTool.StepOval or AnnotationTool.StepRect => _controller.Settings.Shortcuts.ToolStep,
            _ => string.Empty
        };
    }

    private string GetColorShortcut(int index)
    {
        return index switch
        {
            0 => _controller.Settings.Shortcuts.Color1,
            1 => _controller.Settings.Shortcuts.Color2,
            2 => _controller.Settings.Shortcuts.Color3,
            3 => _controller.Settings.Shortcuts.Color4,
            4 => _controller.Settings.Shortcuts.Color5,
            _ => string.Empty
        };
    }

    private void UpdateColorMenuItems(
        IReadOnlyList<ToolStripMenuItem> items,
        IReadOnlyList<string> presets,
        string currentColor,
        bool includeShortcuts = false)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var color = i < presets.Count ? presets[i] : "#FFFFFFFF";
            items[i].Text = $"Color {i + 1} ({color})";
            items[i].Checked = string.Equals(currentColor, color, StringComparison.OrdinalIgnoreCase);
            items[i].ShortcutKeyDisplayString = includeShortcuts ? GetColorShortcut(i) : string.Empty;
        }
    }

    private void UpdateTrayIcon()
    {
        var iconColor = CurrentIconColor();
        var iconKey = $"{iconColor.A:X2}{iconColor.R:X2}{iconColor.G:X2}{iconColor.B:X2}:{_controller.LaserVisuallyActive}";

        if (_lastIconKey == iconKey)
        {
            return;
        }

        var oldIcon = _trayIconImage;
        _trayIconImage = CreateTrayIcon(iconColor, _controller.LaserVisuallyActive);
        _notifyIcon.Icon = _trayIconImage;
        oldIcon.Dispose();

        _lastIconKey = iconKey;
    }

    private System.Windows.Media.Color CurrentIconColor()
    {
        return IsAnnotationMode(_controller.Mode)
            ? _controller.Settings.ToAnnotationMediaColor()
            : _controller.Settings.ToMediaColor();
    }

    private static bool IsAnnotationMode(InteractionMode mode)
    {
        return mode is InteractionMode.Annotate or InteractionMode.ScreenBoard or InteractionMode.BlackScreen or InteractionMode.WhiteScreen;
    }

    private static Icon CreateTrayIcon(System.Windows.Media.Color mediaColor, bool active)
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        var sourceAlpha = mediaColor.A / 255.0;
        var laserColor = Color.FromArgb((int)Math.Round(sourceAlpha * (active ? 255 : 120)), mediaColor.R, mediaColor.G, mediaColor.B);
        var glowColor = Color.FromArgb((int)Math.Round(sourceAlpha * (active ? 90 : 28)), mediaColor.R, mediaColor.G, mediaColor.B);
        var tailColor = Color.FromArgb((int)Math.Round(sourceAlpha * (active ? 175 : 78)), mediaColor.R, mediaColor.G, mediaColor.B);
        var thinTailColor = Color.FromArgb((int)Math.Round(sourceAlpha * (active ? 95 : 44)), mediaColor.R, mediaColor.G, mediaColor.B);

        using var tailPen = new Pen(tailColor, 4.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var thinTailPen = new Pen(thinTailColor, 2.0f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var glowBrush = new SolidBrush(glowColor);
        using var headBrush = new SolidBrush(laserColor);
        using var hotBrush = new SolidBrush(
            Color.FromArgb(
                (int)Math.Round(sourceAlpha * 230),
                255,
                255,
                255));

        graphics.DrawBezier(tailPen, 5, 24, 10, 16, 17, 14, 22, 10);
        graphics.DrawBezier(thinTailPen, 6, 25, 11, 17, 17, 15, 23, 11);
        graphics.FillEllipse(glowBrush, 15, 3, 15, 15);
        graphics.FillEllipse(headBrush, 18, 6, 9, 9);
        graphics.FillEllipse(hotBrush, 21, 8, 3, 3);

        if (!active)
        {
            using var slashPen = new Pen(Color.FromArgb(185, 105, 105, 105), 2.4f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(slashPen, 7, 25, 25, 7);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static string TrimNotifyText(string text)
    {
        return text.Length <= 63 ? text : text[..60] + "...";
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
