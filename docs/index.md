# ServiceHost

Windows service manager with HTTP API for AI assistants.

## Quick Start

```powershell
# Download latest release
Invoke-WebRequest -Uri "https://github.com/mbundgaard/ServiceHost/releases/latest/download/ServiceHost.exe" -OutFile "ServiceHost.exe"

# Unblock (Windows blocks downloaded executables)
Unblock-File .\ServiceHost.exe

# Run (starts API on localhost:9500)
.\ServiceHost.exe
```

## Get Full API Documentation

Once running, query the discovery endpoint:

```
GET http://localhost:9500/
```

Returns full API schema, available endpoints, service status, and configuration examples.

## What It Does

- Manages background services (start/stop/restart via API)
- Monitors readiness via port or stdout patterns
- Serves logs via API
- Config auto-reloads on change (no restart needed)
- Services persist when UI closes

## Configuration

Create `ServiceHost.json` next to the executable:

```json
{
  "apiPort": 9500,
  "logDirectory": "./logs",
  "services": [
    {
      "name": "my-api",
      "command": "dotnet",
      "args": ["run"],
      "workingDirectory": "./my-api",
      "port": 5000
    }
  ]
}
```

## Source

https://github.com/mbundgaard/ServiceHost
