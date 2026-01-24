# ServiceHost

A Windows WPF application that manages multiple services with an HTTP API designed for Claude Code integration.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Windows](https://img.shields.io/badge/Platform-Windows-0078D6)
![License](https://img.shields.io/badge/License-MIT-green)

## Features

- **WPF UI** - Dark themed interface with service list, controls, and log viewer
- **HTTP API** - Self-describing REST API on localhost:9500
- **Process Management** - Start, stop, restart services with stdout/stderr capture
- **Readiness Detection** - Port-based or pattern-based startup detection
- **Persistent Services** - Services keep running when UI closes
- **Claude Code Ready** - API returns manifest describing all available operations

## Quick Start

```powershell
# Clone and build
git clone https://github.com/yourusername/ServiceHost.git
cd ServiceHost
.\dev.ps1
```

Or publish a single-file executable:

```powershell
.\publish.ps1
# Output: publish/ServiceHost.exe
```

## Configuration

Create `ServiceHost.json` next to the executable:

```json
{
  "apiPort": 9500,
  "logDirectory": "./logs",
  "services": [
    {
      "name": "api",
      "command": "dotnet",
      "args": ["run"],
      "workingDirectory": "./api",
      "port": 5000,
      "environment": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    },
    {
      "name": "frontend",
      "command": "npm",
      "args": ["run", "dev"],
      "workingDirectory": "./app",
      "port": 5173
    }
  ]
}
```

### Service Options

| Field | Description |
|-------|-------------|
| `name` | Unique service identifier |
| `command` | Executable to run |
| `args` | Command-line arguments (array) |
| `workingDirectory` | Working directory for the process |
| `port` | TCP port to check for readiness (optional) |
| `readyPattern` | Regex pattern in stdout indicating readiness (optional) |
| `environment` | Environment variables (optional) |
| `startupTimeoutSeconds` | Readiness timeout (default: 30) |
| `shutdownTimeoutSeconds` | Graceful shutdown timeout (default: 5) |

## HTTP API

### Discovery Endpoint

```bash
curl http://localhost:9500/
```

Returns a self-describing manifest with all endpoints, examples, and current service status:

```json
{
  "name": "ServiceHost",
  "version": "1.0.0",
  "description": "Service manager with HTTP API for Claude Code",
  "configPath": "C:/path/to/ServiceHost.json",
  "endpoints": {
    "GET /": "API description and service status",
    "GET /services": "List all services",
    "GET /services/{name}/logs?tail=N": "Get last N lines of logs",
    "POST /services/start": "Start all services",
    "POST /services/stop": "Stop all services",
    "POST /services/restart": "Restart all services",
    "POST /services/{name}/start": "Start a service",
    "POST /services/{name}/stop": "Stop a service",
    "POST /services/{name}/restart": "Restart a service"
  },
  "services": [
    {
      "name": "api",
      "status": "running",
      "port": 5000,
      "pid": 12345,
      "command": "dotnet run",
      "workingDirectory": "./api"
    }
  ]
}
```

### Examples

```bash
# Start all services
curl -X POST http://localhost:9500/services/start

# Start a specific service
curl -X POST http://localhost:9500/services/api/start

# Stop a service
curl -X POST http://localhost:9500/services/api/stop

# Restart a service
curl -X POST http://localhost:9500/services/api/restart

# Get logs (last 50 lines)
curl http://localhost:9500/services/api/logs?tail=50
```

### Response Format

```json
// Success
{ "success": true, "name": "api", "status": "running", "pid": 12345 }

// Failure
{ "success": false, "name": "api", "error": "Timeout waiting for port 5000" }
```

## UI

The dark-themed UI provides:

- **Service List** - Status indicator, name, and start/stop/restart buttons
- **Log Viewer** - Real-time log display for selected service
- **Batch Controls** - Start All / Stop All buttons

Status indicators:
- Green: Running
- Gray: Stopped
- Orange: Starting/Stopping
- Red: Failed

## Claude Code Integration

ServiceHost is designed to work with Claude Code:

1. Start ServiceHost with your project's services configured
2. Claude Code fetches `http://localhost:9500/` to discover the API
3. Claude Code can start/stop/restart services and fetch logs as needed

Services persist when the UI closes, so you can:
- Start services via the UI
- Close ServiceHost
- Let Claude Code manage services via the API
- Reopen ServiceHost to see current status

## Building

Requirements:
- .NET 8.0 SDK
- Windows (WPF)

```powershell
# Development
.\dev.ps1

# Publish single-file exe
.\publish.ps1
```

## License

MIT
