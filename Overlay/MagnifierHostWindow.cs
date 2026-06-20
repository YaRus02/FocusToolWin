using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FocusTool.Win.Models;
using FocusTool.Win.Native;
using FocusTool.Win.Services;

namespace FocusTool.Win.Overlay;

/// <summary>
/// Hosts the Windows Magnification API lens in a plain Win32 (WinForms) window.
/// A real layered + transparent top-level window lets the mouse fall straight
/// through to the desktop, so clicks and hover work in place with no hide hack -
/// unlike a WPF host, whose input layer blocked the pass-through entirely.
/// </summary>
internal sealed class MagnifierHostWindow : IDisposable
{
    private static readonly object RuntimeLock = new();
    private static int _runtimeReferences;

    private HostForm? _host;
    private ClickThroughNativeWindow? _magnifierSubclass;
    private IntPtr _hostHwnd;
    private IntPtr _magnifierHwnd;
    private bool _runtimeAcquired;
    private bool _disposed;
    private bool _shown;
    private int _diameter;
    private int _left = int.MinValue;
    private int _top = int.MinValue;
    private double _zoom;
    private IntPtr[] _filterHandles = [];

    public MagnifierHostWindow()
    {
        IsAvailable = AcquireRuntime();
        if (!IsAvailable)
        {
            AppLog.Error(
                "Could not initialize Windows Magnification API.",
                new Win32Exception(Marshal.GetLastWin32Error()));
            return;
        }

        try
        {
            _host = new HostForm();
            _hostHwnd = _host.Handle; // forces handle creation without showing
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            AppLog.Error("Could not create the magnifier host window.", ex);
            return;
        }

        ApplyHostWindowStyles();
        CreateMagnifierChild();
    }

    public bool IsAvailable { get; private set; }

    public void UpdateLens(ScreenPoint cursor, AppSettings settings, double dpiScale, IReadOnlyList<IntPtr> excludedWindows)
    {
        if (_disposed || !IsAvailable || _hostHwnd == IntPtr.Zero || _magnifierHwnd == IntPtr.Zero)
        {
            return;
        }

        var scale = Math.Max(0.1, dpiScale);
        var radius = Math.Max(1, (int)Math.Round(settings.MagnifierRadius * scale));
        var diameter = radius * 2;
        var left = (int)Math.Round(cursor.X - radius);
        var top = (int)Math.Round(cursor.Y - radius);
        var zoom = Math.Clamp(settings.MagnifierZoom, 1.25, 4.0);

        if (!_shown && _host is not null)
        {
            _host.Visible = true; // shown without activation (see HostForm)
            _shown = true;
        }

        if (left != _left || top != _top || diameter != _diameter)
        {
            if (NativeMethods.SetWindowPos(
                _hostHwnd,
                IntPtr.Zero,
                left,
                top,
                diameter,
                diameter,
                NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder))
            {
                _left = left;
                _top = top;
            }
        }

        if (diameter != _diameter)
        {
            if (ResizeMagnifierChild(diameter)
                && ApplyCircularRegion(diameter))
            {
                _diameter = diameter;
            }
        }

        if (Math.Abs(_zoom - zoom) > 0.001)
        {
            var transform = NativeMethods.MagTransform.Scale((float)zoom);
            if (NativeMethods.MagSetWindowTransform(_magnifierHwnd, ref transform))
            {
                _zoom = zoom;
            }
        }

        UpdateFilterList(excludedWindows);

        var sourceSize = Math.Max(1, (int)Math.Round(diameter / zoom));
        var sourceLeft = (int)Math.Round(cursor.X - sourceSize / 2.0);
        var sourceTop = (int)Math.Round(cursor.Y - sourceSize / 2.0);
        var sourceRect = new NativeMethods.Rect(sourceLeft, sourceTop, sourceLeft + sourceSize, sourceTop + sourceSize);
        // Re-submit the source every composition frame. The Magnification API does
        // not reliably repaint stationary sources for all dynamic surfaces (video,
        // hardware-accelerated canvases), even though their pixels keep changing.
        _ = NativeMethods.MagSetWindowSource(_magnifierHwnd, sourceRect);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _magnifierSubclass?.Release();
        _magnifierSubclass = null;

        if (_magnifierHwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_magnifierHwnd);
            _magnifierHwnd = IntPtr.Zero;
        }

