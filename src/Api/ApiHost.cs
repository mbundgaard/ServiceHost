using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ServiceHost.Models;
using ServiceHost.Services;

namespace ServiceHost.Api;

public class ApiHost : IDisposable
{
    private readonly int _port;
    private readonly ProcessManager _processManager;
    private readonly LogManager _logManager;
    private readonly ConfigurationService _configService;
    private readonly VersionChecker _versionChecker;
    private readonly string _configPath;
    private WebApplication? _app;
    private Task? _runTask;
    private readonly CancellationTokenSource _cts = new();

    public bool IsRunning { get; private set; }
    public event Action? ShutdownRequested;

    public ApiHost(int port, ProcessManager processManager, LogManager logManager, ConfigurationService configService, VersionChecker versionChecker)
    {
        _port = port;
        _processManager = processManager;
        _logManager = logManager;
        _configService = configService;
        _versionChecker = versionChecker;
        _configPath = configService.ConfigPath;
    }

    /// <summary>
    /// Check if config has changed and apply updates (add new services, remove deleted ones).
    /// </summary>
    private async Task<object?> CheckAndApplyConfigChangesAsync(CancellationToken ct)
    {
        var changes = await _configService.ReloadIfChangedAsync();
        if (changes == null || !changes.HasChanges)
            return null;

        // Remove deleted services
        foreach (var removed in changes.Removed)
        {
            await _processManager.UnregisterServiceAsync(removed, ct);
        }

        // Add new services
        foreach (var added in changes.Added)
        {
            var config = _configService.Config.Services.FirstOrDefault(s => s.Name == added);
            if (config != null)
            {
                _processManager.RegisterService(config);
                await _processManager.DetectRunningServicesAsync();
            }
        }

        return new
        {
            configReloaded = true,
            added = changes.Added,
            removed = changes.Removed
        };
    }

    public void Start()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://localhost:{_port}");
        builder.Logging.ClearProviders();

        _app = builder.Build();

