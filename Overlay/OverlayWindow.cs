using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusTool.Win.Models;
using FocusTool.Win.Native;
using Screen = System.Windows.Forms.Screen;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace FocusTool.Win.Overlay;

internal sealed class OverlayWindow : Window
{
    private readonly Screen _screen;
    private readonly IOverlayInputHandler _inputHandler;
    private readonly OverlaySurface _surface;
    private HwndSource? _source;
    private bool _annotateInputEnabled;
    private bool _applyingBounds;
    private bool _nativeMouseCaptured;
    private bool _sourceReady;

    public OverlayWindow(
        Screen screen,
        TrailModel trailModel,
        AnnotationDocument annotations,
        Func<AppSettings> settingsProvider,
        Func<InteractionMode> modeProvider,
        Func<double> clockProvider,
        Func<ScreenPoint?> spotlightProvider,
        Func<CursorHighlightFrame> cursorHighlightProvider,
        Func<ScreenBoardFrame?> screenBoardProvider,
        Func<RectOverlayVisual?> rectOverlayProvider,
        Func<IReadOnlyList<RegionMask>> regionMaskProvider,
        Func<IReadOnlyList<ScreenRect>> spotlightRegionProvider,
        Func<int> spotlightRegionSelectionProvider,
        IOverlayInputHandler inputHandler)
    {
        _screen = screen;
        _inputHandler = inputHandler;
        var bounds = screen.Bounds;
        _surface = new OverlaySurface(
            trailModel,
            annotations,
            settingsProvider,
            modeProvider,
            clockProvider,
            spotlightProvider,
            cursorHighlightProvider,
            screenBoardProvider,
            rectOverlayProvider,
            regionMaskProvider,
            spotlightRegionProvider,
            spotlightRegionSelectionProvider,
            new ScreenRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));

        Title = "FocusTool";
        Content = _surface;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;

        Left = _screen.Bounds.Left;
        Top = _screen.Bounds.Top;
        Width = _screen.Bounds.Width;
        Height = _screen.Bounds.Height;

        PreviewKeyDown += OnPreviewKeyDown;
        TextInput += OnTextInput;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _sourceReady = true;
        _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _source?.AddHook(WndProc);
        SetInteractionMode(_inputHandler.Mode);
        PositionOverScreen();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        PositionOverScreen();

        // WPF can rebuild the layered surface on a DPI change and drop the manually
        // applied click-through styles, which would silently make an annotation
        // overlay click-through (or vice versa). Re-assert them for the current mode.
        if (_sourceReady)
        {
            ApplyWindowStyles(_annotateInputEnabled);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_nativeMouseCaptured)
        {
            _nativeMouseCaptured = false;
            NativeMethods.ReleaseCapture();
            _inputHandler.HandleOverlayCaptureLost();
        }

