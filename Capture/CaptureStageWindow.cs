using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using FocusTool.Win.Services;
using DrawingSize = System.Drawing.Size;
using Form = System.Windows.Forms.Form;
using FormClosedEventArgs = System.Windows.Forms.FormClosedEventArgs;
using Screen = System.Windows.Forms.Screen;

namespace FocusTool.Win.Capture;

/// <summary>
/// A normal, resizable window that mirrors a captured source window. Because it
/// is an ordinary top-level window with real swap-chain pixels, screen-share and
/// recording tools (OBS, Zoom, Discord, Teams) can grab it via "Share window"
/// while it carries the source content. v1 is view-only (no overlays yet).
/// </summary>
internal sealed class CaptureStageWindow : Form
{
    private const Format SwapFormat = Format.B8G8R8A8_UNorm;
    private const int BufferCount = 2;

    private readonly object _gate = new();
    private readonly IntPtr _sourceWindow;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGISwapChain1? _swapChain;
    private ID3D11Texture2D? _backBuffer;
    private WindowCaptureSession? _session;
    private SizeInt32 _swapSize;
    private bool _graphicsDisposed;

    public CaptureStageWindow(IntPtr sourceWindow)
    {
        _sourceWindow = sourceWindow;
        Text = "FocusTool Capture Stage";
        BackColor = System.Drawing.Color.Black;
        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
        ShowInTaskbar = true;
        MinimumSize = new DrawingSize(160, 120);
        ClientSize = new DrawingSize(640, 360);
        SetStyle(System.Windows.Forms.ControlStyles.AllPaintingInWmPaint | System.Windows.Forms.ControlStyles.Opaque, true);
    }

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

    private void InitializeGraphics()
    {
        _device = CreateDevice();
        _context = _device.ImmediateContext;
        _session = new WindowCaptureSession(_device, _sourceWindow);

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
            if (_graphicsDisposed || _swapChain is null || _backBuffer is null || _context is null)
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

            _context.CopyResource(_backBuffer, texture);
            _swapChain.Present(1, PresentFlags.None);
        }
    }

    private void ResizeSwapChain(int width, int height)
    {
        _backBuffer?.Dispose();
        _backBuffer = null;
        _swapChain!.ResizeBuffers(BufferCount, (uint)width, (uint)height, SwapFormat, SwapChainFlags.None);
        _backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _swapSize = new SizeInt32 { Width = width, Height = height };
    }

    private void OnSourceClosed(object? sender, EventArgs e)
    {
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(Close);
        }
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
            _backBuffer?.Dispose();
            _backBuffer = null;
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
