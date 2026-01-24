using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using ServiceHost.Models;

namespace ServiceHost.Services;

public class ProcessManager : IDisposable
{
    private readonly LogManager _logManager;
    private readonly Dictionary<string, ServiceState> _services = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public IReadOnlyDictionary<string, ServiceState> Services => _services;

    public event Action<string, ServiceStatus>? StatusChanged;

    public ProcessManager(LogManager logManager)
    {
        _logManager = logManager;
    }

    public void RegisterService(ServiceConfig config)
    {
        _services[config.Name] = new ServiceState(config);
    }

    /// <summary>
    /// Detect services that are already running by checking if their ports are in use.
    /// Also loads existing log files.
    /// </summary>
    public async Task DetectRunningServicesAsync()
    {
        foreach (var (name, state) in _services)
        {
            // Load existing log content
            _logManager.LoadExistingLog(name);

            // Check if port is in use
            if (state.Config.Port.HasValue)
            {
                var isRunning = await IsPortInUseAsync(state.Config.Port.Value);
                if (isRunning)
                {
                    state.Status = ServiceStatus.Running;
                    state.StartedAt = DateTime.Now; // We don't know actual start time
                    StatusChanged?.Invoke(name, ServiceStatus.Running);
                }
            }
        }
    }

    private static async Task<bool> IsPortInUseAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync("127.0.0.1", port);
            var timeoutTask = Task.Delay(500);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);
            return completedTask == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(bool success, string? error)> StartServiceAsync(string name, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!_services.TryGetValue(name, out var state))
            {
                return (false, $"Service '{name}' not found");
            }

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

            foreach (var arg in state.Config.Args)
            {
                startInfo.ArgumentList.Add(arg);
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

                // Set up output capture
                var checker = new ReadinessChecker(
                    state.Config.Port,
                    state.Config.ReadyPattern,
                    state.Config.StartupTimeoutSeconds);

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        _logManager.WriteLine(name, e.Data);
                        checker.CheckLine(e.Data);
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        _logManager.WriteLine(name, $"[stderr] {e.Data}");
                        checker.CheckLine(e.Data);
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

                // Wait for readiness
                var (ready, error) = await checker.WaitForReadyAsync(cancellationToken);

                if (!ready)
                {
                    _logManager.WriteLine(name, $"Readiness check failed: {error}");
                    await KillProcessTreeAsync(process);
                    state.SetFailed(error ?? "Readiness check failed");
                    StatusChanged?.Invoke(name, ServiceStatus.Failed);
                    return (false, error);
                }

                _logManager.WriteLine(name, "Service is ready");
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
            _lock.Release();
        }
    }

    public async Task<(bool success, string? error)> StopServiceAsync(string name, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!_services.TryGetValue(name, out var state))
            {
                return (false, $"Service '{name}' not found");
            }

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
            else if (state.Config.Port.HasValue)
            {
                // No process reference but we know the port - try to find and kill the process
                var killed = await KillProcessOnPortAsync(state.Config.Port.Value, state.Config.ShutdownTimeoutSeconds);
                if (killed)
                {
                    _logManager.WriteLine(name, "Service stopped (by port)");
                    state.SetStopped();
                    StatusChanged?.Invoke(name, ServiceStatus.Stopped);
                    return (true, null);
                }
                else
                {
                    // Port might already be free
                    var isRunning = await IsPortInUseAsync(state.Config.Port.Value);
                    if (!isRunning)
                    {
                        _logManager.WriteLine(name, "Service already stopped");
                        state.SetStopped();
                        StatusChanged?.Invoke(name, ServiceStatus.Stopped);
                        return (true, null);
                    }
                    else
                    {
                        var error = "Failed to stop process: could not find process to kill";
                        _logManager.WriteLine(name, error);
                        state.SetFailed(error);
                        StatusChanged?.Invoke(name, ServiceStatus.Failed);
                        return (false, error);
                    }
                }
            }
            else
            {
                // No process reference and no port - just mark as stopped
                state.SetStopped();
                StatusChanged?.Invoke(name, ServiceStatus.Stopped);
                return (true, null);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<bool> KillProcessOnPortAsync(int port, int timeoutSeconds)
    {
        try
        {
            // Use netstat to find the PID
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c netstat -ano | findstr \":{port}.*LISTENING\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            // Parse the PID from the output
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && int.TryParse(parts[^1].Trim(), out var pid))
                {
                    try
                    {
                        var targetProcess = Process.GetProcessById(pid);
                        targetProcess.Kill(entireProcessTree: true);

                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                        await targetProcess.WaitForExitAsync(cts.Token);
                        return true;
                    }
                    catch
                    {
                        // Process might already be gone
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
        }

        return false;
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
        _lock.Dispose();
    }
}
