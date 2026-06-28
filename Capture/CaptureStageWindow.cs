using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics;
using FocusTool.Win.Native;
using FocusTool.Win.Overlay;
using FocusTool.Win.Services;
using DrawingSize = System.Drawing.Size;
using Form = System.Windows.Forms.Form;
using FormClosedEventArgs = System.Windows.Forms.FormClosedEventArgs;
using Screen = System.Windows.Forms.Screen;

namespace FocusTool.Win.Capture;

/// <summary>
/// A borderless stage window that mirrors a captured source window. Because it
/// is an ordinary top-level window with real swap-chain pixels, screen-share and
/// recording tools (OBS, Zoom, Discord, Teams) can grab it via "Share window"
/// while it carries the source content plus FocusTool overlay snapshots.
/// </summary>
internal sealed class CaptureStageWindow : Form
{
    private const Format SwapFormat = Format.B8G8R8A8_UNorm;
    private const int BufferCount = 2;

    private readonly object _gate = new();
    private readonly IntPtr _sourceWindow;
    private readonly string _sourceTitle;
    private readonly System.Windows.Forms.Label _statusLabel;
    private bool _deviceLost;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGISwapChain1? _swapChain;
    private ID3D11Texture2D? _backBuffer;
    private ID3D11Texture2D? _sourceFrame;
    private ID2D1Factory1? _d2dFactory;
    private ID2D1Device? _d2dDevice;
    private ID2D1DeviceContext? _d2dContext;
    private ID2D1Bitmap1? _d2dTarget;
    private ID2D1Bitmap? _overlayBitmap;
    private readonly List<SpriteBitmap> _spriteBitmaps = [];
    private OverlaySnapshotData? _pendingOverlay;
    private WindowCaptureSession? _session;
    private SizeInt32 _swapSize;
    private bool _graphicsDisposed;
    private bool _placedOnShow;
    private bool _sourceAvailable = true;
    private bool _sourceUnavailableQueued;

    public CaptureStageWindow(IntPtr sourceWindow, string sourceTitle)
    {
        _sourceWindow = sourceWindow;
        _sourceTitle = string.IsNullOrWhiteSpace(sourceTitle) ? "Window" : sourceTitle.Trim();
        Text = $"FocusTool Capture Stage - {_sourceTitle}";
        BackColor = System.Drawing.Color.Black;
        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
        ShowInTaskbar = true;
        // Non-minimizable: a minimized window stops rendering, so the capture would freeze.
        MinimizeBox = false;
        MaximizeBox = false;
        MinimumSize = new DrawingSize(160, 120);
        ClientSize = new DrawingSize(640, 360);
        SetStyle(System.Windows.Forms.ControlStyles.AllPaintingInWmPaint | System.Windows.Forms.ControlStyles.Opaque, true);

        _statusLabel = new System.Windows.Forms.Label
        {
            Dock = System.Windows.Forms.DockStyle.Fill,
            BackColor = System.Drawing.Color.Black,
            ForeColor = System.Drawing.Color.White,
            Font = new System.Drawing.Font(Font.FontFamily, Math.Max(12f, Font.Size + 2f), System.Drawing.FontStyle.Regular),
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            Visible = false,
        };
        Controls.Add(_statusLabel);
    }

