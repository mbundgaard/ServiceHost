using System.IO;
using System.Windows;
using ServiceHost.Api;
using ServiceHost.Services;
using ServiceHost.ViewModels;

namespace ServiceHost;

public partial class App : Application
{
    private ConfigurationService? _configService;
    private LogManager? _logManager;
    private ProcessManager? _processManager;
    private ApiHost? _apiHost;
    private MainViewModel? _viewModel;

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        // If a previous session downloaded a newer build, swap it in now — before we
        // bind the API port or adopt child services — so the new version just comes up
        // on next launch. Child services keep running detached and are re-adopted.
        if (TryApplyPendingUpdate())
        {
            Shutdown();
            return;
        }

        // Catch unhandled exceptions to prevent silent crashes
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            var msg = $"[UNHANDLED] {ex?.GetType().Name}: {ex?.Message}\n{ex?.StackTrace}";
            System.Diagnostics.Debug.WriteLine(msg);
            try { _logManager?.WriteLine("_crash", msg); } catch { }
            MessageBox.Show(msg, "Unhandled Exception", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            var msg = $"[DISPATCHER] {args.Exception.GetType().Name}: {args.Exception.Message}\n{args.Exception.StackTrace}";
            System.Diagnostics.Debug.WriteLine(msg);
            try { _logManager?.WriteLine("_crash", msg); } catch { }
            MessageBox.Show(msg, "Dispatcher Exception", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            var msg = $"[TASK] {args.Exception.GetType().Name}: {args.Exception.Message}\n{args.Exception.StackTrace}";
            System.Diagnostics.Debug.WriteLine(msg);
            try { _logManager?.WriteLine("_crash", msg); } catch { }
            args.SetObserved();
        };

        // Load configuration
        _configService = new ConfigurationService();
        var loaded = await _configService.LoadAsync();

        if (!loaded)
        {
            await ConfigurationService.SaveExampleConfigAsync(_configService.ConfigPath);
            await _configService.LoadAsync();
        }

        // Initialize services
        var logDirectory = _configService.GetLogDirectory();
        _logManager = new LogManager(logDirectory);
        _processManager = new ProcessManager(_logManager);

        // Register services from config
        foreach (var serviceConfig in _configService.Config.Services)
        {
            _processManager.RegisterService(serviceConfig);
        }

        // Load existing log files
        _processManager.LoadExistingLogs();

        // Start API host
        _apiHost = new ApiHost(_configService.Config.ApiPort, _processManager, _logManager, _configService);
        _apiHost.ShutdownRequested += () =>
        {
            Dispatcher.Invoke(() => Shutdown());
        };
        try
        {
            _apiHost.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to start API on port {_configService.Config.ApiPort}:\n\n{ex.Message}",
                "API Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // Create view model
        var folderName = new DirectoryInfo(AppContext.BaseDirectory).Name;
        _viewModel = new MainViewModel(_processManager, _logManager, _configService.Config.ApiPort, _configService.ConfigPath, folderName);

        // Create and show main window
        var mainWindow = new MainWindow
        {
            DataContext = _viewModel
        };
        mainWindow.Show();
    }

    private async void Application_Exit(object sender, ExitEventArgs e)
    {
        // Stop API server only - services keep running
        if (_apiHost != null)
        {
            await _apiHost.StopAsync();
            _apiHost.Dispose();
        }

        // Clean up without stopping services
        _logManager?.Dispose();
        _viewModel?.Dispose();
    }

    /// <summary>Returns true if a pending update was found and the relauncher was started
    /// (caller must Shutdown immediately so the swap can complete).</summary>
    private static bool TryApplyPendingUpdate()
    {
        try
        {
            var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var currentExe = Environment.ProcessPath; // single-file safe; Assembly.Location is empty
            if (current == null || string.IsNullOrEmpty(currentExe)) return false;

            // UpdateApplier.DownloadAsync writes to %TEMP%\ServiceHost-update-<version>.exe.
            // Scan for any such file with a higher version than what's running and apply the highest.
            const string prefix = "ServiceHost-update-";
            var candidates = Directory.GetFiles(Path.GetTempPath(), prefix + "*.exe");
            Version? bestVer = null;
            string? bestPath = null;
            foreach (var path in candidates)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var verStr = name.Substring(prefix.Length);
                if (!Version.TryParse(verStr, out var ver)) continue;
                if (ver <= current) continue;
                if (bestVer == null || ver > bestVer)
                {
                    bestVer = ver;
                    bestPath = path;
                }
            }

            if (bestPath == null) return false;
            Services.Relauncher.RelaunchAfterExit(currentExe!, swapFromPath: bestPath);
            return true;
        }
        catch
        {
            // Detection must never block startup — if anything throws, just boot normally.
            return false;
        }
    }
}
