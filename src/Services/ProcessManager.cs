using System.Diagnostics;
using System.IO;
using ServiceHost.Models;

namespace ServiceHost.Services;

public class ProcessManager : IDisposable
{
    private readonly LogManager _logManager;
    private readonly Dictionary<string, ServiceState> _services = new();
    private readonly Dictionary<string, SemaphoreSlim> _serviceLocks = new();
    private readonly object _lockSync = new();
    private bool _disposed;

    public IReadOnlyDictionary<string, ServiceState> Services => _services;

    public event Action<string, ServiceStatus>? StatusChanged;
    public event Action<string, ServiceState>? ServiceAdded;
    public event Action<string>? ServiceRemoved;

    public ProcessManager(LogManager logManager)
    {
        _logManager = logManager;
    }

    public void RegisterService(ServiceConfig config)
    {
        var state = new ServiceState(config);
        _services[config.Name] = state;
        lock (_lockSync)
        {
            _serviceLocks[config.Name] = new SemaphoreSlim(1, 1);
        }
        ServiceAdded?.Invoke(config.Name, state);
    }

    private SemaphoreSlim GetServiceLock(string name)
    {
        lock (_lockSync)
        {
            if (!_serviceLocks.TryGetValue(name, out var serviceLock))
            {
                serviceLock = new SemaphoreSlim(1, 1);
                _serviceLocks[name] = serviceLock;
            }
            return serviceLock;
        }
    }

