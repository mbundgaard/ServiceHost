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
    private readonly string _configPath;
    private WebApplication? _app;
    private Task? _runTask;
    private readonly CancellationTokenSource _cts = new();

    public bool IsRunning { get; private set; }

    public ApiHost(int port, ProcessManager processManager, LogManager logManager, ConfigurationService configService)
    {
        _port = port;
        _processManager = processManager;
        _logManager = logManager;
        _configService = configService;
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

            var manifest = new
            {
                name = "ServiceHost",
                version = "1.0.0",
                description = "Service manager with HTTP API for Claude Code",
                configPath = _configPath,
                configuration = new
                {
                    note = "To add or remove services, edit the config file. Changes are auto-detected on next API request.",
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
                    ["GET /services/{name}/logs?tail=N"] = "Get last N lines of logs (default 100)",
                    ["POST /services/start"] = "Start all services",
                    ["POST /services/stop"] = "Stop all services",
                    ["POST /services/restart"] = "Restart all services",
                    ["POST /services/{name}/start"] = "Start a service (blocks until ready)",
                    ["POST /services/{name}/stop"] = "Stop a service (blocks until stopped)",
                    ["POST /services/{name}/restart"] = "Restart a service"
                },
                examples = new Dictionary<string, string>
                {
                    ["start_one"] = $"curl -X POST http://localhost:{_port}/services/api/start",
                    ["stop_one"] = $"curl -X POST http://localhost:{_port}/services/api/stop",
                    ["start_all"] = $"curl -X POST http://localhost:{_port}/services/start",
                    ["get_logs"] = $"curl http://localhost:{_port}/services/api/logs?tail=50"
                },
                services
            };

            return Results.Json(manifest, new JsonSerializerOptions { WriteIndented = true });
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
