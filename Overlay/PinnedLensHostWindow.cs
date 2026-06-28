using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FocusTool.Win.Models;
using FocusTool.Win.Native;
using FocusTool.Win.Services;

namespace FocusTool.Win.Overlay;

internal sealed class PinnedLensHostWindow : IDisposable
{
    private const int MinimumSourceSize = 12;
    private const double DefaultMaximumWorkingAreaShare = 0.72;
    internal const int BorderThickness = 1;
    internal const int FreezeAfterMenuDelayMs = 120;
    internal const double ZoomStep = 0.25;

    private readonly ScreenRect _sourceRect;
    private readonly double _maximumZoom;
    private readonly Func<PinnedLensHostWindow, Func<bool>, Task<bool>> _freezeFrameCaptureCoordinator;
    private PinnedLensHostForm? _host;
    private PinnedLensTransparentChildNativeWindow? _magnifierSubclass;
    private MagnificationRuntime? _runtime;
    private IntPtr _hostHwnd;
    private IntPtr _magnifierHwnd;
    private IntPtr[] _filterHandles = [];
    private Bitmap? _frozenFrame;
    private Rectangle _windowBounds;
    private bool _shown;
    private bool _disposed;
    private bool _frozen;
    private double _zoom;
    private double _appliedZoom;

    public PinnedLensHostWindow(
        ScreenRect sourceRect,
        AppSettings settings,
        Action? closeAll,
        Func<PinnedLensHostWindow, Func<bool>, Task<bool>> freezeFrameCaptureCoordinator)
    {
        CloseAllRequested = closeAll;
        _freezeFrameCaptureCoordinator = freezeFrameCaptureCoordinator;
        _sourceRect = NormalizeSourceRect(sourceRect);
        _runtime = MagnificationRuntime.Acquire("pinned lens");
        IsAvailable = _runtime is not null;
        if (_runtime is null)
        {
            return;
        }

        _maximumZoom = CalculateMaximumZoom(_sourceRect);
        _zoom = ClampZoom(settings.PinnedLensZoom);

        try
        {
            _host = new PinnedLensHostForm(this);
            _host.FormClosed += OnHostFormClosed;
            var windowSize = CalculateWindowSize(_sourceRect, _zoom);
            _windowBounds = new Rectangle(CalculateInitialLocation(_sourceRect, windowSize), windowSize);
            _hostHwnd = _host.Handle;
            ApplyHostWindowBounds(_windowBounds, topmost: false);
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            AppLog.Error("Could not create the pinned lens window.", ex);
            return;
        }

        CreateMagnifierChild();
    }

    public event EventHandler? Closed;

    public event EventHandler? FreezeStateChanged;

    public event EventHandler? Selected;

    public Action? CloseAllRequested { get; }

    public bool IsAvailable { get; private set; }

    public ScreenRect SourceRect => _sourceRect;

    public IntPtr Handle => _hostHwnd;

    public bool IsFrozen => _frozen;

    public bool IsSelected { get; private set; }

    public double Zoom => _zoom;

    internal double MaximumZoom => _maximumZoom;

