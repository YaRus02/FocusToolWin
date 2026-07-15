using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FocusTool.Win.Native;

internal sealed class HotKeyManager : IDisposable
{
    private const int FirstHotKeyId = 0x4C51;

    private readonly Dictionary<int, Action> _actions = [];
    private readonly List<string> _registrationErrors = [];
    private readonly HwndSource _source;
    private readonly Action<Exception>? _callbackErrorHandler;
    private bool _disposed;

    public IReadOnlyList<string> RegistrationErrors => _registrationErrors;

    public HotKeyManager(Action<Exception>? callbackErrorHandler = null)
    {
        _callbackErrorHandler = callbackErrorHandler;
        var parameters = new HwndSourceParameters("FocusToolHotkeySink")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    // Re-registers in place on the long-lived sink window. Callable re-entrantly
    // from inside a hotkey action (the WndProc): it only unregisters/registers
    // hotkey ids on the existing window, never destroys the window mid-message.
    public void SetRegistrations(IEnumerable<HotKeyRegistration> registrations)
    {
        if (_disposed)
        {
            return;
        }

        foreach (var existingId in _actions.Keys)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, existingId);
        }

        _actions.Clear();
        _registrationErrors.Clear();

        var id = FirstHotKeyId;
        foreach (var registration in registrations)
        {
            if (string.IsNullOrWhiteSpace(registration.ShortcutText))
            {
                continue;
            }

            id++;
            if (!Shortcut.TryParse(registration.ShortcutText, out var shortcut))
            {
                _registrationErrors.Add($"Invalid shortcut: {registration.ShortcutText}");
                continue;
            }

            if (shortcut.IsMouseButton)
            {
                _registrationErrors.Add($"Global hotkey must be a keyboard shortcut: {registration.ShortcutText}");
                continue;
            }

            if (!NativeMethods.RegisterHotKey(_source.Handle, id, shortcut.ToNativeModifiers(), (uint)shortcut.VirtualKey))
            {
                var ex = new Win32Exception(Marshal.GetLastWin32Error());
                _registrationErrors.Add($"Could not register {registration.ShortcutText}: {ex.Message}");
                continue;
            }

            _actions[id] = registration.Action;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var id in _actions.Keys)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        }

        _actions.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                try
                {
                    _callbackErrorHandler?.Invoke(ex);
                }
                catch
                {
                    // Exceptions must never escape the HwndSource hook.
                }
            }

            handled = true;
        }

        return IntPtr.Zero;
    }
}

internal readonly record struct HotKeyRegistration(string ShortcutText, Action Action);
