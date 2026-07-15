using System.Threading;
using System.Windows;
using FocusTool.Win.Services;

namespace FocusTool.Win;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private FocusToolController? _controller;
    private bool _fatalShutdownStarted;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "FocusTool.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            _controller = new FocusToolController();
            _controller.Start();
        }
        catch (Exception ex)
        {
            Services.AppLog.Error("FocusTool startup failed", ex);
            DisposeControllerSafely();
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        DisposeControllerSafely();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args)
    {
        Services.AppLog.Error("Unhandled dispatcher exception; shutting down safely", args.Exception);
        args.Handled = true;
        if (_fatalShutdownStarted)
        {
            return;
        }

        _fatalShutdownStarted = true;
        DisposeControllerSafely();
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Send,
            () => Shutdown(-1));
    }

    private void DisposeControllerSafely()
    {
        var controller = _controller;
        _controller = null;
        if (controller is null)
        {
            return;
        }

        try
        {
            controller.Dispose();
        }
        catch (Exception ex)
        {
            Services.AppLog.Error("FocusTool cleanup failed", ex);
        }
    }
}
