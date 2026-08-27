using System.IO;
using System.Windows;
using ServiceHost.ViewModels;

namespace ServiceHost;

public partial class App : Application
{
    private ServiceHostRuntime? _runtime;
    private MainViewModel? _viewModel;

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        // If a previous session downloaded a newer build, swap it in now — before we
        // bind the API port or adopt child services — so the new version just comes up
        // on next launch. Child services keep running detached and are re-adopted.
        if (ServiceHostRuntime.TryApplyPendingUpdate())
        {
            Shutdown();
            return;
        }

        RegisterCrashHandlers();

        try
        {
            _runtime = await ServiceHostRuntime.StartAsync(ServiceHostOptions.FromArgs(e.Args));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to start ServiceHost:\n\n{ex.Message}",
                "ServiceHost Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _runtime.ShutdownRequested += () =>
        {
            Dispatcher.Invoke(() => Shutdown());
        };

        var folderName = new DirectoryInfo(AppContext.BaseDirectory).Name;
        _viewModel = new MainViewModel(
            _runtime.ProcessManager,
            _runtime.LogManager,
            _runtime.ApiPort,
            _runtime.ConfigurationService.ConfigPath,
            folderName);

        var mainWindow = new MainWindow
        {
            DataContext = _viewModel
        };
        mainWindow.Show();
    }

    private async void Application_Exit(object sender, ExitEventArgs e)
    {
        // Stop API server/UI resources only - child services keep running.
        _viewModel?.Dispose();

        if (_runtime != null)
        {
            await _runtime.StopAsync();
        }
    }

    private void RegisterCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            var msg = $"[UNHANDLED] {ex?.GetType().Name}: {ex?.Message}\n{ex?.StackTrace}";
            System.Diagnostics.Debug.WriteLine(msg);
            try { _runtime?.LogManager.WriteLine("_crash", msg); } catch { }
            MessageBox.Show(msg, "Unhandled Exception", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            var msg = $"[DISPATCHER] {args.Exception.GetType().Name}: {args.Exception.Message}\n{args.Exception.StackTrace}";
            System.Diagnostics.Debug.WriteLine(msg);
            try { _runtime?.LogManager.WriteLine("_crash", msg); } catch { }
            MessageBox.Show(msg, "Dispatcher Exception", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            var msg = $"[TASK] {args.Exception.GetType().Name}: {args.Exception.Message}\n{args.Exception.StackTrace}";
            System.Diagnostics.Debug.WriteLine(msg);
            try { _runtime?.LogManager.WriteLine("_crash", msg); } catch { }
            args.SetObserved();
        };
    }
}
