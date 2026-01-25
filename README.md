# ServiceHost

A Windows WPF application that manages multiple services with an HTTP API designed for AI assistant integration.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Windows](https://img.shields.io/badge/Platform-Windows-0078D6)
![License](https://img.shields.io/badge/License-MIT-green)

![ServiceHost UI](MainView.png)

## Why ServiceHost?

When AI assistants spawn services directly, processes can linger after the session ends, leaving ports blocked and requiring manual cleanup. ServiceHost solves this by providing a dedicated service manager that:

- Gives you visibility and control over running services
- Persists across AI assistant sessions
- Provides clean start/stop/restart operations
- Detects already-running services on startup

Typical use cases:

- **Backend API** - ASP.NET Core, Express, FastAPI, Go servers
- **Frontend dev server** - Vite, Webpack, Next.js, Angular CLI
- **Database** - Local PostgreSQL, Redis, MongoDB instances
- **Workers** - Background job processors, queue consumers
- **Sidecars** - Auth proxies, mock servers, local S3/Azure emulators

## Features

- **WPF UI** - Dark themed interface with service list, controls, and log viewer
- **HTTP API** - Self-describing REST API on localhost:9500
- **Process Management** - Start, stop, restart services with stdout/stderr capture
- **Readiness Detection** - Port-based or pattern-based startup detection
- **Persistent Services** - Services keep running when UI closes
- **Auto-Reload Config** - Edit `ServiceHost.json` and changes are picked up on next API request
- **Service CRUD via API** - Create, update, delete services via REST endpoints
- **Auto-Update Check** - Checks GitHub releases for new versions, includes update instructions in API
- **Copy Prompt** - One-click button copies an AI assistant prompt to your clipboard
- **AI Ready** - API returns self-describing manifest for AI assistants

## Quick Start

```powershell
# Clone and build
git clone https://github.com/mbundgaard/ServiceHost.git
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
| `url` | Clickable URL shown in UI (e.g., health endpoint or main page) |
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
  "version": "4",
  "description": "Service manager with HTTP API for AI assistants",
  "update": {
    "currentVersion": "3",
    "newVersion": "4",
    "downloadUrl": "https://github.com/mbundgaard/ServiceHost/releases/latest/download/ServiceHost.exe",
    "exePath": "C:/path/to/ServiceHost.exe",
    "processId": 12345,
    "instructions": "To update: 1) Download from downloadUrl to exePath.tmp, 2) POST /shutdown, 3) Wait for processId to exit, 4) Delete exePath, 5) Rename exePath.tmp to exePath, 6) Start exePath"
  },
  "endpoints": {
    "GET /": "API description and service status",
    "GET /services": "List all services",
    "POST /services": "Create a new service",
    "PUT /services/{name}": "Update an existing service",
    "DELETE /services/{name}": "Delete a service",
    "GET /services/{name}/logs?tail=N": "Get last N lines of logs",
    "POST /services/start": "Start all services",
    "POST /services/stop": "Stop all services",
    "POST /services/restart": "Restart all services",
    "POST /services/{name}/start": "Start a service",
    "POST /services/{name}/stop": "Stop a service",
    "POST /services/{name}/restart": "Restart a service",
    "POST /shutdown": "Shutdown the application"
  },
  "services": [...]
}
```

The `update` section only appears when a new version is available. It includes step-by-step instructions for AI assistants to perform the update automatically.

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

# Create a new service
curl -X POST http://localhost:9500/services \
  -H "Content-Type: application/json" \
  -d '{"name":"worker","command":"node","args":["worker.js"],"port":3001}'

# Update a service
curl -X PUT http://localhost:9500/services/worker \
  -H "Content-Type: application/json" \
  -d '{"name":"worker","command":"node","args":["worker-v2.js"],"port":3001}'

# Delete a service
curl -X DELETE http://localhost:9500/services/worker

# Shutdown application (for updates)
curl -X POST http://localhost:9500/shutdown
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
- **Copy Prompt** - Copies an AI assistant prompt to clipboard for quick integration

Status indicators:
- Green: Running
- Gray: Stopped
- Orange: Starting/Stopping
- Red: Failed

## AI Assistant Integration

ServiceHost is designed to work with AI coding assistants (Claude Code, Cursor, Windsurf, etc.):

1. Start ServiceHost with your project's services configured
2. Click **Copy Prompt** to copy a ready-to-use prompt to your clipboard
3. Paste the prompt into your AI assistant - it will discover the API via curl
4. The AI assistant can start/stop/restart services and fetch logs as needed

The API manifest includes configuration details, so AI assistants know how to add or modify services in `ServiceHost.json`. Changes are auto-detected on the next API request.

Services persist when the UI closes, so you can:
- Start services via the UI
- Close ServiceHost
- Let the AI assistant manage services via the API
- Reopen ServiceHost to see current status

## Releases & Updates

Download the latest release from [GitHub Releases](https://github.com/mbundgaard/ServiceHost/releases/latest/download/ServiceHost.exe).

ServiceHost automatically checks for updates. When a new version is available, the API includes an `update` section with:
- Current and new version numbers
- Download URL
- Executable path and process ID
- Step-by-step instructions for AI assistants to perform the update

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
