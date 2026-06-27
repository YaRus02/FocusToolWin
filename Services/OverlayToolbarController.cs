using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

internal sealed class OverlayToolbarController
{
    private readonly FocusToolController _controller;
    private readonly Func<bool> _isDisposed;
    private readonly Action _reassertOverlayTopmost;
    private readonly Action _stateChanged;
    private OverlayToolbarWindow? _window;

    public OverlayToolbarController(
        FocusToolController controller,
        Func<bool> isDisposed,
        Action reassertOverlayTopmost,
        Action stateChanged)
    {
        _controller = controller;
        _isDisposed = isDisposed;
        _reassertOverlayTopmost = reassertOverlayTopmost;
        _stateChanged = stateChanged;
    }

    public bool IsVisible => _window?.IsVisible == true;

    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        Show();
    }

    public void Show()
    {
        EnsureWindow();
        _window!.ShowNearCursor();
        _reassertOverlayTopmost();
        _window.ReassertTopmost();
        _stateChanged();
    }

    public void Hide()
    {
        if (_window is not { IsVisible: true })
        {
            return;
        }

        _window.Hide();
        _stateChanged();
    }

    public void HideTransient()
    {
        _window?.Hide();
    }

    public void Close()
    {
        _window?.Close();
        _window = null;
    }

    public void ReassertTopmost()
    {
        if (_window is { IsVisible: true })
        {
            _window.ReassertTopmost();
        }
    }

    public bool TryGetVisibleHandle(out IntPtr handle)
    {
        if (_window is { IsVisible: true } && _window.Handle != IntPtr.Zero)
        {
            handle = _window.Handle;
            return true;
        }

        handle = IntPtr.Zero;
        return false;
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        _window = new OverlayToolbarWindow(_controller);
        _window.Closed += (_, _) =>
        {
            _window = null;
            if (!_isDisposed())
            {
                _stateChanged();
            }
        };
    }
}