    /// <summary>
    /// Unregister a service. Stops it first if running.
    /// </summary>
    public async Task UnregisterServiceAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_services.TryGetValue(name, out var state))
        {
            if (state.Status == ServiceStatus.Running || state.Status == ServiceStatus.Starting)
            {
                await StopServiceAsync(name, cancellationToken);
            }
            _services.Remove(name);
            ServiceRemoved?.Invoke(name);
        }
    }

    /// <summary>
    /// Check if a service exists.
    /// </summary>
    public bool HasService(string name) => _services.ContainsKey(name);

    /// <summary>
    /// Load existing log files for all registered services.
    /// </summary>
    public void LoadExistingLogs()
    {
        foreach (var (name, _) in _services)
        {
            _logManager.LoadExistingLog(name);
        }
    }

    public async Task<(bool success, string? error)> StartServiceAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!_services.TryGetValue(name, out var state))
        {
            return (false, $"Service '{name}' not found");
        }

        var serviceLock = GetServiceLock(name);
        await serviceLock.WaitAsync(cancellationToken);
        try
        {

            if (state.Status == ServiceStatus.Running)
            {
                return (true, null);
            }

            state.Status = ServiceStatus.Starting;
            StatusChanged?.Invoke(name, ServiceStatus.Starting);

            // Reset log file
            _logManager.ResetLog(name);
            _logManager.WriteLine(name, $"Starting service: {state.Config.Command} {string.Join(" ", state.Config.Args)}");

            var startInfo = new ProcessStartInfo
            {
                FileName = state.Config.Command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // When command is cmd /c, join remaining args into a single command string
            // so that shell environment (e.g. npm's PATH setup) propagates correctly
            var args = state.Config.Args;
            if (args.Count >= 2
                && state.Config.Command.Equals("cmd", StringComparison.OrdinalIgnoreCase)
                && args[0].Equals("/c", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add(string.Join(" ", args.Skip(1)));
            }
            else
            {
                foreach (var arg in args)
                {
                    startInfo.ArgumentList.Add(arg);
                }
            }

            if (!string.IsNullOrEmpty(state.Config.WorkingDirectory))
            {
                var workDir = state.Config.WorkingDirectory;
                if (!Path.IsPathRooted(workDir))
                {
                    workDir = Path.Combine(AppContext.BaseDirectory, workDir);
                }
                startInfo.WorkingDirectory = Path.GetFullPath(workDir);
            }

            if (state.Config.Environment != null)
            {
                foreach (var (key, value) in state.Config.Environment)
                {
                    startInfo.Environment[key] = value;
                }
            }

            Process process;
            try
            {
                process = new Process { StartInfo = startInfo };
                process.EnableRaisingEvents = true;

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        _logManager.WriteLine(name, e.Data);
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        _logManager.WriteLine(name, $"[stderr] {e.Data}");
                    }
                };

                process.Exited += (s, e) =>
                {
                    _logManager.WriteLine(name, $"Process exited with code {process.ExitCode}");
                    if (state.Status == ServiceStatus.Running)
                    {
                        state.SetFailed($"Process exited unexpectedly with code {process.ExitCode}");
                        StatusChanged?.Invoke(name, ServiceStatus.Failed);
                    }
                };

                if (!process.Start())
                {
                    state.SetFailed("Failed to start process");
                    StatusChanged?.Invoke(name, ServiceStatus.Failed);
                    return (false, "Failed to start process");
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                _logManager.WriteLine(name, $"Process started with PID {process.Id}");
                state.SetRunning(process);
                StatusChanged?.Invoke(name, ServiceStatus.Running);
                return (true, null);
            }
            catch (Exception ex)
            {
                var error = $"Failed to start process: {ex.Message}";
                _logManager.WriteLine(name, error);
                state.SetFailed(error);
                StatusChanged?.Invoke(name, ServiceStatus.Failed);
                return (false, error);
            }
        }
        finally
        {
            serviceLock.Release();
        }
    }

    public async Task<(bool success, string? error)> StopServiceAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!_services.TryGetValue(name, out var state))
        {
            return (false, $"Service '{name}' not found");
        }

        var serviceLock = GetServiceLock(name);
        await serviceLock.WaitAsync(cancellationToken);
        try
        {
            if (state.Status == ServiceStatus.Stopped)
            {
                return (true, null);
            }

            state.Status = ServiceStatus.Stopping;
            StatusChanged?.Invoke(name, ServiceStatus.Stopping);
            _logManager.WriteLine(name, "Stopping service...");

            // If we have a process reference, use it
            if (state.Process != null)
            {
                var process = state.Process;
                var timeout = TimeSpan.FromSeconds(state.Config.ShutdownTimeoutSeconds);

                try
                {
                    // Try graceful shutdown first
                    if (!process.HasExited)
                    {
                        process.CloseMainWindow();

                        using var cts = new CancellationTokenSource(timeout);
                        try
                        {
                            await process.WaitForExitAsync(cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // Graceful shutdown timed out, force kill
                            _logManager.WriteLine(name, "Graceful shutdown timed out, force killing...");
                        }
                    }

                    // Force kill if still running
                    if (!process.HasExited)
                    {
                        await KillProcessTreeAsync(process);
                    }

                    _logManager.WriteLine(name, "Service stopped");
                    state.SetStopped();
                    StatusChanged?.Invoke(name, ServiceStatus.Stopped);
                    return (true, null);
                }
                catch (Exception ex)
                {
                    var error = $"Failed to stop process: {ex.Message}";
                    _logManager.WriteLine(name, error);
                    state.SetFailed(error);
                    StatusChanged?.Invoke(name, ServiceStatus.Failed);
                    return (false, error);
                }
            }
            else
            {
                // No process reference - just mark as stopped
                state.SetStopped();
                StatusChanged?.Invoke(name, ServiceStatus.Stopped);
                return (true, null);
            }
        }
        finally
        {
            serviceLock.Release();
        }
    }

    public async Task<(bool success, string? error)> RestartServiceAsync(string name, CancellationToken cancellationToken = default)
    {
        var stopResult = await StopServiceAsync(name, cancellationToken);
        if (!stopResult.success)
        {
            return stopResult;
        }

        return await StartServiceAsync(name, cancellationToken);
    }

    private static async Task KillProcessTreeAsync(Process process)
    {
        try
        {
            if (process.HasExited) return;

            // Kill the entire process tree
            process.Kill(entireProcessTree: true);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Last resort - just kill the main process
                try { process.Kill(); } catch { }
            }
        }
        catch
        {
            // Process might already be gone
        }
    }

    public async Task StopAllServicesAsync(CancellationToken cancellationToken = default)
    {
        var tasks = _services.Keys.Select(name => StopServiceAsync(name, cancellationToken));
        await Task.WhenAll(tasks);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Don't stop services - just clean up resources
        lock (_lockSync)
        {
            foreach (var serviceLock in _serviceLocks.Values)
            {
                serviceLock.Dispose();
            }
            _serviceLocks.Clear();
        }
    }
}
