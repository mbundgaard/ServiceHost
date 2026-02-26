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
    private VersionChecker? _versionChecker;
    private ApiHost? _apiHost;
    private MainViewModel? _viewModel;

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        // Load configuration
        _configService = new ConfigurationService();
        var loaded = await _configService.LoadAsync();

        if (!loaded)
        {
            var result = MessageBox.Show(
                "ServiceHost.json not found.\n\nWould you like to create an example configuration file?",
                "Configuration Not Found",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var examplePath = Path.Combine(AppContext.BaseDirectory, "ServiceHost.json");
                await ConfigurationService.SaveExampleConfigAsync(examplePath);
                MessageBox.Show(
                    $"Example configuration created at:\n{examplePath}\n\nPlease edit the file and restart the application.",
                    "Configuration Created",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown();
            return;
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

        // Initialize version checker
        _versionChecker = new VersionChecker();

        // Start API host
        _apiHost = new ApiHost(_configService.Config.ApiPort, _processManager, _logManager, _configService, _versionChecker);
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
        _versionChecker?.Dispose();
        _viewModel?.Dispose();
    }
}
