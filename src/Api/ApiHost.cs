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
    private WebApplication? _app;
    private Task? _runTask;
    private readonly CancellationTokenSource _cts = new();

    public bool IsRunning { get; private set; }

    public ApiHost(int port, ProcessManager processManager)
    {
        _port = port;
        _processManager = processManager;
    }

    public void Start()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://localhost:{_port}");

        // Disable default logging to console
        builder.Logging.ClearProviders();

        _app = builder.Build();

        // GET /services - List all services
        _app.MapGet("/services", () =>
        {
            var services = _processManager.Services.Values.Select(s => new
            {
                name = s.Config.Name,
                status = s.Status.ToString().ToLowerInvariant(),
                pid = s.ProcessId,
                port = s.Config.Port,
                startedAt = s.StartedAt?.ToString("o"),
                error = s.LastError
            });

            return Results.Json(new { services }, new JsonSerializerOptions { WriteIndented = true });
        });

        // POST /services/{name}/start
        _app.MapPost("/services/{name}/start", async (string name, CancellationToken ct) =>
        {
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
                // Wait until cancellation is requested
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
