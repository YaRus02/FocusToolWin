using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FocusTool.Win.Native;
using WpfToolTip = System.Windows.Controls.ToolTip;

namespace FocusTool.Win.Services;

// Keeps toolbar tooltips above the topmost overlay windows. WPF renders each
// ToolTip in a separate popup HWND, so raising the toolbar can otherwise place
// an already open tooltip behind it.
internal static class WpfTopmostToolTipHelper
{
    private static readonly HashSet<WpfToolTip> OpenToolTips = [];

    public static void Attach(WpfToolTip toolTip)
    {
        toolTip.Opened += OnOpened;
        toolTip.Closed += OnClosed;
    }

    public static void ReassertOpen()
    {
        foreach (var toolTip in OpenToolTips)
        {
            Reassert(toolTip);
        }
    }

    private static void OnOpened(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not WpfToolTip toolTip)
        {
            return;
        }

        OpenToolTips.Add(toolTip);
        Reassert(toolTip);
        _ = toolTip.Dispatcher.BeginInvoke(
            new Action(() => Reassert(toolTip)),
            DispatcherPriority.ContextIdle);
    }

    private static void OnClosed(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is WpfToolTip toolTip)
        {
            OpenToolTips.Remove(toolTip);
        }
    }

    private static void Reassert(WpfToolTip toolTip)
    {
        if (!toolTip.IsOpen)
        {
            return;
        }

        var handle = (PresentationSource.FromVisual(toolTip) as HwndSource)?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

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
