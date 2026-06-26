using System.Windows.Threading;
using FocusTool.Win.Models;
using FocusTool.Win.Native;
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
        ReassertToolStripDropDownTopmostRepeated(menu);
    }

    public void ReassertTopmostIfVisible()
    {
        if (_menu is { Visible: true } menu)
        {
            ReassertToolStripDropDownTopmost(menu);
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
        styleMenu.DropDownOpened += (_, _) =>
        {
            ReassertToolStripDropDownTopmostRepeated(styleMenu.DropDown);
        };
        menu.Items.Add(styleMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var deleteItem = menu.Items.Add("Delete mask");
        deleteItem.Click += (_, _) => DeleteMask();
        menu.Opening += (_, _) => UpdateMenuState(menu);
        menu.Opened += (_, _) => ReassertToolStripDropDownTopmostRepeated(menu);
        menu.Closed += (_, _) => _actionTaken = false;

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

    private void ReassertToolStripDropDownTopmostRepeated(Forms.ToolStripDropDown menu)
    {
        ReassertToolStripDropDownTopmost(menu);
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            () => ReassertToolStripDropDownTopmost(menu),
            DispatcherPriority.Send);
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            () => ReassertToolStripDropDownTopmost(menu),
            DispatcherPriority.ContextIdle);

        _topmostTimers.RemoveAll(timer =>
        {
            if (timer.Enabled)
            {
                return false;
            }

            timer.Dispose();
            return true;
        });

        var timer = new Forms.Timer
        {
            Interval = 16
        };
        timer.Tick += (_, _) =>
        {
            if (menu.IsDisposed || !menu.Visible)
            {
                timer.Stop();
                _topmostTimers.Remove(timer);
                timer.Dispose();
                return;
            }

            ReassertToolStripDropDownTopmost(menu);
        };
        _topmostTimers.Add(timer);
        timer.Start();
    }

    private static void ReassertToolStripDropDownTopmost(Forms.ToolStripDropDown menu)
    {
        if (menu.IsDisposed || menu.Disposing)
        {
            return;
        }

        if (menu.Handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            menu.Handle,
            NativeMethods.HwndTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
    }

    public void Dispose()
    {
        _menu?.Dispose();
        _menu = null;
        foreach (var timer in _topmostTimers)
        {
            timer.Stop();
            timer.Dispose();
        }

        _topmostTimers.Clear();
    }
}