    public bool SourceAvailable => _sourceAvailable;

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            InitializeGraphics();
        }
        catch (Exception ex)
        {
            AppLog.Error("Capture Stage could not start.", ex);
            BeginInvoke(Close);
        }
    }

    protected override void WndProc(ref System.Windows.Forms.Message m)
    {
        // Swallow minimize requests (the button is already gone; this also blocks
        // the taskbar button, Win+M, and the system menu's Minimize).
        const int WmSysCommand = 0x0112;
        const int ScMinimize = 0xF020;
        if (m.Msg == WmSysCommand && (m.WParam.ToInt32() & 0xFFF0) == ScMinimize)
        {
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_placedOnShow)
        {
            return;
        }

        _placedOnShow = true;
        WindowState = System.Windows.Forms.FormWindowState.Normal;
        Bounds = Screen.FromControl(this).WorkingArea;
    }

    private void InitializeGraphics()
    {
        _device = CreateDevice();
        _context = _device.ImmediateContext;
        CreateD2DDevice();
        _session = new WindowCaptureSession(_device, _sourceWindow, captureCursor: false);

        var size = _session.SourceSize;
        var width = Math.Max(1, size.Width);
        var height = Math.Max(1, size.Height);
        ApplyInitialClientSize(width, height);
        CreateSwapChain(width, height);

        _session.FrameArrived += OnFrameArrived;
        _session.SourceClosed += OnSourceClosed;
        _session.Start();
    }

    private static ID3D11Device CreateDevice()
    {
        var flags = DeviceCreationFlags.BgraSupport;
        var result = D3D11.D3D11CreateDevice(null, DriverType.Hardware, flags, null, out ID3D11Device? device);
        if (result.Failure)
        {
            result = D3D11.D3D11CreateDevice(null, DriverType.Warp, flags, null, out device);
            result.CheckError();
        }

        return device ?? throw new InvalidOperationException("Direct3D11 device creation returned no device.");
    }

    private void CreateSwapChain(int width, int height)
    {
        using var dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        using var factory = adapter.GetParent<IDXGIFactory2>();

        var description = new SwapChainDescription1
        {
            Width = (uint)width,
            Height = (uint)height,
            Format = SwapFormat,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = BufferCount,
            Scaling = Scaling.Stretch,
            // Blt model (not flip): present copies the back buffer into the window's
            // redirection surface, which is what window-capture tools (OBS BitBlt,
            // Zoom, Discord, Teams) actually read. Flip model bypasses it and reads
            // as blank/white.
            SwapEffect = SwapEffect.Discard,
            AlphaMode = Vortice.DXGI.AlphaMode.Unspecified,
            Flags = SwapChainFlags.None,
        };

        _swapChain = factory.CreateSwapChainForHwnd(_device!, Handle, description);
        factory.MakeWindowAssociation(Handle, WindowAssociationFlags.IgnoreAltEnter);
        _backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _swapSize = new SizeInt32 { Width = width, Height = height };
        BindBackBufferTarget();
    }

    private void CreateD2DDevice()
    {
        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.MultiThreaded);
        using var dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
        _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
        _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
    }

    // Wraps the current back buffer as a Direct2D render target so overlays can be
    // composited on top of the copied source frame before Present.
    private void BindBackBufferTarget()
    {
        _d2dTarget?.Dispose();
        _d2dTarget = null;
        if (_d2dContext is null || _backBuffer is null)
        {
            return;
        }

        using var surface = _backBuffer.QueryInterface<IDXGISurface>();
        var properties = new BitmapProperties1(
            new Vortice.DCommon.PixelFormat(SwapFormat, Vortice.DCommon.AlphaMode.Ignore),
            96f,
            96f,
            BitmapOptions.Target | BitmapOptions.CannotDraw);
        _d2dTarget = _d2dContext.CreateBitmapFromDxgiSurface(surface, properties);
        _d2dContext.Target = _d2dTarget;
    }

    private void ApplyInitialClientSize(int sourceWidth, int sourceHeight)
    {
        var working = Screen.FromControl(this).WorkingArea;
        var maxWidth = Math.Max(320, (int)(working.Width * 0.9));
        var maxHeight = Math.Max(240, (int)(working.Height * 0.9));
        var scale = Math.Min(1.0, Math.Min(maxWidth / (double)sourceWidth, maxHeight / (double)sourceHeight));
        var clientWidth = Math.Max(MinimumSize.Width, (int)Math.Round(sourceWidth * scale));
        var clientHeight = Math.Max(MinimumSize.Height, (int)Math.Round(sourceHeight * scale));
        ClientSize = new DrawingSize(clientWidth, clientHeight);
    }

    private void OnFrameArrived(ID3D11Texture2D texture, SizeInt32 contentSize)
    {
        lock (_gate)
        {
            if (!_sourceAvailable || _graphicsDisposed || _swapChain is null || _backBuffer is null || _context is null)
            {
                return;
            }

            var description = texture.Description;
            var textureWidth = (int)description.Width;
            var textureHeight = (int)description.Height;
            if (textureWidth != _swapSize.Width || textureHeight != _swapSize.Height)
            {
                ResizeSwapChain(textureWidth, textureHeight);
            }

            if (textureWidth != _swapSize.Width || textureHeight != _swapSize.Height)
            {
                return;
            }

            EnsureSourceFrame(textureWidth, textureHeight);
            if (_sourceFrame is null)
            {
                return;
            }

            _context.CopyResource(_sourceFrame, texture);
            RenderCurrentFrame(syncInterval: 1);
        }
    }

    private void EnsureSourceFrame(int width, int height)
    {
        if (_device is null)
        {
            return;
        }

        if (_sourceFrame is not null)
        {
            var current = _sourceFrame.Description;
            if (current.Width == width && current.Height == height)
            {
                return;
            }

            _sourceFrame.Dispose();
            _sourceFrame = null;
        }

        _sourceFrame = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = SwapFormat,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        });
    }

    // syncInterval: 1 for WGC frames (vsync-paced on the pool thread); 0 for the
    // UI-timer re-present so it never blocks the UI thread waiting for vblank.
    private void RenderCurrentFrame(int syncInterval)
    {
        if (!_sourceAvailable || _deviceLost || _swapChain is null || _backBuffer is null || _sourceFrame is null || _context is null)
        {
            return;
        }

        try
        {
            _context.CopyResource(_backBuffer, _sourceFrame);
            DrawOverlay();
            var result = _swapChain.Present((uint)syncInterval, PresentFlags.None);
            if (result.Failure)
            {
                HandleRenderFailure(new InvalidOperationException($"Present failed: 0x{result.Code:X8}"));
            }
        }
        catch (Exception ex)
        {
            HandleRenderFailure(ex);
        }
    }

    // Device loss (TDR / GPU reset / device-removed) or any render error: stop
    // rendering and close the stage rather than sit frozen on a dead device.
    private void HandleRenderFailure(Exception ex)
    {
        if (_deviceLost)
        {
            return;
        }

        _deviceLost = true;
        AppLog.Error("Capture Stage render failed; closing stage.", ex);
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(Close);
        }
    }

    // Composites the latest overlay snapshot over the copied source frame. The
    // overlay snapshot is already cropped to the source window's screen rect, so
    // it maps directly onto the stage back buffer. Called while holding _gate.
    private void DrawOverlay()
    {
        if (_d2dContext is null || _d2dTarget is null)
        {
            return;
        }

        if (_pendingOverlay is { } pending)
        {
            _pendingOverlay = null;
            RefreshOverlay(pending);
        }

        if (!TryGetSourceContentRect(out var left, out var top, out var width, out var height))
        {
            return;
        }

        var destination = new Vortice.Mathematics.Rect(0, 0, _swapSize.Width, _swapSize.Height);

        _d2dContext.BeginDraw();

        if (_overlayBitmap is not null)
        {
            var overlaySize = _overlayBitmap.Size;
            _d2dContext.DrawBitmap(
                _overlayBitmap,
                destination,
                1f,
                BitmapInterpolationMode.Linear,
                new Vortice.Mathematics.Rect(0, 0, overlaySize.Width, overlaySize.Height));
        }

        foreach (var sprite in _spriteBitmaps)
        {
            var spriteRect = new Vortice.Mathematics.Rect(
                (float)((sprite.Left - left) / width * _swapSize.Width),
                (float)((sprite.Top - top) / height * _swapSize.Height),
                (float)(sprite.Width / width * _swapSize.Width),
                (float)(sprite.Height / height * _swapSize.Height));
            var spriteSize = sprite.Bitmap.Size;
            _d2dContext.DrawBitmap(sprite.Bitmap, spriteRect, 1f, BitmapInterpolationMode.Linear, new Vortice.Mathematics.Rect(0, 0, spriteSize.Width, spriteSize.Height));
        }

        _d2dContext.EndDraw();
    }

    private void RefreshOverlay(OverlaySnapshotData data)
    {
        _overlayBitmap?.Dispose();
        _overlayBitmap = null;
        foreach (var sprite in _spriteBitmaps)
        {
            sprite.Bitmap.Dispose();
        }

        _spriteBitmaps.Clear();

        if (_d2dContext is null)
        {
            return;
        }

        if (data.Surface is { } surface)
        {
            _overlayBitmap = CreateBitmap(surface.Pixels, surface.Width, surface.Height, surface.Stride);
        }

        foreach (var sprite in data.Sprites)
        {
            if (CreateBitmap(sprite.Pixels, sprite.Width, sprite.Height, sprite.Stride) is { } bitmap)
            {
                _spriteBitmaps.Add(new SpriteBitmap(bitmap, sprite.ScreenLeft, sprite.ScreenTop, sprite.ScreenWidth, sprite.ScreenHeight));
            }
        }
    }

    private ID2D1Bitmap? CreateBitmap(byte[] pixels, int width, int height, int stride)
    {
        if (_d2dContext is null)
        {
            return null;
        }

        var properties = new BitmapProperties1(
            new Vortice.DCommon.PixelFormat(SwapFormat, Vortice.DCommon.AlphaMode.Premultiplied),
            96f,
            96f,
            BitmapOptions.None);
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            return _d2dContext.CreateBitmap(
                new Vortice.Mathematics.SizeI(width, height),
                handle.AddrOfPinnedObject(),
                (uint)stride,
                properties);
        }
        finally
        {
            handle.Free();
        }
    }

    private sealed class SpriteBitmap
    {
        public SpriteBitmap(ID2D1Bitmap bitmap, double left, double top, double width, double height)
        {
            Bitmap = bitmap;
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public ID2D1Bitmap Bitmap { get; }
        public double Left { get; }
        public double Top { get; }
        public double Width { get; }
        public double Height { get; }
    }

    public bool TryGetSourceRect(out ScreenRect rect)
    {
        if (TryGetSourceContentRect(out var left, out var top, out var width, out var height))
        {
            rect = new ScreenRect(left, top, left + width, top + height);
            return true;
        }

        rect = default;
        return false;
    }

    public void UpdateOverlaySnapshot(OverlaySnapshotData data)
    {
        lock (_gate)
        {
            if (_sourceAvailable && !_graphicsDisposed)
            {
                _pendingOverlay = data;
                RenderCurrentFrame(syncInterval: 0);
            }
        }
    }

    // The rectangle (screen physical px) the captured texture corresponds to.
    // DWM extended frame bounds match the composited window better than GetWindowRect
    // (which includes the invisible resize border on Win10+).
    private bool TryGetSourceContentRect(out double left, out double top, out double width, out double height)
    {
        left = top = width = height = 0;
        if (!_sourceAvailable)
        {
            return false;
        }

        if (!NativeMethods.IsWindow(_sourceWindow))
        {
            QueueSourceUnavailable("Source window is no longer available.");
            return false;
        }

        if (NativeMethods.DwmGetWindowAttribute(_sourceWindow, NativeMethods.DwmwaExtendedFrameBounds, out var bounds, Marshal.SizeOf<NativeMethods.Rect>()) != 0
            && !NativeMethods.GetWindowRect(_sourceWindow, out bounds))
        {
            return false;
        }

        width = bounds.Right - bounds.Left;
        height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        left = bounds.Left;
        top = bounds.Top;
        return true;
    }

    private void ResizeSwapChain(int width, int height)
    {
        // All back-buffer references (incl. the D2D target) must be released before ResizeBuffers.
        if (_d2dContext is not null)
        {
            _d2dContext.Target = null;
        }

        _d2dTarget?.Dispose();
        _d2dTarget = null;
        _backBuffer?.Dispose();
        _backBuffer = null;
        _sourceFrame?.Dispose();
        _sourceFrame = null;
        _swapChain!.ResizeBuffers(BufferCount, (uint)width, (uint)height, SwapFormat, SwapChainFlags.None);
        _backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _swapSize = new SizeInt32 { Width = width, Height = height };
        BindBackBufferTarget();
    }

    private void OnSourceClosed(object? sender, EventArgs e)
    {
        QueueSourceUnavailable("Source window was closed.");
    }

    private void QueueSourceUnavailable(string message)
    {
        if (_sourceUnavailableQueued || !_sourceAvailable || !IsHandleCreated || IsDisposed)
        {
            return;
        }

        _sourceUnavailableQueued = true;
        try
        {
            BeginInvoke((Action)(() => MarkSourceUnavailable(message)));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void MarkSourceUnavailable(string message)
    {
        if (!_sourceAvailable || IsDisposed)
        {
            return;
        }

        _sourceAvailable = false;
        Text = $"FocusTool Capture Stage - {_sourceTitle} (source unavailable)";
        _statusLabel.Text = $"{message}{Environment.NewLine}Close this Capture Stage and pick the source again.";
        _statusLabel.Visible = true;
        _statusLabel.BringToFront();

        DisposeCaptureSession();
        lock (_gate)
        {
            _pendingOverlay = null;
        }
    }

    private void DisposeCaptureSession()
    {
        WindowCaptureSession? session;
        lock (_gate)
        {
            session = _session;
            _session = null;
        }

        if (session is null)
        {
            return;
        }

        session.FrameArrived -= OnFrameArrived;
        session.SourceClosed -= OnSourceClosed;
        session.Dispose();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        DisposeGraphics();
        base.OnFormClosed(e);
    }

    private void DisposeGraphics()
    {
        WindowCaptureSession? session;
        lock (_gate)
        {
            if (_graphicsDisposed)
            {
                return;
            }

            _graphicsDisposed = true;
            session = _session;
            _session = null;
        }

        if (session is not null)
        {
            session.FrameArrived -= OnFrameArrived;
            session.SourceClosed -= OnSourceClosed;
            session.Dispose();
        }

        lock (_gate)
        {
            if (_d2dContext is not null)
            {
                _d2dContext.Target = null;
            }

            _d2dTarget?.Dispose();
            _d2dTarget = null;
            _overlayBitmap?.Dispose();
            _overlayBitmap = null;
            foreach (var sprite in _spriteBitmaps)
            {
                sprite.Bitmap.Dispose();
            }

            _spriteBitmaps.Clear();
            _pendingOverlay = null;
            _d2dContext?.Dispose();
            _d2dContext = null;
            _d2dDevice?.Dispose();
            _d2dDevice = null;
            _d2dFactory?.Dispose();
            _d2dFactory = null;
            _backBuffer?.Dispose();
            _backBuffer = null;
            _sourceFrame?.Dispose();
            _sourceFrame = null;
            _swapChain?.Dispose();
            _swapChain = null;
            _context?.Dispose();
            _context = null;
            _device?.Dispose();
            _device = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeGraphics();
        }

        base.Dispose(disposing);
    }
}
