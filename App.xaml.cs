using System.Threading;
using System.Windows;
using FocusTool.Win.Services;

namespace FocusTool.Win;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private FocusToolController? _controller;

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

        DispatcherUnhandledException += (_, args) =>
        {
            Services.AppLog.Error("Unhandled dispatcher exception", args.Exception);
            System.Windows.MessageBox.Show(args.Exception.Message, "FocusTool - error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        _controller = new FocusToolController();
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
