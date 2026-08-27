# ServiceHost

Windows service manager with WPF and TUI front-ends plus an HTTP API for AI assistants.

## Why TUI?

ServiceHost originally shipped as a WPF app. The TUI was added so humans can run and monitor services directly inside terminal workspaces such as Herdr while agents continue to use the HTTP API. The API remains the stable control surface; WPF and TUI are just two front-ends over the same core runtime.

The preferred long-term usage model is one installed ServiceHost copy on PATH, shared by all projects. Each project checks in only its `ServiceHost.json` and documents its ServiceHost usage in `AGENTS.md`.

## Quick Start

```powershell
# Download latest WPF release
Invoke-WebRequest -Uri "https://github.com/mbundgaard/ServiceHost/releases/latest/download/ServiceHost.exe" -OutFile "ServiceHost.exe"
Unblock-File .\ServiceHost.exe
.\ServiceHost.exe

# Download latest TUI release
Invoke-WebRequest -Uri "https://github.com/mbundgaard/ServiceHost/releases/latest/download/ServiceHost.Tui.exe" -OutFile "ServiceHost.Tui.exe"
Unblock-File .\ServiceHost.Tui.exe
.\ServiceHost.Tui.exe
```

## Project Usage

Recommended shared install location:

```text
%LOCALAPPDATA%\Programs\ServiceHost\
```

Add that folder to PATH, then projects can run:

```powershell
servicehost-tui --config .\ServiceHost.json
```

## Multiple Projects / Port Override

By default the API uses `apiPort` from `ServiceHost.json`, falling back to `9500`. When multiple projects need ServiceHost at the same time, pass a runtime-only port override:

```powershell
servicehost-tui --config D:\Source\ProjectA\ServiceHost.json --port 9501
servicehost-tui --config D:\Source\ProjectB\ServiceHost.json --port 9502
```

The `--port` value only affects the running API listener. It does not rewrite the checked-in config file.

Resolution order:

1. `--port <port>`
2. `SERVICEHOST_PORT`
3. `apiPort` in `ServiceHost.json`
4. default `9500`

## Get Full API Documentation

Once running, query the discovery endpoint:

```text
GET http://localhost:9500/
```

The manifest includes endpoints, service status, examples, and identity fields agents can use to verify the correct instance:

```json
{
  "apiPort": 9500,
  "configPath": "D:/Source/Project/ServiceHost.json",
  "projectDirectory": "D:/Source/Project"
}
```

## What It Does

- Manages background services (start/stop/restart via API)
- Serves logs via API and TUI
- Config auto-reloads on API access when changed
- Services persist when WPF/TUI closes
- Supports one installed executable serving many project configs via `--config`
- Supports multiple simultaneous project instances via `--port`

## Configuration

Create `ServiceHost.json` in the project root or next to the executable:

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

Required service fields: `name`, `command`, `port`.

Relative `logDirectory` and service `workingDirectory` values are resolved relative to the config file directory.

## Source

https://github.com/mbundgaard/ServiceHost
