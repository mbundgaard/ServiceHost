using System.IO;
using ServiceHost.Api;
using ServiceHost.Services;

namespace ServiceHost;

public sealed class ServiceHostRuntime : IAsyncDisposable, IDisposable
{
    public ConfigurationService ConfigurationService { get; }
    public LogManager LogManager { get; }
    public ProcessManager ProcessManager { get; }
    public ApiHost ApiHost { get; }
    public int ApiPort { get; }
    public string ProjectDirectory => ConfigurationService.ConfigDirectory;

    public event Action? ShutdownRequested;

    private bool _stopped;

    private ServiceHostRuntime(
        ConfigurationService configurationService,
        LogManager logManager,
        ProcessManager processManager,
        ApiHost apiHost,
        int apiPort)
    {
        ConfigurationService = configurationService;
        LogManager = logManager;
        ProcessManager = processManager;
        ApiHost = apiHost;
        ApiPort = apiPort;

        ApiHost.ShutdownRequested += OnApiShutdownRequested;
    }

    public static async Task<ServiceHostRuntime> StartAsync(ServiceHostOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new ServiceHostOptions();
        var configService = string.IsNullOrWhiteSpace(options.ConfigPath)
            ? new ConfigurationService()
            : new ConfigurationService(options.ConfigPath);
        var loaded = await configService.LoadAsync();

        if (!loaded)
        {
            await ConfigurationService.SaveExampleConfigAsync(configService.ConfigPath);
            await configService.LoadAsync();
        }

        var apiPort = options.ApiPort ?? configService.Config.ApiPort;
        var logManager = new LogManager(configService.GetLogDirectory());
        var processManager = new ProcessManager(logManager, configService.ConfigDirectory);

        foreach (var serviceConfig in configService.Config.Services)
        {
            processManager.RegisterService(serviceConfig);
        }

        processManager.LoadExistingLogs();

        var apiHost = new ApiHost(apiPort, processManager, logManager, configService);
        var runtime = new ServiceHostRuntime(configService, logManager, processManager, apiHost, apiPort);
        apiHost.Start();

        return runtime;
    }

    public static bool TryApplyPendingUpdate()
    {
        try
        {
            var current = (System.Reflection.Assembly.GetEntryAssembly() ?? System.Reflection.Assembly.GetExecutingAssembly()).GetName().Version;
            var currentExe = Environment.ProcessPath;
            if (current == null || string.IsNullOrEmpty(currentExe)) return false;

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
            Relauncher.RelaunchAfterExit(currentExe, swapFromPath: bestPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task StopAsync()
    {
        if (_stopped) return;
        _stopped = true;

        ApiHost.ShutdownRequested -= OnApiShutdownRequested;
        await ApiHost.StopAsync();
        ApiHost.Dispose();
        ProcessManager.Dispose();
        LogManager.Dispose();
    }

    private void OnApiShutdownRequested() => ShutdownRequested?.Invoke();

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
