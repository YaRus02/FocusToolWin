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
    private readonly ToolStripMenuItem _spotlightItem;
    private readonly ToolStripMenuItem _magnifierItem;
    private readonly ToolStripMenuItem _pinnedLensItem;
    private readonly ToolStripMenuItem _closePinnedLensesItem;
    private readonly ToolStripMenuItem _regionMaskItem;
    private readonly ToolStripMenuItem _clearRegionMasksItem;
    private readonly ToolStripMenuItem _fadingAnnotationsItem;
    private readonly ToolStripMenuItem _toolbarItem;
    private readonly ToolStripMenuItem _screenshotItem;
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
    private readonly Dictionary<AnnotationTool, ToolStripMenuItem> _toolItems = [];
    private readonly List<ToolStripMenuItem> _laserColorItems = [];
    private readonly List<ToolStripMenuItem> _annotationColorItems = [];
    private readonly List<ToolStripMenuItem> _regionMaskColorItems = [];
    private Icon _trayIconImage;
    private string? _lastIconKey;
    private bool _updating;

    public TrayIconController(FocusToolController controller)
    {
        _controller = controller;
        _controller.StateChanged += OnControllerStateChanged;

        _modeItem = new ToolStripMenuItem("Annotate mode") { CheckOnClick = true };
        _modeItem.Click += (_, _) =>
        {
            if (!_updating)
            {
                _controller.SetInteractionMode(_modeItem.Checked ? InteractionMode.Annotate : InteractionMode.Passthrough);
            }
        };

        _statusItem = new ToolStripMenuItem("Mode: Passthrough") { Enabled = false };

        _laserAlwaysModeItem = new ToolStripMenuItem("Always on") { CheckOnClick = true };
        _laserAlwaysModeItem.Click += (_, _) =>
        {
            if (!_updating)
            {
                _controller.SetLaserActivationMode(LaserActivationMode.Always);
            }
        };

        _laserHoldModeItem = new ToolStripMenuItem("Hold key / mouse button") { CheckOnClick = true };
        _laserHoldModeItem.Click += (_, _) =>
        {
            if (!_updating)
            {
                _controller.SetLaserActivationMode(LaserActivationMode.Hold);
            }
        };

        _spotlightItem = new ToolStripMenuItem("Spotlight") { CheckOnClick = true };
        _spotlightItem.Click += (_, _) =>
        {
            if (!_updating)
            {
                _controller.SetSpotlightEnabled(_spotlightItem.Checked);
            }
        };

        _magnifierItem = new ToolStripMenuItem("Magnifier") { CheckOnClick = true };
        _magnifierItem.Click += (_, _) =>
        {
            if (!_updating)
            {
                _controller.SetMagnifierEnabled(_magnifierItem.Checked);
            }
        };

        _pinnedLensItem = new ToolStripMenuItem("New pinned lens");
        _pinnedLensItem.Click += (_, _) =>
        {
            if (!_updating)
            {
                _controller.TogglePinnedLens();
            }
        };

        _closePinnedLensesItem = new ToolStripMenuItem("Close pinned lenses", null, (_, _) => _controller.ClosePinnedLenses());

        _regionMaskItem = new ToolStripMenuItem("New region mask");
        _regionMaskItem.Click += (_, _) =>
        {
            if (!_updating)
            {
                _controller.ToggleRegionMask();
            }
        };

        _clearRegionMasksItem = new ToolStripMenuItem("Clear region masks", null, (_, _) => _controller.ClearRegionMasks());

        _fadingAnnotationsItem = new ToolStripMenuItem("Fading annotations") { CheckOnClick = true };
        _fadingAnnotationsItem.Click += (_, _) =>
        {
            if (!_updating)
            {
                _controller.SetFadingAnnotationsEnabled(_fadingAnnotationsItem.Checked);
            }
        };

        _toolbarItem = new ToolStripMenuItem("Toolbar") { CheckOnClick = true };
        _toolbarItem.Click += (_, _) =>
        {
            if (!_updating)
            {
                _controller.ToggleToolbar();
            }
        };

        _screenshotItem = new ToolStripMenuItem("Screenshot", null, (_, _) => _controller.TakeScreenshot());

        _newTimerItem = new ToolStripMenuItem("New timer");
        _newTimerItem.Click += (_, _) =>
        {
            if (!_updating)
            {
                _controller.NewTimer();
            }
        };

        _closeTimersItem = new ToolStripMenuItem("Close all timers", null, (_, _) => _controller.CloseAllTimers());

        _screenBoardItem = new ToolStripMenuItem("Screen board") { CheckOnClick = true };
        _screenBoardItem.Click += (_, _) =>
        {
            if (!_updating)
            {
                _controller.ToggleScreenBoard();
            }
        };

        _blackScreenItem = new ToolStripMenuItem("Black board") { CheckOnClick = true };
        _blackScreenItem.Click += (_, _) =>
        {
            if (!_updating)
            {
                _controller.SetInteractionMode(_blackScreenItem.Checked ? InteractionMode.BlackScreen : InteractionMode.Passthrough);
            }
        };

        _whiteScreenItem = new ToolStripMenuItem("White board") { CheckOnClick = true };
        _whiteScreenItem.Click += (_, _) =>
        {
            if (!_updating)
            {
                _controller.SetInteractionMode(_whiteScreenItem.Checked ? InteractionMode.WhiteScreen : InteractionMode.Passthrough);
            }
        };

        _glowItem = new ToolStripMenuItem("Laser glow") { CheckOnClick = true };
        _glowItem.Click += (_, _) =>
        {
            if (!_updating)
            {
                _controller.SetGlowEnabled(_glowItem.Checked);
            }
        };

        _undoItem = new ToolStripMenuItem("Undo", null, (_, _) => _controller.UndoAnnotation());
        _redoItem = new ToolStripMenuItem("Redo", null, (_, _) => _controller.RedoAnnotation());
        _clearItem = new ToolStripMenuItem("Clear annotations", null, (_, _) => _controller.ClearAnnotations());

        var tools = new ToolStripMenuItem("Tool");
        AddTool(tools, AnnotationTool.Arrow, "Arrow");
        AddTool(tools, AnnotationTool.Rectangle, "Rectangle");
        AddTool(tools, AnnotationTool.Ellipse, "Ellipse / Circle");
        AddTool(tools, AnnotationTool.Line, "Line");
        AddTool(tools, AnnotationTool.Pencil, "Pencil");
        AddTool(tools, AnnotationTool.Highlighter, "Highlighter");
        AddTool(tools, AnnotationTool.Text, "Text");
        AddTool(tools, AnnotationTool.Move, "Move selection");

        var annotationColors = new ToolStripMenuItem("Annotation color");
        for (var i = 0; i < 5; i++)
        {
            AddAnnotationPreset(annotationColors, i);
        }

        var laserPresets = new ToolStripMenuItem("Laser color");
        for (var i = 0; i < 5; i++)
        {
            AddLaserPreset(laserPresets, i);
        }

        laserPresets.Text = "Color";
        var laserMenu = new ToolStripMenuItem("Laser");
        laserMenu.DropDownItems.Add(_laserAlwaysModeItem);
        laserMenu.DropDownItems.Add(_laserHoldModeItem);
        laserMenu.DropDownItems.Add(new ToolStripSeparator());
        laserMenu.DropDownItems.Add(laserPresets);
        laserMenu.DropDownItems.Add(_glowItem);

        tools.Text = "Tool";
        annotationColors.Text = "Color";
        var drawMenu = new ToolStripMenuItem("Draw");
        drawMenu.DropDownItems.Add(_modeItem);
        drawMenu.DropDownItems.Add(new ToolStripSeparator());
        drawMenu.DropDownItems.Add(tools);
        drawMenu.DropDownItems.Add(annotationColors);
        drawMenu.DropDownItems.Add(_fadingAnnotationsItem);
        drawMenu.DropDownItems.Add(new ToolStripSeparator());
        drawMenu.DropDownItems.Add(_undoItem);
        drawMenu.DropDownItems.Add(_redoItem);
        drawMenu.DropDownItems.Add(_clearItem);

        var pinnedLensMenu = new ToolStripMenuItem("Pinned lens");
        pinnedLensMenu.DropDownItems.Add(_pinnedLensItem);
        pinnedLensMenu.DropDownItems.Add(_closePinnedLensesItem);

        var maskColors = new ToolStripMenuItem("Color");
        for (var i = 0; i < 5; i++)
        {
            AddMaskColor(maskColors, i);
        }

        var regionMaskMenu = new ToolStripMenuItem("Region mask");
        regionMaskMenu.DropDownItems.Add(_regionMaskItem);
        regionMaskMenu.DropDownItems.Add(_clearRegionMasksItem);
        regionMaskMenu.DropDownItems.Add(new ToolStripSeparator());
        regionMaskMenu.DropDownItems.Add(maskColors);

        var boardMenu = new ToolStripMenuItem("Board");
        boardMenu.DropDownItems.Add(_screenBoardItem);
        boardMenu.DropDownItems.Add(_blackScreenItem);
        boardMenu.DropDownItems.Add(_whiteScreenItem);

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add(_statusItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(laserMenu);
        _contextMenu.Items.Add(drawMenu);
        _contextMenu.Items.Add(_spotlightItem);
        _contextMenu.Items.Add(_magnifierItem);
        _contextMenu.Items.Add(pinnedLensMenu);
        _contextMenu.Items.Add(regionMaskMenu);
        _contextMenu.Items.Add(boardMenu);
        _contextMenu.Items.Add(_screenshotItem);

        var timerMenu = new ToolStripMenuItem("Timer");
        timerMenu.DropDownItems.Add(_newTimerItem);
        timerMenu.DropDownItems.Add(_closeTimersItem);
        _contextMenu.Items.Add(timerMenu);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_toolbarItem);
        _contextMenu.Items.Add("Settings...", null, (_, _) => _controller.ShowSettingsWindow());
        _contextMenu.Items.Add(new ToolStripSeparator());
        _exitItem = new ToolStripMenuItem("Exit", null, (_, _) => _controller.Exit());
        _contextMenu.Items.Add(_exitItem);
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
            InteractionMode.PinnedLensSelect => "Mode: Select lens area",
            InteractionMode.RegionMaskSelect => "Mode: Select mask area",
            InteractionMode.ScreenBoard => "Mode: Screen board",
            InteractionMode.BlackScreen => "Mode: Black board",
            InteractionMode.WhiteScreen => "Mode: White board",
            _ => "Mode: Passthrough"
        };
        _modeItem.Checked = _controller.Mode == InteractionMode.Annotate;
        _modeItem.ShortcutKeyDisplayString = _controller.Settings.Shortcuts.ToggleAnnotate;

        _laserAlwaysModeItem.Checked = _controller.ActivationMode == LaserActivationMode.Always;
        _laserHoldModeItem.Checked = _controller.ActivationMode == LaserActivationMode.Hold;
        _laserAlwaysModeItem.ShortcutKeyDisplayString = _controller.Settings.Shortcuts.ToggleLaserActivation;
        _laserHoldModeItem.ShortcutKeyDisplayString = _controller.Settings.LaserHoldShortcut;

        _spotlightItem.Checked = _controller.SpotlightEnabled;
        _spotlightItem.ShortcutKeyDisplayString = _controller.Settings.Shortcuts.ToggleSpotlight;

        _magnifierItem.Checked = _controller.MagnifierEnabled;
        _magnifierItem.ShortcutKeyDisplayString = _controller.MagnifierShortcut;

        _pinnedLensItem.Checked = _controller.PinnedLensActive || _controller.PinnedLensSelectionActive;
        _pinnedLensItem.Text = _controller.PinnedLensSelectionActive
            ? "Pinned lens: select area"
            : _controller.PinnedLensCount > 0
                ? $"New pinned lens ({_controller.PinnedLensCount} active)"
                : "New pinned lens";
        _pinnedLensItem.ShortcutKeyDisplayString = _controller.PinnedLensShortcut;
        _closePinnedLensesItem.Enabled = _controller.PinnedLensActive;

        _regionMaskItem.Checked = _controller.RegionMaskActive || _controller.RegionMaskSelectionActive;
        _regionMaskItem.Text = _controller.RegionMaskSelectionActive
            ? "Region mask: select area"
            : _controller.RegionMaskCount > 0
                ? $"New region mask ({_controller.RegionMaskCount} active)"
                : "New region mask";
        _regionMaskItem.ShortcutKeyDisplayString = _controller.RegionMaskShortcut;
        _clearRegionMasksItem.Enabled = _controller.RegionMaskActive;
        _clearRegionMasksItem.ShortcutKeyDisplayString = _controller.ClearRegionMasksShortcut;

        _fadingAnnotationsItem.Checked = _controller.FadingAnnotationsEnabled;
        _fadingAnnotationsItem.ShortcutKeyDisplayString = _controller.FadingAnnotationsShortcut;

        _toolbarItem.Checked = _controller.ToolbarVisible;
        _toolbarItem.ShortcutKeyDisplayString = _controller.ToolbarShortcut;

        _screenshotItem.ShortcutKeyDisplayString = _controller.ScreenshotShortcut;
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
                ? "FocusTool: Magnifier"
            : _controller.PinnedLensSelectionActive
                ? "FocusTool: Select lens area"
            : _controller.PinnedLensActive
                ? $"FocusTool: {_controller.PinnedLensCount} pinned lens"
            : _controller.RegionMaskSelectionActive
                ? "FocusTool: Select mask area"
            : _controller.RegionMaskActive
                ? $"FocusTool: {_controller.RegionMaskCount} region masks"
            : _controller.BlackScreenEnabled
                ? "FocusTool: Black board"
            : _controller.WhiteScreenEnabled
                ? "FocusTool: White board"
            : _controller.SpotlightEnabled
                ? "FocusTool: Spotlight"
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
            _ => tool.ToString()
        };
    }

    private void AddTool(ToolStripMenuItem parent, AnnotationTool tool, string title)
    {
        var item = new ToolStripMenuItem(title) { CheckOnClick = true };
        item.Click += (_, _) =>
        {
            if (!_updating)
            {
                _controller.SetAnnotationTool(tool);
            }
        };

        _toolItems[tool] = item;
        parent.DropDownItems.Add(item);
    }

    private void AddAnnotationPreset(ToolStripMenuItem parent, int index)
    {
        var item = new ToolStripMenuItem($"Color {index + 1}");
        item.Click += (_, _) =>
        {
            if (!_updating)
            {
                _controller.SetAnnotationPresetColor(index);
            }
        };

        _annotationColorItems.Add(item);
        parent.DropDownItems.Add(item);
    }

    private void AddLaserPreset(ToolStripMenuItem parent, int index)
    {
        var item = new ToolStripMenuItem($"Color {index + 1}");
        item.Click += (_, _) =>
        {
            if (!_updating)
            {
                _controller.SetLaserPresetColor(index);
            }
        };

        _laserColorItems.Add(item);
        parent.DropDownItems.Add(item);
    }

    private void AddMaskColor(ToolStripMenuItem parent, int index)
    {
        var item = new ToolStripMenuItem($"Color {index + 1}");
        item.Click += (_, _) =>
        {
            if (!_updating)
            {
                _controller.SetRegionMaskPresetColor(index);
            }
        };

        _regionMaskColorItems.Add(item);
        parent.DropDownItems.Add(item);
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
