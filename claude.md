# ServiceHost

Windows WPF application that manages services with an HTTP API for Claude Code.

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
│   └── ReadinessChecker.cs      # Port/pattern readiness
├── Api/
│   └── ApiHost.cs          # HTTP API (ASP.NET Core minimal API)
└── ViewModels/
    └── MainViewModel.cs    # MVVM bindings
```

## HTTP API (localhost:9500)

```
GET  /                          → API manifest + service status
GET  /services                  → List services
GET  /services/{name}/logs      → Get logs (?tail=N)
POST /services/start            → Start all
POST /services/stop             → Stop all
POST /services/restart          → Restart all
POST /services/{name}/start     → Start one
POST /services/{name}/stop      → Stop one
POST /services/{name}/restart   → Restart one
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
      "port": 5000,
      "readyPattern": null,
      "environment": {}
    }
  ]
}
```

## Key Behaviors

- **Readiness**: Blocks until port accepts connections or pattern matches stdout
- **Persistence**: Services keep running when UI closes
- **Detection**: On startup, detects already-running services by checking ports
- **Logs**: Truncated on start/restart, timestamped, accessible via API
- **Stop**: Graceful shutdown first, then force kill after timeout