        _host?.Dispose();
        _host = null;
        _hostHwnd = IntPtr.Zero;
        _filterHandles = [];
        ReleaseRuntime();
    }

    private void ApplyHostWindowStyles()
    {
        if (_hostHwnd == IntPtr.Zero)
        {
            return;
        }

        // Extended styles also come from HostForm.CreateParams; re-asserting them
        // here is harmless. A layered window needs a layered-attributes call before
        // it will paint, and we want it topmost and click-through.
        var style = NativeMethods.GetWindowLongPtr(_hostHwnd, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExLayered;
        style |= NativeMethods.WsExTransparent;
        style |= NativeMethods.WsExNoActivate;
        style |= NativeMethods.WsExToolWindow;
        style &= ~NativeMethods.WsExAppWindow;

        NativeMethods.SetWindowLongPtr(_hostHwnd, NativeMethods.GwlExStyle, new IntPtr(style));
        NativeMethods.SetLayeredWindowAttributes(_hostHwnd, 0, 255, NativeMethods.LwaAlpha);
        NativeMethods.SetWindowPos(
            _hostHwnd,
            NativeMethods.HwndTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);
    }

    private void CreateMagnifierChild()
    {
        _magnifierHwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate,
            NativeMethods.WcMagnifier,
            "FocusTool Magnifier Control",
            NativeMethods.WsChild | NativeMethods.WsVisible,
            0,
            0,
            1,
            1,
            _hostHwnd,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_magnifierHwnd == IntPtr.Zero)
        {
            IsAvailable = false;
            AppLog.Error(
                "Could not create the Windows Magnifier control.",
                new Win32Exception(Marshal.GetLastWin32Error()));
            return;
        }

        _magnifierSubclass = new ClickThroughNativeWindow(_magnifierHwnd);
    }

    private bool ResizeMagnifierChild(int diameter)
    {
        return NativeMethods.SetWindowPos(
            _magnifierHwnd,
            IntPtr.Zero,
            0,
            0,
            diameter,
            diameter,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    private bool ApplyCircularRegion(int diameter)
    {
        if (_hostHwnd == IntPtr.Zero)
        {
            return false;
        }

        var region = NativeMethods.CreateEllipticRgn(0, 0, diameter + 1, diameter + 1);
        if (region == IntPtr.Zero)
        {
            return false;
        }

        if (NativeMethods.SetWindowRgn(_hostHwnd, region, true) == 0)
        {
            NativeMethods.DeleteObject(region);
            return false;
        }

        return true;
    }

    private void UpdateFilterList(IReadOnlyList<IntPtr> excludedWindows)
    {
        if (_magnifierHwnd == IntPtr.Zero)
        {
            return;
        }

        var handles = new List<IntPtr>(excludedWindows.Count + 2);
        foreach (var handle in excludedWindows)
        {
            if (handle != IntPtr.Zero && handle != _magnifierHwnd && !handles.Contains(handle))
            {
                handles.Add(handle);
            }
        }

        if (_hostHwnd != IntPtr.Zero && !handles.Contains(_hostHwnd))
        {
            handles.Add(_hostHwnd);
        }

        var nextHandles = handles.ToArray();
        if (nextHandles.SequenceEqual(_filterHandles))
        {
            return;
        }

        if (NativeMethods.MagSetWindowFilterList(
                _magnifierHwnd,
                NativeMethods.MwFilterModeExclude,
                nextHandles.Length,
                nextHandles))
        {
            _filterHandles = nextHandles;
        }
    }

    private bool AcquireRuntime()
    {
        lock (RuntimeLock)
        {
            if (_runtimeReferences == 0 && !NativeMethods.MagInitialize())
            {
                return false;
            }

            _runtimeReferences++;
            _runtimeAcquired = true;
            return true;
        }
    }

    private void ReleaseRuntime()
    {
        if (!_runtimeAcquired)
        {
            return;
        }

        lock (RuntimeLock)
        {
            if (_runtimeReferences > 0)
            {
                _runtimeReferences--;
                if (_runtimeReferences == 0)
                {
                    NativeMethods.MagUninitialize();
                }
            }
        }

        _runtimeAcquired = false;
    }

    /// <summary>
    /// Plain Win32 top-level window (no border, no activation, layered + transparent
    /// so the mouse passes through). Position/size are driven manually via SetWindowPos.
    /// </summary>
    private sealed class HostForm : Form
    {
        public HostForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            SetStyle(ControlStyles.Opaque, true);
            Bounds = new Rectangle(-32000, -32000, 1, 1);
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The magnifier child fills the window; skipping the GDI background
            // erase removes the flicker that showed up while the lens moves.
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WsExLayered
                    | NativeMethods.WsExTransparent
                    | NativeMethods.WsExNoActivate
                    | NativeMethods.WsExToolWindow;
                return cp;
            }
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

    private sealed class ClickThroughNativeWindow : NativeWindow
    {
        public ClickThroughNativeWindow(IntPtr handle)
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
}