    public bool Contains(ScreenPoint point)
    {
        return _windowBounds.Contains((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }

    public void SetSelected(bool selected)
    {
        if (IsSelected == selected)
        {
            return;
        }

        IsSelected = selected;
        _host?.Invalidate();
    }

    public void Show()
    {
        if (_disposed || !IsAvailable || _host is null)
        {
            return;
        }

        if (!_shown)
        {
            _host.Show();
            _shown = true;
        }

        ReassertTopmost();
    }

    public void UpdateLens(IReadOnlyList<IntPtr> excludedWindows)
    {
        if (_disposed || _frozen || !IsAvailable || _host is null || _hostHwnd == IntPtr.Zero || _magnifierHwnd == IntPtr.Zero)
        {
            return;
        }

        if (!_shown)
        {
            Show();
        }

        ResizeMagnifierChild();

        if (Math.Abs(_appliedZoom - _zoom) > 0.001)
        {
            var transform = NativeMethods.MagTransform.Scale((float)_zoom);
            if (NativeMethods.MagSetWindowTransform(_magnifierHwnd, ref transform))
            {
                _appliedZoom = _zoom;
            }
        }

        UpdateFilterList(excludedWindows);

        var sourceRect = new NativeMethods.Rect(
            (int)Math.Round(_sourceRect.Left),
            (int)Math.Round(_sourceRect.Top),
            (int)Math.Round(_sourceRect.Right),
            (int)Math.Round(_sourceRect.Bottom));

        _ = NativeMethods.MagSetWindowSource(_magnifierHwnd, sourceRect);
    }

    public async Task SetFrozenAsync(bool frozen)
    {
        if (!frozen)
        {
            Resume();
            return;
        }

        if (_frozen)
        {
            return;
        }

        if (await _freezeFrameCaptureCoordinator(this, FreezeFromDesktopCapture))
        {
            FreezeStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Resume()
    {
        if (!_frozen)
        {
            return;
        }

        _frozen = false;
        DisposeFrozenFrame();
        ResizeMagnifierChild();
        SetMagnifierChildVisible(true);
        _appliedZoom = 0;
        _host?.Refresh();
        FreezeStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool HideForDesktopCapture()
    {
        if (_disposed || _host is null || !_shown || !_host.Visible)
        {
            return false;
        }

        _host.Hide();
        return true;
    }

    public void RestoreAfterDesktopCapture()
    {
        if (_disposed || !IsAvailable || _host is null || !_shown)
        {
            return;
        }

        if (!_host.Visible)
        {
            _host.Show();
        }

        ReassertTopmost();
    }

    public void AdjustZoom(double delta)
    {
        SetZoom(_zoom + delta);
    }

    public void SetZoom(double zoom)
    {
        var nextZoom = ClampZoom(zoom);
        if (Math.Abs(nextZoom - _zoom) < 0.001)
        {
            return;
        }

        _zoom = nextZoom;
        _appliedZoom = 0;
        if (_host is not null)
        {
            var nextSize = CalculateWindowSize(_sourceRect, _zoom);
            var nextBounds = new Rectangle(
                _windowBounds.Left + (_windowBounds.Width - nextSize.Width) / 2,
                _windowBounds.Top + (_windowBounds.Height - nextSize.Height) / 2,
                nextSize.Width,
                nextSize.Height);
            ApplyHostWindowBounds(ClampWindowBoundsToNearestWorkingArea(nextBounds), topmost: false);
            ResizeMagnifierChild();
            _host.Refresh();
        }
    }

    public void PaintFrozenFrame(Graphics graphics, Rectangle bounds)
    {
        if (!_frozen || _frozenFrame is null || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var previousInterpolationMode = graphics.InterpolationMode;
        var previousPixelOffsetMode = graphics.PixelOffsetMode;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(_frozenFrame, bounds);
        graphics.InterpolationMode = previousInterpolationMode;
        graphics.PixelOffsetMode = previousPixelOffsetMode;
    }

    public void ReassertTopmost()
    {
        if (_hostHwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            _hostHwnd,
            NativeMethods.HwndTopmost,
            _windowBounds.Left,
            _windowBounds.Top,
            Math.Max(1, _windowBounds.Width),
            Math.Max(1, _windowBounds.Height),
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
    }

    public void ReassertContextMenuTopmost()
    {
        _host?.ReassertContextMenuTopmost();
    }

    // Pull the window back onto the nearest surviving monitor's working area after a
    // display change so it is never stranded off-screen on a removed monitor.
    public void ReconcileToWorkingArea()
    {
        if (_disposed || _hostHwnd == IntPtr.Zero)
        {
            return;
        }

        var clamped = ClampWindowBoundsToNearestWorkingArea(_windowBounds);
        if (clamped != _windowBounds)
        {
            ApplyHostWindowBounds(clamped, topmost: true);
        }
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

        DisposeFrozenFrame();

        if (_host is not null)
        {
            _host.FormClosed -= OnHostFormClosed;
            _host.Dispose();
            _host = null;
        }

        _hostHwnd = IntPtr.Zero;
        _filterHandles = [];
        _runtime?.Dispose();
        _runtime = null;
    }

    private void OnHostFormClosed(object? sender, FormClosedEventArgs e)
    {
        if (!_disposed)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void RequestSelection()
    {
        if (!_disposed)
        {
            Selected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CreateMagnifierChild()
    {
        _magnifierHwnd = NativeMethods.CreateWindowEx(
            0,
            NativeMethods.WcMagnifier,
            "FocusTool Pinned Lens Control",
            NativeMethods.WsChild | NativeMethods.WsVisible,
            BorderThickness,
            BorderThickness,
            Math.Max(1, GetLensClientSize().Width),
            Math.Max(1, GetLensClientSize().Height),
            _hostHwnd,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_magnifierHwnd == IntPtr.Zero)
        {
            IsAvailable = false;
            AppLog.Error(
                "Could not create the pinned lens magnifier control.",
                new Win32Exception(Marshal.GetLastWin32Error()));
            return;
        }

        _magnifierSubclass = new PinnedLensTransparentChildNativeWindow(_magnifierHwnd);
    }

    private void ResizeMagnifierChild()
    {
        var lensSize = GetLensClientSize();
        _ = NativeMethods.SetWindowPos(
            _magnifierHwnd,
            IntPtr.Zero,
            BorderThickness,
            BorderThickness,
            Math.Max(1, lensSize.Width),
            Math.Max(1, lensSize.Height),
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    private bool ApplyHostWindowBounds(Rectangle bounds, bool topmost)
    {
        if (_hostHwnd == IntPtr.Zero || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        var flags = NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder;
        if (!topmost)
        {
            flags |= NativeMethods.SwpNoZOrder;
        }

        if (!NativeMethods.SetWindowPos(
                _hostHwnd,
                topmost ? NativeMethods.HwndTopmost : IntPtr.Zero,
                bounds.Left,
                bounds.Top,
                Math.Max(1, bounds.Width),
                Math.Max(1, bounds.Height),
                flags))
        {
            return false;
        }

        _windowBounds = bounds;
        return true;
    }

    internal void SyncWindowBoundsFromNative(bool clamp)
    {
        if (_hostHwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(_hostHwnd, out var rect))
        {
            return;
        }

        var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (clamp)
        {
            bounds = ClampWindowBoundsToNearestWorkingArea(bounds);
            if (bounds.Left != rect.Left || bounds.Top != rect.Top || bounds.Right != rect.Right || bounds.Bottom != rect.Bottom)
            {
                ApplyHostWindowBounds(bounds, topmost: false);
                return;
            }
        }

        _windowBounds = bounds;
    }

    internal void SetWindowBoundsFromHostMove(Rectangle bounds)
    {
        _windowBounds = bounds;
    }

    private void SetMagnifierChildVisible(bool visible)
    {
        if (_magnifierHwnd == IntPtr.Zero)
        {
            return;
        }

        _ = NativeMethods.SetWindowPos(
            _magnifierHwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove
                | NativeMethods.SwpNoSize
                | NativeMethods.SwpNoZOrder
                | NativeMethods.SwpNoActivate
                | (visible ? NativeMethods.SwpShowWindow : NativeMethods.SwpHideWindow));
    }

    private Size GetLensClientSize()
    {
        return new Size(
            Math.Max(1, _windowBounds.Width - BorderThickness * 2),
            Math.Max(1, _windowBounds.Height - BorderThickness * 2));
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

    private bool CaptureFrozenFrame()
    {
        var lensSize = GetLensClientSize();
        if (lensSize.Width <= 0 || lensSize.Height <= 0)
        {
            return false;
        }

        var sourceSize = GetSourceCaptureSize(_sourceRect);
        if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
        {
            return false;
        }

        Bitmap? frame = null;
        try
        {
            frame = new Bitmap(lensSize.Width, lensSize.Height);
            using var source = new Bitmap(sourceSize.Width, sourceSize.Height);
            using (var sourceGraphics = Graphics.FromImage(source))
            {
                sourceGraphics.Clear(Color.Black);
                var sourceBounds = new Rectangle(
                    (int)Math.Round(_sourceRect.Left),
                    (int)Math.Round(_sourceRect.Top),
                    sourceSize.Width,
                    sourceSize.Height);
                var captureBounds = Rectangle.Intersect(sourceBounds, GetVirtualScreenArea());
                if (!captureBounds.IsEmpty)
                {
                    sourceGraphics.CopyFromScreen(
                        captureBounds.Location,
                        new Point(captureBounds.Left - sourceBounds.Left, captureBounds.Top - sourceBounds.Top),
                        captureBounds.Size);
                }
            }

            using (var frameGraphics = Graphics.FromImage(frame))
            {
                frameGraphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                frameGraphics.PixelOffsetMode = PixelOffsetMode.Half;
                frameGraphics.DrawImage(source, new Rectangle(Point.Empty, lensSize));
            }

            DisposeFrozenFrame();
            _frozenFrame = frame;
            frame = null;
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or ExternalException or ArgumentException)
        {
            AppLog.Error("Could not freeze the pinned lens frame.", ex);
            return false;
        }
        finally
        {
            frame?.Dispose();
        }
    }

    private bool FreezeFromDesktopCapture()
    {
        if (_frozen)
        {
            return true;
        }

        if (!CaptureFrozenFrame())
        {
            return false;
        }

        _frozen = true;
        SetMagnifierChildVisible(false);
        _host?.Refresh();
        return true;
    }

    private void DisposeFrozenFrame()
    {
        _frozenFrame?.Dispose();
        _frozenFrame = null;
    }

    private static ScreenRect NormalizeSourceRect(ScreenRect sourceRect)
    {
        var width = Math.Max(MinimumSourceSize, sourceRect.Width);
        var height = Math.Max(MinimumSourceSize, sourceRect.Height);
        return new ScreenRect(sourceRect.Left, sourceRect.Top, sourceRect.Left + width, sourceRect.Top + height);
    }

    private double ClampZoom(double desiredZoom)
    {
        return Math.Clamp(Math.Min(desiredZoom, _maximumZoom), 1.0, 4.0);
    }

    private static double CalculateMaximumZoom(ScreenRect sourceRect)
    {
        var screen = Screen.FromPoint(new Point(
            (int)Math.Round(sourceRect.Left + sourceRect.Width / 2),
            (int)Math.Round(sourceRect.Top + sourceRect.Height / 2)));
        var maxWidth = Math.Max(240, screen.WorkingArea.Width * DefaultMaximumWorkingAreaShare);
        var maxHeight = Math.Max(160, screen.WorkingArea.Height * DefaultMaximumWorkingAreaShare);
        return Math.Clamp(Math.Min(maxWidth / Math.Max(1, sourceRect.Width), maxHeight / Math.Max(1, sourceRect.Height)), 1.0, 4.0);
    }

    private static Size CalculateWindowSize(ScreenRect sourceRect, double zoom)
    {
        var lensSize = new Size(
            Math.Max(24, (int)Math.Round(sourceRect.Width * zoom)),
            Math.Max(24, (int)Math.Round(sourceRect.Height * zoom)));
        return new Size(
            lensSize.Width + BorderThickness * 2,
            lensSize.Height + BorderThickness * 2);
    }

    private static Point CalculateInitialLocation(ScreenRect sourceRect, Size windowSize)
    {
        const int margin = 14;
        var sourceCenter = new Point(
            (int)Math.Round(sourceRect.Left + sourceRect.Width / 2),
            (int)Math.Round(sourceRect.Top + sourceRect.Height / 2));
        var area = Screen.FromPoint(sourceCenter).WorkingArea;
        var right = (int)Math.Round(sourceRect.Right) + margin;
        var left = (int)Math.Round(sourceRect.Left) - windowSize.Width - margin;
        var top = (int)Math.Round(sourceRect.Top);

        var x = right + windowSize.Width <= area.Right
            ? right
            : left >= area.Left
                ? left
                : area.Left + (area.Width - windowSize.Width) / 2;

        var y = Math.Clamp(top, area.Top + margin, Math.Max(area.Top + margin, area.Bottom - windowSize.Height - margin));
        x = Math.Clamp(x, area.Left + margin, Math.Max(area.Left + margin, area.Right - windowSize.Width - margin));
        return new Point(x, y);
    }

    private static Size GetSourceCaptureSize(ScreenRect sourceRect)
    {
        return new Size(
            Math.Max(1, (int)Math.Round(sourceRect.Width)),
            Math.Max(1, (int)Math.Round(sourceRect.Height)));
    }

    private static Rectangle GetVirtualWorkingArea()
    {
        var screens = Screen.AllScreens;
        if (screens.Length == 0)
        {
            return Screen.PrimaryScreen?.WorkingArea ?? Rectangle.Empty;
        }

        var left = screens.Min(screen => screen.WorkingArea.Left);
        var top = screens.Min(screen => screen.WorkingArea.Top);
        var right = screens.Max(screen => screen.WorkingArea.Right);
        var bottom = screens.Max(screen => screen.WorkingArea.Bottom);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static Rectangle GetVirtualScreenArea()
    {
        var screens = Screen.AllScreens;
        if (screens.Length == 0)
        {
            return Screen.PrimaryScreen?.Bounds ?? Rectangle.Empty;
        }

        var left = screens.Min(screen => screen.Bounds.Left);
        var top = screens.Min(screen => screen.Bounds.Top);
        var right = screens.Max(screen => screen.Bounds.Right);
        var bottom = screens.Max(screen => screen.Bounds.Bottom);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static Rectangle GetNearestWorkingArea(Rectangle bounds)
    {
        var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var nearestScreen = Screen.FromPoint(center);
        if (nearestScreen is not null)
        {
            return nearestScreen.WorkingArea;
        }

        return GetVirtualWorkingArea();
    }

    internal static Rectangle ClampWindowBoundsToNearestWorkingArea(Rectangle bounds)
    {
        const int margin = 1;
        var area = GetNearestWorkingArea(bounds);
        if (area.IsEmpty)
        {
            return bounds;
        }

        var maxLeft = Math.Max(area.Left + margin, area.Right - bounds.Width - margin);
        var maxTop = Math.Max(area.Top + margin, area.Bottom - bounds.Height - margin);
        var left = Math.Clamp(bounds.Left, area.Left + margin, maxLeft);
        var top = Math.Clamp(bounds.Top, area.Top + margin, maxTop);
        return new Rectangle(left, top, bounds.Width, bounds.Height);
    }

}
