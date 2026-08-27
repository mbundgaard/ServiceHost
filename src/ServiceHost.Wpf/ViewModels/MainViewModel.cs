using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceHost.Models;
using ServiceHost.Services;

namespace ServiceHost.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ProcessManager _processManager;
    private readonly LogManager _logManager;
    private readonly int _apiPort;
    private readonly string _configPath;
    private readonly DispatcherTimer _refreshTimer;
    private readonly object _pendingLogLock = new();
    private readonly System.Text.StringBuilder _pendingLogLines = new();
    private volatile bool _hasPendingLogLines;

    [ObservableProperty]
    private ObservableCollection<ServiceItemViewModel> _services = new();

    [ObservableProperty]
    private ServiceItemViewModel? _selectedService;

    [ObservableProperty]
    private bool _isLogPaused;

    /// <summary>
    /// Fired when the entire log view should be replaced (selection change, clear, unpause).
    /// </summary>
    public event Action<string>? LogReset;

    /// <summary>
    /// Fired when new log lines should be appended to the current view.
    /// </summary>
    public event Action<string>? LogAppended;

    partial void OnIsLogPausedChanged(bool value)
    {
        if (!value && SelectedService != null)
        {
            // Catch up: reload full log and discard pending buffer
            lock (_pendingLogLock)
            {
                _pendingLogLines.Clear();
                _hasPendingLogLines = false;
            }
            LogReset?.Invoke(_logManager.GetLogContent(SelectedService.Name));
        }
    }

    [ObservableProperty]
    private string _statusText = "0/0 services running";

    [ObservableProperty]
    private bool _isApiRunning;

    [ObservableProperty]
    private string _versionText = "v0";

    /// <summary>True once an update is available or ready — drives the hand cursor + accent color
    /// on the status-bar version label, and enables its click.</summary>
    [ObservableProperty]
    private bool _updateClickable;

    private const string GitHubRepo = "mbundgaard/ServiceHost";
    private static readonly TimeSpan UpdatePollInterval = TimeSpan.FromHours(1);
    private readonly CancellationTokenSource _updateCts = new();
    // Written on the UI thread (via Dispatcher), read on the poll thread — volatile for visibility.
    private volatile string? _updateUrl;            // release page (available state)
    private volatile string? _downloadedUpdatePath; // set once the background download lands (ready state)

    public int ApiPort => _apiPort;
    public string ConfigPath => _configPath;
    public string WindowTitle { get; }
    public bool HasSelectedService => SelectedService != null;

    public MainViewModel(ProcessManager processManager, LogManager logManager, int apiPort, string configPath, string folderName)
    {
        _processManager = processManager;
        _logManager = logManager;
        _apiPort = apiPort;
        _configPath = configPath;
        WindowTitle = $"ServiceHost — {folderName}";

        var version = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version;
        VersionText = version != null ? $"v{version.Major}" : "v0";

        // Start polling GitHub for newer releases in the background.
        _ = PollForUpdatesAsync(version, _updateCts.Token);

        // Initialize service view models
        foreach (var state in processManager.Services.Values)
        {
            var vm = new ServiceItemViewModel(state, this);
            Services.Add(vm);
        }

        // Subscribe to log updates
        _logManager.LogLineReceived += OnLogLineReceived;
        _processManager.StatusChanged += OnStatusChanged;
        _processManager.ServiceAdded += OnServiceAdded;
        _processManager.ServiceRemoved += OnServiceRemoved;

        // Timer to refresh UI periodically
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _refreshTimer.Tick += (s, e) => RefreshUI();
        _refreshTimer.Start();

        UpdateStatusText();
    }

    partial void OnSelectedServiceChanged(ServiceItemViewModel? value)
    {
        // Discard any pending lines from the previous service
        lock (_pendingLogLock)
        {
            _pendingLogLines.Clear();
            _hasPendingLogLines = false;
        }

        var content = value != null ? _logManager.GetLogContent(value.Name) : string.Empty;
        LogReset?.Invoke(content);
        OnPropertyChanged(nameof(HasSelectedService));
    }

    private void OnLogLineReceived(string serviceName, string line)
    {
        if (SelectedService?.Name != serviceName) return;

        lock (_pendingLogLock)
        {
            _pendingLogLines.AppendLine(line);
            _hasPendingLogLines = true;
        }
    }

    private void OnStatusChanged(string serviceName, ServiceStatus status)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var service = Services.FirstOrDefault(s => s.Name == serviceName);
            service?.RefreshStatus();
            UpdateStatusText();

            // Switch focus to the service being operated on
            if (service != null && (status == ServiceStatus.Starting || status == ServiceStatus.Stopping))
            {
                SelectedService = service;
            }
        });
    }

    private void OnServiceAdded(string name, ServiceState state)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (!Services.Any(s => s.Name == name))
            {
                Services.Add(new ServiceItemViewModel(state, this));
                UpdateStatusText();
            }
        });
    }

    private void OnServiceRemoved(string name)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var service = Services.FirstOrDefault(s => s.Name == name);
            if (service != null)
            {
                Services.Remove(service);
                if (SelectedService == service)
                    SelectedService = null;
                UpdateStatusText();
            }
        });
    }

    private void RefreshUI()
    {
        foreach (var service in Services)
        {
            service.RefreshStatus();
        }
        UpdateStatusText();
        FlushPendingLogLines();
    }

    private void FlushPendingLogLines()
    {
        if (!_hasPendingLogLines || IsLogPaused) return;

        string newLines;
        lock (_pendingLogLock)
        {
            newLines = _pendingLogLines.ToString();
            _pendingLogLines.Clear();
            _hasPendingLogLines = false;
        }

        if (newLines.Length > 0)
        {
            LogAppended?.Invoke(newLines);
        }
    }

    private void UpdateStatusText()
    {
        var running = Services.Count(s => s.Status == ServiceStatus.Running);
        var total = Services.Count;
        StatusText = $"{running}/{total} services running";
    }

    /// <summary>Background loop: check GitHub for a newer release, then (if found) nudge the
    /// status-bar version label and download the new build. Silent on every failure — an update
    /// problem must never disturb service management.</summary>
    private async Task PollForUpdatesAsync(Version? current, CancellationToken ct)
    {
        if (current == null) return;

        // Small initial delay so startup isn't competing with the first network call.
        try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (TaskCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            // Skip the network round-trip once a download is already staged.
            if (_downloadedUpdatePath == null || !System.IO.File.Exists(_downloadedUpdatePath))
            {
                try
                {
                    var info = await UpdateChecker.CheckAsync(GitHubRepo, current, ct);
                    if (info != null) await ApplyUpdateInfoAsync(info, ct);
                }
                catch { /* update poll must never crash the app */ }
            }

            try { await Task.Delay(UpdatePollInterval, ct); } catch (TaskCanceledException) { return; }
        }
    }

    /// <summary>Drives the status-bar nudge + background download given a discovered update.</summary>
    private async Task ApplyUpdateInfoAsync(UpdateInfo info, CancellationToken ct)
    {
        var versionStr = $"v{info.LatestVersion.Major}";

        // Phase 1: available — link to the release page while the download streams.
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _updateUrl = info.ReleaseUrl;
            VersionText = $"{versionStr} ↗";
            UpdateClickable = true;
        });

        if (string.IsNullOrEmpty(info.AssetUrl)) return; // no matching .exe asset — manual update only

        // Phase 2: background download to %TEMP%\ServiceHost-update-<version>.exe.
        var dlPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ServiceHost-update-{info.LatestVersion}.exe");
        bool ok = await UpdateApplier.DownloadAsync(info.AssetUrl!, dlPath, ct);
        if (!ok) return; // keep Phase 1 link so the user can still reach the release page

        // Phase 3: ready — click now installs + restarts.
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _downloadedUpdatePath = dlPath;
            VersionText = $"Update ready — {versionStr}";
            UpdateClickable = true;
        });
    }

    /// <summary>Invoked by the status-bar version click. Available → open the release page;
    /// ready → install the downloaded build and restart.</summary>
    public void OnVersionClicked()
    {
        if (!string.IsNullOrEmpty(_downloadedUpdatePath) && System.IO.File.Exists(_downloadedUpdatePath))
        {
            var currentExe = Environment.ProcessPath; // single-file safe
            if (string.IsNullOrEmpty(currentExe)) return;
            UpdateApplier.ApplyAndExit(_downloadedUpdatePath!, currentExe!);
            Application.Current.Shutdown();
            return;
        }

        if (string.IsNullOrEmpty(_updateUrl)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_updateUrl) { UseShellExecute = true });
        }
        catch { /* opening the browser is a nicety; never crash the app over it */ }
    }

    [RelayCommand]
    private void CopyPrompt()
    {
        var prompt = $@"There is a ServiceHost API running at http://localhost:{_apiPort}/ that manages local services. GET / returns a JSON manifest with all endpoints, service status, config examples, and tips. Use it to manage services.";
        Clipboard.SetText(prompt);
    }

    [RelayCommand]
    private void ClearLog()
    {
        if (SelectedService != null)
        {
            _logManager.ResetLog(SelectedService.Name);
            lock (_pendingLogLock)
            {
                _pendingLogLines.Clear();
                _hasPendingLogLines = false;
            }
            LogReset?.Invoke(string.Empty);
        }
    }

    [RelayCommand]
    private async Task StartAllAsync()
    {
        var tasks = Services
            .Where(s => s.Status != ServiceStatus.Running)
            .Select(s => s.StartAsync())
            .ToList();
        await Task.WhenAll(tasks);
    }

    [RelayCommand]
    private async Task StopAllAsync()
    {
        var tasks = Services
            .Where(s => s.Status != ServiceStatus.Stopped)
            .Select(s => s.StopAsync())
            .ToList();
        await Task.WhenAll(tasks);
    }

    public async Task StartServiceAsync(string name)
    {
        await _processManager.StartServiceAsync(name);
    }

    public async Task StopServiceAsync(string name)
    {
        await _processManager.StopServiceAsync(name);
    }

    public async Task RestartServiceAsync(string name)
    {
        await _processManager.RestartServiceAsync(name);
    }

    public void Dispose()
    {
        _updateCts.Cancel();
        _refreshTimer.Stop();
        _logManager.LogLineReceived -= OnLogLineReceived;
        _processManager.StatusChanged -= OnStatusChanged;
        _processManager.ServiceAdded -= OnServiceAdded;
        _processManager.ServiceRemoved -= OnServiceRemoved;
    }
}

