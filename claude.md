# ServiceHost

Windows WPF application that manages services with an HTTP API for AI assistants.

## Rules

- **Do not push to git** until the user explicitly says to commit/push

## Build Commands

```powershell
.\dev.ps1      # Build and run
.\publish.ps1  # Create publish/ServiceHost.exe
```

## Project Structure

```
src/
├── App.xaml(.cs)           # Application startup, dependency wiring
├── MainWindow.xaml(.cs)    # WPF UI, dark theme
├── Models/
│   ├── ServiceConfig.cs    # JSON config model
│   └── ServiceState.cs     # Runtime state (MVVM observable)
├── Services/
│   ├── ConfigurationService.cs  # Loads ServiceHost.json
│   ├── ProcessManager.cs        # Start/stop/monitor processes
│   ├── LogManager.cs            # Log file management
│   └── VersionChecker.cs        # GitHub release update checking
├── Api/
│   └── ApiHost.cs          # HTTP API (ASP.NET Core minimal API)
└── ViewModels/
    └── MainViewModel.cs    # MVVM bindings
```

## HTTP API (localhost:9500)

```
GET    /                           → API manifest + service status + update info
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
POST   /shutdown                   → Shutdown application (for updates)
```

## Configuration (ServiceHost.json)

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
      "url": "http://localhost:5000/health",
      "environment": {}
    }
  ]
}
```

Config auto-reloads on change - no restart needed.

## Key Behaviors

- **Shell Mode**: When command is `cmd` with `/c` as first arg, remaining args are auto-joined into a single command string for correct PATH propagation
- **Persistence**: Services keep running when UI closes
- **Logs**: Truncated on start/restart, timestamped, accessible via API
- **Stop**: Graceful shutdown first, then force kill after timeout
- **Update Check**: Queries GitHub releases, shows update section in API when new version available
- **File Locking**: Config file access is serialized to prevent corruption
- **Name Validation**: Service names validated against invalid filename chars and Windows reserved names
