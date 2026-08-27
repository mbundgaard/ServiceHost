# ServiceHost

Windows service manager with WPF and TUI front-ends plus a shared HTTP API for AI assistants.

## Agent Rules

- **Do not push to git** until the user explicitly says to push.
- Commit only when the user asks for a commit or clearly approves it.
- Keep the HTTP API as the stable agent-facing control surface; do not remove or bypass it.
- Prefer small, buildable migration steps.
- When using Herdr, run the ServiceHost TUI in the `ServiceHost shell` pane and use API calls from agent panes for control/testing.
- Do not run WPF and TUI at the same time with the same `apiPort` unless intentionally testing port-conflict behavior.

## Current Workflow

This project is migrating from a WPF-only app to shared core + multiple front-ends:

```text
src/
├── ServiceHost.sln
├── ServiceHost.Core/            # Shared runtime used by WPF and TUI
│   ├── ServiceHostRuntime.cs    # Dependency wiring/startup/shutdown
│   ├── Models/                  # JSON config + runtime state
│   ├── Services/                # Config, process, logs, updates, relaunch
│   └── Api/ApiHost.cs           # HTTP API (ASP.NET Core minimal API)
├── ServiceHost.Wpf/             # Existing Windows WPF front-end
│   ├── App.xaml(.cs)
│   ├── MainWindow.xaml(.cs)
│   └── ViewModels/MainViewModel.cs
└── ServiceHost.Tui/             # Terminal.Gui front-end for Herdr/terminal use
    └── Program.cs
```

## Build Commands

```powershell
# Build everything
 dotnet build .\src\ServiceHost.sln

# Run front-ends; use one at a time on the default port
.\dev.ps1          # Build and run WPF UI
.\dev-tui.ps1      # Build and run TUI

# Publish
.\publish.ps1      # Create publish/ServiceHost.exe
.\publish-tui.ps1  # Create publish-tui/ServiceHost.Tui.exe
```

## TUI Notes

The TUI uses a stacked layout optimized for Herdr panes:

```text
header
services list
selected-service logs
status bar
```

Keyboard controls:

```text
F2      Start/stop selected service
F3      Restart selected service
F4      Start/stop all services
Ctrl+L  Clear selected log
Ctrl+Q  Quit UI; child services keep running
```

Important: `ServiceHost.Tui` must remain a real console app. The project uses:

```xml
<DisableWinExeOutputInference>true</DisableWinExeOutputInference>
```

Do not remove this, or the `.exe` may detach from the terminal instead of rendering in the pane.

## HTTP API (localhost:9500)

The API lives in `ServiceHost.Core/Api/ApiHost.cs` and is shared by WPF and TUI.

```text
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

Agents should discover current state with:

```bash
curl http://localhost:9500/
curl http://localhost:9500/services
```

## Configuration (ServiceHost.json)

Auto-created with example on first run. Auto-reloads on API access when changed.

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

- **Port**: Required. Identifies the process bound to the port; used to adopt running instances on startup and kill conflicting processes before start.
- **Shell Mode**: When command is `cmd` with `/c` as first arg, remaining args are auto-joined into a single command string for correct PATH propagation.
- **Process Adoption**: On startup, services with a port already in use are adopted as running.
- **Persistence**: Services keep running when WPF/TUI closes; only the UI/API host exits.
- **Stop**: Kills the entire process tree directly.
- **Logs**: Truncated on start/restart, timestamped, accessible via API and TUI.
- **Self-Update**: Shared update/relaunch logic lives in Core; WPF currently owns update polling/status UX.
- **File Locking**: Config file access is serialized to prevent corruption.
- **Name Validation**: Service names validated against invalid filename chars and Windows reserved names.
- **Crash Handling**: WPF has dialog crash reporting; TUI should prefer terminal-safe error reporting.

## Releases

Automated via GitHub Actions. Every push to `master` triggers `.github/workflows/release.yml`, auto-increments version from git tags, publishes both WPF and TUI executables, and creates a GitHub release.