public partial class ServiceItemViewModel : ObservableObject
{
    private readonly ServiceState _state;
    private readonly MainViewModel _parent;

    public string Name => _state.Config.Name;
    public string? Url => _state.Config.Url;
    public bool HasUrl => !string.IsNullOrEmpty(_state.Config.Url);

    [ObservableProperty]
    private ServiceStatus _status;

    [ObservableProperty]
    private int? _processId;

    [ObservableProperty]
    private bool _isOperating;

    public ServiceItemViewModel(ServiceState state, MainViewModel parent)
    {
        _state = state;
        _parent = parent;
        _status = state.Status;
        _processId = state.ProcessId;
    }

    public void RefreshStatus()
    {
        Status = _state.Status;
        ProcessId = _state.ProcessId;
    }

    [RelayCommand]
    public async Task StartAsync()
    {
        if (IsOperating) return;
        IsOperating = true;
        try
        {
            await _parent.StartServiceAsync(Name);
        }
        finally
        {
            IsOperating = false;
        }
    }

    [RelayCommand]
    public async Task StopAsync()
    {
        if (IsOperating) return;
        IsOperating = true;
        try
        {
            await _parent.StopServiceAsync(Name);
        }
        finally
        {
            IsOperating = false;
        }
    }

    [RelayCommand]
    public async Task RestartAsync()
    {
        if (IsOperating) return;
        IsOperating = true;
        try
        {
            await _parent.RestartServiceAsync(Name);
        }
        finally
        {
            IsOperating = false;
        }
    }
}
