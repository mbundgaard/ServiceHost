# ServiceHost

Windows WPF application that manages services with an HTTP API for AI assistants.

## Rules

- **Do not push to git** until the user explicitly says to commit/push

## Build Commands

```powershell
.\dev.ps1          # Build and run WPF UI
.\dev-tui.ps1      # Build and run TUI
.\publish.ps1      # Create publish/ServiceHost.exe
.\publish-tui.ps1  # Create publish-tui/ServiceHost.Tui.exe
```

## Releases

Automated via GitHub Actions. Every push to `master` triggers `.github/workflows/release.yml` which auto-increments the version from git tags, publishes a single-file exe, and creates a GitHub release.

## Project Structure

```
src/
├── ServiceHost.sln
├── ServiceHost.Core/            # Shared runtime used by WPF and TUI
│   ├── ServiceHostRuntime.cs    # Dependency wiring/startup/shutdown
│   ├── Models/                  # JSON config + runtime state
│   ├── Services/                # Config, process, logs, updates, relaunch
│   └── Api/ApiHost.cs           # HTTP API (ASP.NET Core minimal API)
├── ServiceHost.Wpf/             # Windows WPF front-end
│   ├── App.xaml(.cs)
│   ├── MainWindow.xaml(.cs)
│   └── ViewModels/MainViewModel.cs
└── ServiceHost.Tui/             # Terminal.Gui front-end
    └── Program.cs
```

## HTTP API (localhost:9500)

```
GET    /                           → API manifest + service status
GET    /services                   → List services
POST   /services                   → Create service (JSON body)
PUT    /services/{name}            → Update service (JSON body)
DELETE /services/{name}            → Delete service
GET    /services/{name}/logs       → Get logs (?tail=N)
POST   /services/logs/clear        → Clear all logs
POST   /services/{name}/logs/clear → Clear one log
POST   /services/start             → Start all (parallel)
POST   /services/stop              → Stop all (parallel)
POST   /services/restart           → Restart all (parallel)
POST   /services/{name}/start      → Start one
POST   /services/{name}/stop       → Stop one
POST   /services/{name}/restart    → Restart one
POST   /shutdown                   → Shutdown application
```

## Configuration (ServiceHost.json)

Auto-created with example on first run. Auto-reloads on change — no restart needed.

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
      "url": "http://localhost:5000/health",
      "environment": {}
    }
  ]
}
```

Required fields: `name`, `command`, `port`.

## Key Behaviors

- **Port**: Required. Identifies the process bound to the port — used to adopt running instances on startup and kill conflicting processes before start
- **Shell Mode**: When command is `cmd` with `/c` as first arg, remaining args are auto-joined into a single command string for correct PATH propagation
- **Process Adoption**: On startup, services with a port already in use are adopted as running (no orphaned processes)
- **Persistence**: Services keep running when UI closes
- **Stop**: Kills the entire process tree directly
- **Logs**: Truncated on start/restart, timestamped, accessible via API
- **Self-Update**: Polls GitHub releases hourly and downloads new builds to `%TEMP%` in the background. The status-bar version label becomes a clickable nudge to install + restart; any pending download is auto-applied on next startup. Child services persist and are re-adopted across the restart
- **File Locking**: Config file access is serialized to prevent corruption
- **Name Validation**: Service names validated against invalid filename chars and Windows reserved names
- **Crash Handling**: Unhandled exceptions are caught, logged to `_crash`, and shown in a dialog