        // GET / - API manifest with service status
        _app.MapGet("/", async (CancellationToken ct) =>
        {
            await CheckAndApplyConfigChangesAsync(ct);
            var versionInfo = await _versionChecker.CheckForUpdateAsync();
            var services = _processManager.Services.Values.Select(s => new
            {
                name = s.Config.Name,
                status = s.Status.ToString().ToLowerInvariant(),
                port = s.Config.Port,
                pid = s.ProcessId,
                command = $"{s.Config.Command} {string.Join(" ", s.Config.Args)}",
                workingDirectory = s.Config.WorkingDirectory,
                startedAt = s.StartedAt?.ToString("o"),
                error = s.LastError
            });

            object? update = versionInfo.UpdateAvailable ? new
            {
                currentVersion = versionInfo.CurrentVersion,
                newVersion = versionInfo.LatestVersion,
                downloadUrl = versionInfo.DownloadUrl,
                exePath = versionInfo.ExePath,
                processId = versionInfo.ProcessId,
                instructions = "To update: 1) Download from downloadUrl to exePath.tmp, 2) POST /shutdown, 3) Wait for processId to exit, 4) Delete exePath, 5) Rename exePath.tmp to exePath, 6) Start exePath"
            } : null;

            var manifest = new
            {
                name = "ServiceHost",
                version = versionInfo.CurrentVersion,
                update,
                description = "Service manager with HTTP API for Claude Code",
                configPath = _configPath,
                configuration = new
                {
                    note = "Services can be managed via API (POST/PUT/DELETE /services) or by editing the config file. File changes are auto-detected on next API request.",
                    file = _configPath,
                    format = new
                    {
                        services = new[]
                        {
                            new
                            {
                                name = "service-name",
                                command = "executable",
                                args = new[] { "arg1", "arg2" },
                                workingDirectory = "./path",
                                port = 5000,
                                url = "http://localhost:5000/health",
                                environment = new Dictionary<string, string> { ["KEY"] = "value" },
                                startupTimeoutSeconds = 30
                            }
                        }
                    }
                },
                endpoints = new Dictionary<string, string>
                {
                    ["GET /"] = "API description and service status",
                    ["GET /services"] = "List all services",
                    ["POST /services"] = "Create a new service (JSON body with service config)",
                    ["PUT /services/{name}"] = "Update an existing service (JSON body with service config)",
                    ["DELETE /services/{name}"] = "Delete a service (stops if running)",
                    ["GET /services/{name}/logs?tail=N"] = "Get last N lines of logs (default 100)",
                    ["POST /services/logs/clear"] = "Clear logs for all services",
                    ["POST /services/{name}/logs/clear"] = "Clear log for one service",
                    ["POST /services/start"] = "Start all services (parallel)",
                    ["POST /services/stop"] = "Stop all services (parallel)",
                    ["POST /services/restart"] = "Restart all services (parallel)",
                    ["POST /services/{name}/start"] = "Start a service (blocks until ready)",
                    ["POST /services/{name}/stop"] = "Stop a service (blocks until stopped)",
                    ["POST /services/{name}/restart"] = "Restart a service",
                    ["POST /shutdown"] = "Shutdown the application (for updates)"
                },
                tips = new[]
                {
                    "Logs are auto-cleared on start/restart. Use clear to remove old entries before testing, so subsequent log fetches show only relevant output.",
                    "Batch operations (start/stop/restart all) run in parallel for faster execution.",
                    "Start blocks until the port accepts connections, so when it returns the service is ready to use.",
                    "Use ?tail=N on the logs endpoint to limit output and avoid large responses.",
                    "Starting an already-running service returns success immediately (idempotent).",
                    "Services keep running even when the UI is closed - they persist until explicitly stopped.",
                    "Use POST/PUT/DELETE /services to create, update, or delete services via API. Changes are persisted to the config file automatically.",
                    "When updating a running service (PUT), it will be automatically stopped and restarted with the new configuration."
                },
                examples = new Dictionary<string, string>
                {
                    ["start_one"] = $"curl -X POST http://localhost:{_port}/services/api/start",
                    ["stop_one"] = $"curl -X POST http://localhost:{_port}/services/api/stop",
                    ["start_all"] = $"curl -X POST http://localhost:{_port}/services/start",
                    ["get_logs"] = $"curl http://localhost:{_port}/services/api/logs?tail=50",
                    ["clear_log"] = $"curl -X POST http://localhost:{_port}/services/api/logs/clear",
                    ["create_service"] = $"curl -X POST http://localhost:{_port}/services -H \"Content-Type: application/json\" -d '{{\"name\":\"myservice\",\"command\":\"python\",\"args\":[\"-m\",\"http.server\",\"8080\"],\"port\":8080}}'",
                    ["update_service"] = $"curl -X PUT http://localhost:{_port}/services/myservice -H \"Content-Type: application/json\" -d '{{\"name\":\"myservice\",\"command\":\"python\",\"args\":[\"-m\",\"http.server\",\"9090\"],\"port\":9090}}'",
                    ["delete_service"] = $"curl -X DELETE http://localhost:{_port}/services/myservice"
                },
                services
            };

            return Results.Json(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
        });

        // GET /services - List all services
        _app.MapGet("/services", async (CancellationToken ct) =>
        {
            await CheckAndApplyConfigChangesAsync(ct);
            var services = _processManager.Services.Values.Select(s => new
            {
                name = s.Config.Name,
                status = s.Status.ToString().ToLowerInvariant(),
                port = s.Config.Port,
                pid = s.ProcessId,
                command = $"{s.Config.Command} {string.Join(" ", s.Config.Args)}",
                workingDirectory = s.Config.WorkingDirectory,
                startedAt = s.StartedAt?.ToString("o"),
                error = s.LastError
            });

            return Results.Json(new { services }, new JsonSerializerOptions { WriteIndented = true });
        });

        // POST /services - Create a new service
        _app.MapPost("/services", async (ServiceConfig service, CancellationToken ct) =>
        {
            // Check if service already exists in process manager
            if (_processManager.HasService(service.Name))
            {
                return Results.Json(new { success = false, error = $"Service '{service.Name}' already exists" }, statusCode: 409);
            }

            // Add to configuration
            var (success, error) = await _configService.AddServiceAsync(service);
            if (!success)
            {
                return Results.Json(new { success = false, error }, statusCode: 400);
            }

            // Register with process manager
            _processManager.RegisterService(service);

            return Results.Json(new
            {
                success = true,
                name = service.Name,
                message = $"Service '{service.Name}' created successfully"
            }, statusCode: 201);
        });

        // PUT /services/{name} - Update an existing service
        _app.MapPut("/services/{name}", async (string name, ServiceConfig updatedService, CancellationToken ct) =>
        {
            if (!_processManager.Services.TryGetValue(name, out var state))
            {
                return Results.Json(new { success = false, error = $"Service '{name}' not found" }, statusCode: 404);
            }

            // Preserve the name if not provided in update
            if (string.IsNullOrWhiteSpace(updatedService.Name))
            {
                updatedService.Name = name;
            }

            // Stop if running, but keep registered
            var wasRunning = state.Status == ServiceStatus.Running || state.Status == ServiceStatus.Starting;
            if (wasRunning)
            {
                await _processManager.StopServiceAsync(name, ct);
            }

            // Try config update first (before unregistering)
            var (success, error) = await _configService.UpdateServiceAsync(name, updatedService);
            if (!success)
            {
                // Restart if it was running - service still registered
                if (wasRunning)
                {
                    await _processManager.StartServiceAsync(name, ct);
                }
                return Results.Json(new { success = false, error }, statusCode: 400);
            }

            // Config succeeded - now swap the registration
            await _processManager.UnregisterServiceAsync(name, ct);
            _processManager.RegisterService(updatedService);

            // Restart with new config
            if (wasRunning)
            {
                await _processManager.StartServiceAsync(updatedService.Name, ct);
            }

            return Results.Json(new
            {
                success = true,
                name = updatedService.Name,
                message = $"Service '{name}' updated successfully",
                wasRestarted = wasRunning
            });
        });

        // DELETE /services/{name} - Delete a service
        _app.MapDelete("/services/{name}", async (string name, CancellationToken ct) =>
        {
            if (!_processManager.HasService(name))
            {
                return Results.Json(new { success = false, error = $"Service '{name}' not found" }, statusCode: 404);
            }

            // Stop and unregister from process manager
            await _processManager.UnregisterServiceAsync(name, ct);

            // Remove from configuration
            var (success, error) = await _configService.RemoveServiceAsync(name);
            if (!success)
            {
                return Results.Json(new { success = false, error }, statusCode: 400);
            }

            return Results.Json(new
            {
                success = true,
                name,
                message = $"Service '{name}' deleted successfully"
            });
        });

        // GET /services/{name}/logs - Get log content
        _app.MapGet("/services/{name}/logs", async (string name, int? tail, CancellationToken ct) =>
        {
            await CheckAndApplyConfigChangesAsync(ct);
            if (!_processManager.Services.ContainsKey(name))
            {
                return Results.Json(new { error = $"Service '{name}' not found" }, statusCode: 404);
            }

            var content = _logManager.GetLogContent(name);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            var tailCount = tail ?? 100;
            if (tailCount > 0 && lines.Length > tailCount)
            {
                lines = lines.Skip(lines.Length - tailCount).ToArray();
            }

            return Results.Json(new
            {
                name,
                lineCount = lines.Length,
                logs = lines
            }, new JsonSerializerOptions { WriteIndented = true });
        });

        // POST /services/{name}/logs/clear - Clear log for one service
        _app.MapPost("/services/{name}/logs/clear", (string name) =>
        {
            if (!_processManager.Services.ContainsKey(name))
            {
                return Results.Json(new { error = $"Service '{name}' not found" }, statusCode: 404);
            }

            _logManager.ResetLog(name);
            return Results.Json(new { success = true, name, message = "Log cleared" });
        });

        // POST /services/logs/clear - Clear logs for all services
        _app.MapPost("/services/logs/clear", () =>
        {
            var names = _processManager.Services.Keys.ToList();
            foreach (var name in names)
            {
                _logManager.ResetLog(name);
            }
            return Results.Json(new { success = true, cleared = names });
        });

        // POST /services/start - Start all services (parallel)
        _app.MapPost("/services/start", async (CancellationToken ct) =>
        {
            await CheckAndApplyConfigChangesAsync(ct);
            var names = _processManager.Services.Keys.ToList();
            var tasks = names.Select(async name =>
            {
                var (success, error) = await _processManager.StartServiceAsync(name, ct);
                var state = _processManager.Services[name];
                return new
                {
                    name,
                    success,
                    status = state.Status.ToString().ToLowerInvariant(),
                    pid = state.ProcessId,
                    error
                };
            });
            var results = await Task.WhenAll(tasks);
            return Results.Json(new { results }, new JsonSerializerOptions { WriteIndented = true });
        });

        // POST /services/stop - Stop all services (parallel)
        _app.MapPost("/services/stop", async (CancellationToken ct) =>
        {
            await CheckAndApplyConfigChangesAsync(ct);
            var names = _processManager.Services.Keys.ToList();
            var tasks = names.Select(async name =>
            {
                var (success, error) = await _processManager.StopServiceAsync(name, ct);
                var state = _processManager.Services[name];
                return new
                {
                    name,
                    success,
                    status = state.Status.ToString().ToLowerInvariant(),
                    error
                };
            });
            var results = await Task.WhenAll(tasks);
            return Results.Json(new { results }, new JsonSerializerOptions { WriteIndented = true });
        });

        // POST /services/restart - Restart all services (parallel)
        _app.MapPost("/services/restart", async (CancellationToken ct) =>
        {
            await CheckAndApplyConfigChangesAsync(ct);
            var names = _processManager.Services.Keys.ToList();
            var tasks = names.Select(async name =>
            {
                var (success, error) = await _processManager.RestartServiceAsync(name, ct);
                var state = _processManager.Services[name];
                return new
                {
                    name,
                    success,
                    status = state.Status.ToString().ToLowerInvariant(),
                    pid = state.ProcessId,
                    error
                };
            });
            var results = await Task.WhenAll(tasks);
            return Results.Json(new { results }, new JsonSerializerOptions { WriteIndented = true });
        });

        // POST /services/{name}/start
        _app.MapPost("/services/{name}/start", async (string name, CancellationToken ct) =>
        {
            await CheckAndApplyConfigChangesAsync(ct);
            var (success, error) = await _processManager.StartServiceAsync(name, ct);

            if (!_processManager.Services.TryGetValue(name, out var state))
            {
                return Results.Json(new { success = false, name, error = "Service not found" }, statusCode: 404);
            }

            if (success)
            {
                return Results.Json(new
                {
                    success = true,
                    name,
                    status = state.Status.ToString().ToLowerInvariant(),
                    pid = state.ProcessId
                });
            }
            else
            {
                return Results.Json(new
                {
                    success = false,
                    name,
                    error
                }, statusCode: 500);
            }
        });

        // POST /services/{name}/stop
        _app.MapPost("/services/{name}/stop", async (string name, CancellationToken ct) =>
        {
            await CheckAndApplyConfigChangesAsync(ct);
            var (success, error) = await _processManager.StopServiceAsync(name, ct);

            if (!_processManager.Services.TryGetValue(name, out var state))
            {
                return Results.Json(new { success = false, name, error = "Service not found" }, statusCode: 404);
            }

            if (success)
            {
                return Results.Json(new
                {
                    success = true,
                    name,
                    status = state.Status.ToString().ToLowerInvariant()
                });
            }
            else
            {
                return Results.Json(new
                {
                    success = false,
                    name,
                    error
                }, statusCode: 500);
            }
        });

        // POST /services/{name}/restart
        _app.MapPost("/services/{name}/restart", async (string name, CancellationToken ct) =>
        {
            await CheckAndApplyConfigChangesAsync(ct);
            var (success, error) = await _processManager.RestartServiceAsync(name, ct);

            if (!_processManager.Services.TryGetValue(name, out var state))
            {
                return Results.Json(new { success = false, name, error = "Service not found" }, statusCode: 404);
            }

            if (success)
            {
                return Results.Json(new
                {
                    success = true,
                    name,
                    status = state.Status.ToString().ToLowerInvariant(),
                    pid = state.ProcessId
                });
            }
            else
            {
                return Results.Json(new
                {
                    success = false,
                    name,
                    error
                }, statusCode: 500);
            }
        });

        // POST /shutdown - Shutdown the application
        _app.MapPost("/shutdown", () =>
        {
            // Trigger shutdown on a delay so we can return the response first
            Task.Run(async () =>
            {
                await Task.Delay(500);
                ShutdownRequested?.Invoke();
            });

            return Results.Json(new
            {
                success = true,
                message = "Shutdown initiated"
            });
        });

        IsRunning = true;
        _runTask = Task.Run(async () =>
        {
            try
            {
                await _app.StartAsync(_cts.Token);
                await Task.Delay(Timeout.Infinite, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        });
    }

    public async Task StopAsync()
    {
        if (_app != null && IsRunning)
        {
            IsRunning = false;
            _cts.Cancel();

            try
            {
                await _app.StopAsync();
            }
            catch
            {
                // Ignore shutdown errors
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _app?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
    }
}
