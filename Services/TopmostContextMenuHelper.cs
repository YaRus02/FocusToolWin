using System.Windows.Threading;
using FocusTool.Win.Native;
using Forms = System.Windows.Forms;

namespace FocusTool.Win.Services;

internal static class TopmostContextMenuHelper
{
    public static void Attach(Forms.ContextMenuStrip menu, List<Forms.Timer> timers)
    {
        menu.Opened += (_, _) => ReassertRepeated(menu, timers);
        AttachSubmenuHandlers(menu.Items, timers);
    }

    public static void ReassertIfVisible(Forms.ToolStripDropDown menu)
    {
        if (menu.Visible)
        {
            Reassert(menu);
        }
    }

    public static void ReassertRepeated(Forms.ToolStripDropDown menu, List<Forms.Timer> timers)
    {
        Reassert(menu);
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            () => Reassert(menu),
            DispatcherPriority.Send);
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            () => Reassert(menu),
            DispatcherPriority.ContextIdle);

        timers.RemoveAll(timer =>
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
                timers.Remove(timer);
                timer.Dispose();
                return;
            }

            Reassert(menu);
        };
        timers.Add(timer);
        timer.Start();
    }

    public static void DisposeTimers(List<Forms.Timer> timers)
    {
        foreach (var timer in timers)
        {
            timer.Stop();
            timer.Dispose();
        }

        timers.Clear();
    }

    private static void AttachSubmenuHandlers(Forms.ToolStripItemCollection items, List<Forms.Timer> timers)
    {
        foreach (Forms.ToolStripItem item in items)
        {
            if (item is not Forms.ToolStripMenuItem menuItem)
            {
                continue;
            }

            menuItem.DropDownOpened += (_, _) => ReassertRepeated(menuItem.DropDown, timers);
            AttachSubmenuHandlers(menuItem.DropDownItems, timers);
        }
    }

    private static void Reassert(Forms.ToolStripDropDown menu)
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
}
