using FocusTool.Win.Models;
using FocusTool.Win.Overlay;
using DrawingPoint = System.Drawing.Point;
using Forms = System.Windows.Forms;

namespace FocusTool.Win.Services;

internal sealed class RegionMaskContextMenuController : IDisposable
{
    private readonly Func<int, RegionMaskStyle?> _styleProvider;
    private readonly Action<int, RegionMaskStyle> _setStyle;
    private readonly Action<int> _deleteMask;
    private readonly List<Forms.Timer> _topmostTimers = [];
    private Forms.ContextMenuStrip? _menu;
    private int _maskId;
    private bool _actionTaken;

    public RegionMaskContextMenuController(
        Func<int, RegionMaskStyle?> styleProvider,
        Action<int, RegionMaskStyle> setStyle,
        Action<int> deleteMask)
    {
        _styleProvider = styleProvider;
        _setStyle = setStyle;
        _deleteMask = deleteMask;
    }

    public bool IsVisible => _menu is { Visible: true };

    public void Show(ScreenPoint point, int maskId)
    {
        var menu = GetMenu();
        if (menu.Visible)
        {
            _actionTaken = true;
            menu.Close(Forms.ToolStripDropDownCloseReason.CloseCalled);
        }

        _maskId = maskId;
        _actionTaken = false;
        menu.Show(new DrawingPoint((int)Math.Round(point.X), (int)Math.Round(point.Y)));
    }

    public void ReassertTopmostIfVisible()
    {
        if (_menu is { Visible: true } menu)
        {
            TopmostContextMenuHelper.ReassertIfVisible(menu);
        }
    }

    private Forms.ContextMenuStrip GetMenu()
    {
        if (_menu is not null)
        {
            return _menu;
        }

        var menu = new Forms.ContextMenuStrip();
        var styleMenu = new Forms.ToolStripMenuItem("Style");
        AddStyleItem(styleMenu, "Solid", RegionMaskStyle.Solid);
        AddStyleItem(styleMenu, "Stripes", RegionMaskStyle.Stripes);
        AddStyleItem(styleMenu, "HIDE label", RegionMaskStyle.Label);
        AddStyleItem(styleMenu, "Stripes + HIDE", RegionMaskStyle.StripesWithLabel);
        menu.Items.Add(styleMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var deleteItem = menu.Items.Add("Delete");
        deleteItem.Click += (_, _) => DeleteMask();
        menu.Opening += (_, _) => UpdateMenuState(menu);
        menu.Closed += (_, _) => _actionTaken = false;
        TopmostContextMenuHelper.Attach(menu, _topmostTimers);

        _menu = menu;
        return menu;
    }

    private void AddStyleItem(Forms.ToolStripMenuItem parent, string text, RegionMaskStyle style)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            CheckOnClick = false,
            Tag = style
        };
        item.Click += (_, _) => SetStyle(style);
        parent.DropDownItems.Add(item);
    }

    private void UpdateMenuState(Forms.ContextMenuStrip menu)
    {
        var currentStyle = _styleProvider(_maskId) ?? RegionMaskStyle.StripesWithLabel;
        foreach (Forms.ToolStripItem item in menu.Items)
        {
            if (item is Forms.ToolStripMenuItem { Text: "Style" } styleMenu)
            {
                foreach (Forms.ToolStripItem child in styleMenu.DropDownItems)
                {
                    if (child is Forms.ToolStripMenuItem styleItem && styleItem.Tag is RegionMaskStyle style)
                    {
                        styleItem.Checked = style == currentStyle;
                    }
                }
            }
        }
    }

    private void SetStyle(RegionMaskStyle style)
    {
        if (_actionTaken)
        {
            return;
        }

        _actionTaken = true;
        _setStyle(_maskId, style);
    }

    private void DeleteMask()
    {
        if (_actionTaken)
        {
            return;
        }

        _actionTaken = true;
        _deleteMask(_maskId);
    }

    public void Dispose()
    {
        TopmostContextMenuHelper.DisposeTimers(_topmostTimers);
        _menu?.Dispose();
        _menu = null;
    }
}
