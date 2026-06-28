using System.Windows.Forms;
using FocusTool.Win.Native;

namespace FocusTool.Win.Overlay;

internal sealed class PinnedLensTransparentChildNativeWindow : NativeWindow
{
    public PinnedLensTransparentChildNativeWindow(IntPtr handle)
    {
        AssignHandle(handle);
    }

    public void Release()
    {
        ReleaseHandle();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WmNcHitTest)
        {
            m.Result = NativeMethods.HtTransparent;
            return;
        }

        base.WndProc(ref m);
    }
}
