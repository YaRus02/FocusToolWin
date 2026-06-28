using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FocusTool.Win.Native;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace FocusTool.Win.Services;

// WPF counterpart to TopmostContextMenuHelper: keeps a WPF ContextMenu (and any
// open submenus) above the topmost overlay windows while it is shown.
internal static class WpfTopmostContextMenuHelper
{
    // Wire a freshly built context menu so it (and its submenus) reassert topmost
    // whenever they open.
    public static void Attach(ContextMenu menu)
    {
        menu.Opened += (_, _) => ReassertSoon(menu);
        AttachSubmenuHandlers(menu.Items, menu);
    }

    // Reassert immediately if the menu is currently open (e.g. after the owning
    // window reasserts its own topmost).
    public static void ReassertIfOpen(ContextMenu? menu)
    {
        if (menu is { IsOpen: true })
        {
            ReassertSoon(menu);
        }
    }

    private static void ReassertSoon(ContextMenu menu)
    {
        Reassert(menu);
        _ = menu.Dispatcher.BeginInvoke(new Action(() => Reassert(menu)), DispatcherPriority.ContextIdle);
    }

    private static void Reassert(ContextMenu menu)
    {
        if (!menu.IsOpen)
        {
            return;
        }

        var handles = new HashSet<IntPtr>();
        AddVisualHandle(menu, handles);
        AddItemHandles(menu.Items, handles);

        foreach (var handle in handles)
        {
            NativeMethods.SetWindowPos(
                handle,
                NativeMethods.HwndTopmost,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
        }
    }

    private static void AttachSubmenuHandlers(ItemCollection items, ContextMenu menu)
    {
        foreach (var item in items)
        {
            if (item is not MenuItem menuItem)
            {
                continue;
            }

            menuItem.SubmenuOpened += (_, _) => ReassertSoon(menu);
            AttachSubmenuHandlers(menuItem.Items, menu);
        }
    }

    private static void AddItemHandles(ItemCollection items, ISet<IntPtr> handles)
    {
        foreach (var item in items)
        {
            if (item is not MenuItem menuItem)
            {
                continue;
            }

            AddVisualHandle(menuItem, handles);
            AddItemHandles(menuItem.Items, handles);
        }
    }

    private static void AddVisualHandle(Visual visual, ISet<IntPtr> handles)
    {
        var handle = (PresentationSource.FromVisual(visual) as HwndSource)?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            handles.Add(handle);
        }
    }
}
