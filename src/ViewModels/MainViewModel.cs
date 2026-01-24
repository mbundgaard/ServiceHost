using System.Collections.ObjectModel;
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

    [ObservableProperty]
    private ObservableCollection<ServiceItemViewModel> _services = new();

    [ObservableProperty]
    private ServiceItemViewModel? _selectedService;

    [ObservableProperty]
    private string _logContent = string.Empty;

    [ObservableProperty]
    private string _statusText = "0/0 services running";

    [ObservableProperty]
    private bool _isApiRunning;

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

        // Initialize service view models
        foreach (var state in processManager.Services.Values)
        {
            var vm = new ServiceItemViewModel(state, this);
            Services.Add(vm);
        }

        // Subscribe to log updates
        _logManager.LogLineReceived += OnLogLineReceived;
        _processManager.StatusChanged += OnStatusChanged;

        // Timer to refresh UI periodically
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _refreshTimer.Tick += (s, e) => RefreshUI();
        _refreshTimer.Start();

        UpdateStatusText();
    }

    partial void OnSelectedServiceChanged(ServiceItemViewModel? value)
    {
        if (value != null)
        {
            LogContent = _logManager.GetLogContent(value.Name);
        }
        else
        {
            LogContent = string.Empty;
        }
        OnPropertyChanged(nameof(HasSelectedService));
    }

    private void OnLogLineReceived(string serviceName, string line)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (SelectedService?.Name == serviceName)
            {
                LogContent = _logManager.GetLogContent(serviceName);
            }
        });
    }

    private void OnStatusChanged(string serviceName, ServiceStatus status)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var service = Services.FirstOrDefault(s => s.Name == serviceName);
            service?.RefreshStatus();
            UpdateStatusText();
        });
    }

    private void RefreshUI()
    {
        foreach (var service in Services)
        {
            service.RefreshStatus();
        }
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        var running = Services.Count(s => s.Status == ServiceStatus.Running);
        var total = Services.Count;
        StatusText = $"{running}/{total} services running";
    }

    [RelayCommand]
    private void CopyPrompt()
    {
        var prompt = $@"Use curl to discover the ServiceHost API at http://localhost:{_apiPort}/ - it will return a JSON manifest describing all available endpoints and the current status of configured services.";
        Clipboard.SetText(prompt);
    }

    [RelayCommand]
    private void ClearLog()
    {
        if (SelectedService != null)
        {
            _logManager.ResetLog(SelectedService.Name);
            LogContent = string.Empty;
        }
    }

    [RelayCommand]
    private async Task StartAllAsync()
    {
        foreach (var service in Services)
        {
            if (service.Status != ServiceStatus.Running)
            {
                await service.StartAsync();
            }
        }
    }

    [RelayCommand]
    private async Task StopAllAsync()
    {
        foreach (var service in Services)
        {
            if (service.Status != ServiceStatus.Stopped)
            {
                await service.StopAsync();
            }
        }
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
        _refreshTimer.Stop();
        _logManager.LogLineReceived -= OnLogLineReceived;
        _processManager.StatusChanged -= OnStatusChanged;
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
    private int? _port;

    [ObservableProperty]
    private bool _isOperating;

    public ServiceItemViewModel(ServiceState state, MainViewModel parent)
    {
        _state = state;
        _parent = parent;
        _status = state.Status;
        _processId = state.ProcessId;
        _port = state.Config.Port;
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
