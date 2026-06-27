using Vortice.Direct3D11;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace FocusTool.Win.Capture;

/// <summary>
/// Mirrors a single source window via Windows Graphics Capture. Frames are
/// delivered on a pool thread as Direct3D11 textures living on the supplied
/// device, so the consumer can copy them straight into its own swap chain.
/// </summary>
internal sealed class WindowCaptureSession : IDisposable
{
    private const DirectXPixelFormat PixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;
    private const int BufferCount = 2;

    private readonly object _gate = new();
    private readonly IDirect3DDevice _winrtDevice;
    private readonly GraphicsCaptureItem _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private SizeInt32 _lastSize;
    private bool _disposed;

    public WindowCaptureSession(ID3D11Device device, IntPtr sourceWindow)
    {
        _winrtDevice = CaptureInterop.CreateDirect3DDevice(device);
        _item = CaptureInterop.CreateItemForWindow(sourceWindow);
        _item.Closed += OnItemClosed;
        _lastSize = _item.Size;
    }

    /// <summary>Raised on a capture pool thread. The texture is owned by the session and is released after the handler returns.</summary>
    public event Action<ID3D11Texture2D, SizeInt32>? FrameArrived;

    public event EventHandler? SourceClosed;

    public SizeInt32 SourceSize => _lastSize;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice, PixelFormat, BufferCount, _item.Size);
            _framePool.FrameArrived += OnFrameArrived;
            _session = _framePool.CreateCaptureSession(_item);
            _session.IsCursorCaptureEnabled = false;
            _session.StartCapture();
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();
        if (frame is null)
        {
            return;
        }

        var contentSize = frame.ContentSize;
        var handler = FrameArrived;
        if (handler is not null)
        {
            using var texture = CaptureInterop.GetTexture(frame.Surface);
            handler(texture, contentSize);
        }

        if (contentSize.Width == _lastSize.Width && contentSize.Height == _lastSize.Height)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed || _framePool is null)
            {
                return;
            }

            _lastSize = contentSize;
            _framePool.Recreate(_winrtDevice, PixelFormat, BufferCount, contentSize);
        }
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        SourceClosed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _item.Closed -= OnItemClosed;
        if (_framePool is not null)
        {
            _framePool.FrameArrived -= OnFrameArrived;
        }

        _session?.Dispose();
        _framePool?.Dispose();
        _winrtDevice.Dispose();
    }
}
