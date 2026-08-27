using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ServiceHost.Models;

public enum ServiceStatus
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed
}

public partial class ServiceState : ObservableObject
{
    public ServiceConfig Config { get; }

    [ObservableProperty]
    private ServiceStatus _status = ServiceStatus.Stopped;

    [ObservableProperty]
    private int? _processId;

    [ObservableProperty]
    private string? _lastError;

    [ObservableProperty]
    private DateTime? _startedAt;

    public Process? Process { get; set; }

    public ServiceState(ServiceConfig config)
    {
        Config = config;
    }

    public void Reset()
    {
        Status = ServiceStatus.Stopped;
        ProcessId = null;
        LastError = null;
        StartedAt = null;
        Process = null;
    }

    public void SetRunning(Process process)
    {
        Process = process;
        ProcessId = process.Id;
        Status = ServiceStatus.Running;
        StartedAt = DateTime.Now;
        LastError = null;
    }

    public void SetFailed(string error)
    {
        Status = ServiceStatus.Failed;
        LastError = error;
        Process = null;
        ProcessId = null;
    }

    public void SetStopped()
    {
        Status = ServiceStatus.Stopped;
        Process = null;
        ProcessId = null;
        StartedAt = null;
    }
}
