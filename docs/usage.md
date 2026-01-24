# ServiceHost Usage Guide

## Overview

ServiceHost is a Windows application that manages multiple services (processes) with:
- A graphical UI for manual control
- An HTTP API for programmatic control (ideal for Claude Code)
- Automatic log capture and file writing

## Installation

1. Download or build `ServiceHost.exe`
2. Create a `ServiceHost.json` configuration file
3. Run `ServiceHost.exe`

## Configuration File

Create `ServiceHost.json` in the same directory as the executable:

```json
{
  "apiPort": 9500,
  "logDirectory": "./logs",
  "services": [
    {
      "name": "api",
      "command": "dotnet",
      "args": ["run", "--project", "src/Api"],
      "workingDirectory": "C:/Projects/MyApp",
      "port": 5000,
      "readyPattern": null,
      "environment": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    },
    {
      "name": "worker",
      "command": "node",
      "args": ["worker.js"],
      "workingDirectory": "C:/Projects/MyApp/worker",
      "port": null,
      "readyPattern": "Worker started successfully"
    }
  ]
}
```

### Configuration Options

| Field | Description |
|-------|-------------|
| `apiPort` | HTTP API port (default: 9500) |
| `logDirectory` | Directory for log files (default: ./logs) |
| `services` | Array of service definitions |

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

## Readiness Detection

ServiceHost blocks start requests until the service is ready:

1. **Port-based**: If `port` is specified, waits until TCP connection succeeds
2. **Pattern-based**: If `readyPattern` is specified, waits until pattern appears in stdout
3. **Timeout**: Fails after 30 seconds if neither condition is met

## Using the UI

### Service List
- Green circle: Service is running
- Gray circle: Service is stopped
- Red circle: Service failed/error

### Buttons
- **▶** Start the service
- **■** Stop the service
- **↻** Restart the service
- **Start All** / **Stop All**: Batch operations

### Log Viewer
- Click a service to view its logs
- Logs are timestamped and captured in real-time
- Log files are truncated on start/restart

## HTTP API

### List Services
```bash
curl http://localhost:9500/services
```

### Start Service
```bash
curl -X POST http://localhost:9500/services/api/start
```

### Stop Service
```bash
curl -X POST http://localhost:9500/services/api/stop
```

### Restart Service
```bash
curl -X POST http://localhost:9500/services/api/restart
```

### Response Format

Success:
```json
{
  "success": true,
  "name": "api",
  "status": "running",
  "pid": 12345
}
```

Failure:
```json
{
  "success": false,
  "name": "api",
  "error": "Timeout waiting for port 5000"
}
```

## Log Files

Logs are stored in the configured `logDirectory`:
- One file per service: `{name}.log`
- Each line is timestamped: `[2024-01-15 10:23:45] content`
- Files are truncated when a service starts or restarts

## Claude Code Integration

ServiceHost is designed for use with Claude Code:

1. Start ServiceHost with your project's services configured
2. Claude Code can use the HTTP API to start/stop services
3. Logs are accessible via file system at `logs/{name}.log`

Example workflow:
```bash
# Start the API service
curl -X POST http://localhost:9500/services/api/start

# Check logs
cat logs/api.log

# Restart after code changes
curl -X POST http://localhost:9500/services/api/restart
```

## Troubleshooting

### Service won't start
- Check the command and arguments in configuration
- Verify the working directory exists
- Check logs for error messages

### API not responding
- Verify the apiPort is not in use
- Check Windows Firewall settings

### Process not stopping
- ServiceHost sends graceful shutdown first, then force-kills after 5 seconds
- For stubborn processes, the entire process tree is terminated