        _source?.RemoveHook(WndProc);
        _source = null;
        _surface.Detach();
        base.OnClosed(e);
    }

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public bool Contains(ScreenPoint point)
    {
        var bounds = _screen.Bounds;
        return point.X >= bounds.Left
            && point.X < bounds.Right
            && point.Y >= bounds.Top
            && point.Y < bounds.Bottom;
    }

    public bool Intersects(ScreenRect rect)
    {
        var bounds = _screen.Bounds;
        return new ScreenRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom).Intersects(rect);
    }

    public double DistanceSquaredTo(ScreenPoint point)
    {
        var bounds = _screen.Bounds;
        var clampedX = Math.Clamp(point.X, bounds.Left, bounds.Right);
        var clampedY = Math.Clamp(point.Y, bounds.Top, bounds.Bottom);
        var dx = point.X - clampedX;
        var dy = point.Y - clampedY;
        return dx * dx + dy * dy;
    }

    public double DpiScaleX => VisualTreeHelper.GetDpi(this).DpiScaleX;

    public void PositionOverScreen()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var bounds = _screen.Bounds;
        _applyingBounds = true;
        try
        {
            var flags = NativeMethods.SwpShowWindow;
            if (!_annotateInputEnabled)
            {
                flags |= NativeMethods.SwpNoActivate;
            }

            NativeMethods.SetWindowPos(
                handle,
                NativeMethods.HwndTopmost,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                flags | NativeMethods.SwpNoOwnerZOrder);
        }
        finally
        {
            _applyingBounds = false;
        }
    }

    public void ReassertTopmost()
    {
        var handle = new WindowInteropHelper(this).Handle;
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

    public void SetInteractionMode(InteractionMode mode)
    {
        _annotateInputEnabled = IsOverlayInputMode(mode);
        if (!_annotateInputEnabled && _nativeMouseCaptured)
        {
            _nativeMouseCaptured = false;
            NativeMethods.ReleaseCapture();
        }

        Focusable = _annotateInputEnabled;
        IsHitTestVisible = _annotateInputEnabled;
        Cursor = _annotateInputEnabled ? WpfCursors.Cross : WpfCursors.Arrow;
        _surface.SetAnnotationInputEnabled(_annotateInputEnabled);

        if (_sourceReady)
        {
            ApplyWindowStyles(_annotateInputEnabled);
            if (_annotateInputEnabled)
            {
                Activate();
                Focus();
                _surface.Focus();
            }
        }

        _surface.InvalidateVisual();
    }

    public void Refresh()
    {
        _surface.InvalidateVisual();
    }

    public BitmapSource? CaptureSurface()
    {
        if (_surface.ActualWidth <= 1 || _surface.ActualHeight <= 1)
        {
            return null;
        }

        _surface.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(_surface);
        var pixelWidth = Math.Max(1, (int)Math.Round(_surface.ActualWidth * dpi.DpiScaleX));
        var pixelHeight = Math.Max(1, (int)Math.Round(_surface.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        bitmap.Render(_surface);
        bitmap.Freeze();
        return bitmap;
    }

    private void ApplyWindowStyles(bool annotate)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();

        style |= NativeMethods.WsExLayered;
        style |= NativeMethods.WsExToolWindow;
        style &= ~NativeMethods.WsExAppWindow;

        if (annotate)
        {
            style &= ~NativeMethods.WsExTransparent;
            style &= ~NativeMethods.WsExNoActivate;
        }
        else
        {
            style |= NativeMethods.WsExTransparent;
            style |= NativeMethods.WsExNoActivate;
        }

        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, new IntPtr(style));
        NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpFrameChanged | NativeMethods.SwpNoActivate);
    }

    private static bool IsOverlayInputMode(InteractionMode mode)
    {
        return mode is InteractionMode.Annotate
            or InteractionMode.PinnedLensSelect
            or InteractionMode.RegionMaskSelect
            or InteractionMode.ScreenshotRegionSelect
            or InteractionMode.RegionSpotlightSelect
            or InteractionMode.ScreenBoard
            or InteractionMode.BlackScreen
            or InteractionMode.WhiteScreen;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionOverScreen();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_applyingBounds || !_sourceReady)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var expectedWidth = _screen.Bounds.Width / Math.Max(0.1, dpi.DpiScaleX);
        var expectedHeight = _screen.Bounds.Height / Math.Max(0.1, dpi.DpiScaleY);

        if (e.NewSize.Width < expectedWidth * 0.5 || e.NewSize.Height < expectedHeight * 0.5)
        {
            PositionOverScreen();
        }
    }

    private void OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (!_annotateInputEnabled)
        {
            return;
        }

        var key = e.Key == Key.System
            ? e.SystemKey
            : e.Key == Key.ImeProcessed
                ? e.ImeProcessedKey
                : e.Key;

        e.Handled = _inputHandler.HandleOverlayKeyDown(key, Keyboard.Modifiers);
    }

    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!_annotateInputEnabled)
        {
            return;
        }

        _inputHandler.HandleOverlayTextInput(e.Text);
        e.Handled = true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_annotateInputEnabled)
        {
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case NativeMethods.WmLButtonDown:
                _nativeMouseCaptured = true;
                NativeMethods.SetCapture(hwnd);
                _inputHandler.HandleOverlayMouseDown(ToScreenPoint(hwnd, lParam), MouseButton.Left, Keyboard.Modifiers);
                handled = true;
                break;
            case NativeMethods.WmMouseMove:
                if (_nativeMouseCaptured)
                {
                    _inputHandler.HandleOverlayMouseMove(ToScreenPoint(hwnd, lParam), Keyboard.Modifiers);
                    handled = true;
                }

                break;
            case NativeMethods.WmLButtonUp:
                if (_nativeMouseCaptured)
                {
                    _nativeMouseCaptured = false;
                    NativeMethods.ReleaseCapture();
                    _inputHandler.HandleOverlayMouseUp(ToScreenPoint(hwnd, lParam), MouseButton.Left, Keyboard.Modifiers);
                    handled = true;
                }

                break;
            case NativeMethods.WmRButtonDown:
                _inputHandler.HandleOverlayMouseDown(ToScreenPoint(hwnd, lParam), MouseButton.Right, Keyboard.Modifiers);
                handled = true;
                break;
            case NativeMethods.WmRButtonUp:
                _inputHandler.HandleOverlayMouseUp(ToScreenPoint(hwnd, lParam), MouseButton.Right, Keyboard.Modifiers);
                handled = true;
                break;
            case NativeMethods.WmCancelMode:
            case NativeMethods.WmCaptureChanged:
                if (_nativeMouseCaptured
                    && (msg != NativeMethods.WmCaptureChanged || lParam != hwnd))
                {
                    _nativeMouseCaptured = false;
                    _inputHandler.HandleOverlayCaptureLost();
                    handled = true;
                }

                break;
        }

        return IntPtr.Zero;
    }

    private static ScreenPoint ToScreenPoint(IntPtr hwnd, IntPtr lParam)
    {
        var raw = lParam.ToInt64();
        var point = new NativeMethods.Point
        {
            X = unchecked((short)(raw & 0xFFFF)),
            Y = unchecked((short)((raw >> 16) & 0xFFFF))
        };

        NativeMethods.ClientToScreen(hwnd, ref point);
        return new ScreenPoint(point.X, point.Y);
    }
}
