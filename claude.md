# ServiceHost

A Windows WPF application that hosts multiple services with an HTTP API for Claude Code interaction.

## Quick Start

```bash
cd src
dotnet build
dotnet run
```

## Build & Publish

```bash
cd src
dotnet publish -c Release -r win-x64
```

Output: `src/bin/Release/net8.0-windows/win-x64/publish/ServiceHost.exe`

## Configuration

Create `ServiceHost.json` in the working directory:

```json
{
  "apiPort": 9500,
  "logDirectory": "./logs",
  "services": [
    {
      "name": "myservice",
      "command": "dotnet",
      "args": ["run"],
      "workingDirectory": "./myproject",
      "port": 5000,
      "readyPattern": null,
      "environment": {}
    }
  ]
}
```

## HTTP API (localhost:9500)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/services` | GET | List all services with status |
| `/services/{name}/start` | POST | Start service (blocks until ready) |
| `/services/{name}/stop` | POST | Stop service (blocks until stopped) |
| `/services/{name}/restart` | POST | Restart service (blocks until ready) |

### Response Format

```json
// Success
{ "success": true, "name": "api", "status": "running", "pid": 12345 }

// Failure
{ "success": false, "name": "api", "error": "Timeout waiting for port 5000" }

// GET /services
{
  "services": [
    { "name": "api", "status": "running", "pid": 12345, "port": 5000 }
  ]
}
```

## Architecture

```
ServiceHost.exe
├── HTTP API (localhost:9500)
├── WPF UI (service list, controls, log viewer)
└── Process Manager (spawn/kill, stdout/stderr capture, logs)
```

## Key Files

- `src/Models/ServiceConfig.cs` - Configuration model
- `src/Models/ServiceState.cs` - Runtime state
- `src/Services/ProcessManager.cs` - Process lifecycle management
- `src/Services/LogManager.cs` - Log file management
- `src/Services/ReadinessChecker.cs` - Port/pattern readiness detection
- `src/Api/ApiHost.cs` - HTTP API endpoints
- `src/ViewModels/MainViewModel.cs` - WPF MVVM binding

## Logs

Logs are written to `logs/{servicename}.log` with timestamps. Log files are truncated on service start/restart.
