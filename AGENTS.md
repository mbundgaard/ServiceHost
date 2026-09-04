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
F1      Show info popup
F2      Next service
F3      Start/stop selected service
F4      Restart selected service
F5      Start/stop all services
F6      Show/hide services list
Ctrl+L  Clear selected log
Ctrl+Q  Quit UI; child services keep running
```

Important: `ServiceHost.Tui` must remain a real console app. The project uses:

```xml
<DisableWinExeOutputInference>true</DisableWinExeOutputInference>
```

Do not remove this, or the `.exe` may detach from the terminal instead of rendering in the pane.

## Future Usage Model for Projects

Target model: ServiceHost is installed once, and each project checks in only its own `ServiceHost.json` plus instructions in that project's `AGENTS.md`. Avoid copying `ServiceHost.exe` into every repository.

Current canonical local install location:

```text
%LOCALAPPDATA%\Programs\ServiceHost
```

On this machine that expands to:

```text
C:\Users\martin\AppData\Local\Programs\ServiceHost
```

The source root (`D:\Source\ServiceHost`) should not contain runtime exe copies; local root copies were removed to avoid PATH/version confusion.

The `%LOCALAPPDATA%\Programs\ServiceHost` install location is registered in the **User PATH**, but existing terminals/panes may not see PATH changes until restarted. In a fresh terminal these commands should work:

```powershell
servicehost
servicehost-tui
```

In an already-open terminal, either use the full path or temporarily refresh PATH:

```powershell
$env:Path += ";$env:LOCALAPPDATA\Programs\ServiceHost"
$env:LOCALAPPDATA\Programs\ServiceHost\servicehost-tui.cmd --config .\ServiceHost.json
```

Preferred project contract:

```text
<Project>/ServiceHost.json   # checked in
<Project>/AGENTS.md          # documents ServiceHost usage for agents
```

Preferred startup command:

```powershell
servicehost-tui --config .\ServiceHost.json
# or explicit installed path:
D:\Tools\ServiceHost\ServiceHost.Tui.exe --config .\ServiceHost.json
```

Multiple projects can run concurrently by overriding the API port at runtime:

```powershell
servicehost-tui --config D:\Source\ProjectA\ServiceHost.json --port 9501
servicehost-tui --config D:\Source\ProjectB\ServiceHost.json --port 9502
```

After ServiceHost is running, agents should control services via the HTTP API, not by launching raw project services directly:

```bash
curl http://localhost:9500/
curl http://localhost:9500/services
curl -X POST http://localhost:9500/services/start
curl -X POST http://localhost:9500/services/<name>/restart
curl http://localhost:9500/services/<name>/logs?tail=100
```

Recommended `AGENTS.md` snippet for other projects:

````md
## ServiceHost

This project uses ServiceHost for local dev services.

Config: `./ServiceHost.json`

Start the ServiceHost TUI from the project root:

```powershell
servicehost-tui --config .\ServiceHost.json
# If port 9500 is already in use, choose a project-specific port:
servicehost-tui --config .\ServiceHost.json --port 9510
```

After it is running, agents should use the configured API URL, usually `http://localhost:9500/`, to start/stop/restart services and read logs. If `--port` was used, use that port. Do not start project services directly unless ServiceHost is unavailable.
````

Implementation notes for ServiceHost:

- `--config <path>` is supported by both WPF and TUI.
- `--port <port>` is supported as a runtime-only API port override and does not rewrite `ServiceHost.json`.
- Config path resolution prefers: `--config`, then `SERVICEHOST_CONFIG`, then `./ServiceHost.json` in current working directory, then executable directory for backwards compatibility.
- Credentials path resolution prefers: `--credentials`, then `SERVICEHOST_CREDENTIALS`, then `ServiceHost.credentials.json` next to the resolved config file.
- API port resolution prefers: `--port`, then `SERVICEHOST_PORT`, then `apiPort` in config, then default `9500`.
- API manifest exposes the resolved `apiPort`, `configPath`, and `projectDirectory` so agents can verify they are talking to the correct ServiceHost instance.
- Multiple projects can run concurrently by using different API ports.

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
POST   /credentials/session        → Upload session-only credentials (memory only; values are never returned)
DELETE /credentials/session        → Clear session-only credentials from memory
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

For services that use `${NAME}` placeholders, agents should inspect `credentials` in `GET /` and confirm `credentials.allResolved == true` before starting those services. If credentials are provided out-of-band for this session only, upload them with `POST /credentials/session` and re-check the manifest.

## Current Implementation Status

Latest pushed release on `master`: `v18` from commit `5daeff8`.

Credentials separation is implemented locally but, at the time this section was written, not yet committed/pushed. Current local changes include:

- `--credentials <path>` and `SERVICEHOST_CREDENTIALS` support.
- Project-local default credentials file: `ServiceHost.credentials.json` next to the resolved config file.
- Flat credentials JSON loading.
- `${NAME}` placeholder scanning and runtime-only in-memory resolution.
- Safe HTTP API credential status: placeholder names and resolved/unresolved booleans only.
- Session-only credential upload endpoints: `POST /credentials/session` and `DELETE /credentials/session`. Values stay in memory, are never returned, and are cleared on shutdown.
- TUI credential summary in the header: `creds:none`, `creds:ok`, or `creds:missing:N`.
- Fail-fast service start for unresolved credentials, listing placeholder names only.
- `.gitignore` entry for `ServiceHost.credentials.json`.

Before continuing from a new session, run:

```powershell
git status --short
dotnet build .\src\ServiceHost.sln
```

## Config/Credentials Separation

Permanent design requirement: project configs may be checked in, credentials must not be checked in.

Projects should check in non-secret service config only:

```text
<Project>/ServiceHost.json
```

Real credentials should live outside source control in a project-local gitignored file:

```text
<Project>/ServiceHost.credentials.json
```

Checked-in config may use placeholders such as:

```json
{
  "environment": {
    "ConnectionStrings__Default": "${MUNERIS_KIOSK_DB}",
    "TransactionApi__BaseUrl": "${MUNERIS_KIOSK_TRANSACTION_URL}"
  }
}
```

Local credentials map placeholder names to real values:

```json
{
  "MUNERIS_KIOSK_DB": "Server=...;Password=...",
  "MUNERIS_KIOSK_TRANSACTION_URL": "https://..."
}
```

Target commands:

```powershell
servicehost-tui --config .\ServiceHost.json --credentials .\ServiceHost.credentials.json
```

Credential rules:

- Resolve `${NAME}` placeholders in memory at runtime only.
- Never rewrite `ServiceHost.json` with resolved values.
- Never expose credential values in logs, TUI, or HTTP API.
- API/TUI may expose placeholder names and resolved/unresolved booleans.
- Agents may upload temporary credentials with `POST /credentials/session`; uploaded values are session-only memory state, never written to disk, never returned, and cleared on shutdown.
- Agents may clear session credentials with `DELETE /credentials/session`.
- API responses from credential endpoints return only safe status: placeholder names, resolved/unresolved booleans, and `sessionCredentialCount`; they must never echo submitted values.
- Session credentials override file credentials with the same key for the current ServiceHost process only.
- Agents should verify all required credentials are resolved via the API before starting dependent services.

Session-only credential API examples:

```bash
# Upload temporary in-memory credentials. The response must not echo values.
curl -X POST http://localhost:9500/credentials/session \
  -H "Content-Type: application/json" \
  -d '{"MUNERIS_KIOSK_DB":"Server=...;Password=...","MUNERIS_KIOSK_TRANSACTION_URL":"https://..."}'

# Verify without reading secret values.
curl http://localhost:9500/
# Check credentials.allResolved == true or inspect credentials.unresolved.

# Clear all session-only credentials.
curl -X DELETE http://localhost:9500/credentials/session
```
- Starting a service with unresolved credentials should fail fast and list unresolved placeholder names only.

Detailed design is recorded in `docs/plans/2026-08-27-config-credentials-separation.md`.

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

## Releases and Local Installation

Automated releases are created by GitHub Actions. Every push to `master` triggers `.github/workflows/release.yml`, auto-increments version from git tags, publishes both WPF and TUI executables, and creates a GitHub release.

Release assets:

```text
ServiceHost.exe
ServiceHost.Tui.exe
```

Canonical local install location:

```text
%LOCALAPPDATA%\Programs\ServiceHost
```

This is a normal PATH-based tool install, not a Pi/Herdr extension. The install folder should contain:

```text
ServiceHost.exe
ServiceHost.Tui.exe
servicehost.cmd
servicehost-tui.cmd
```

The `.cmd` files are simple shims so users and agents can run:

```powershell
servicehost
servicehost-tui
```

Current manual install/update process from a machine with GitHub CLI:

```powershell
$installDir = Join-Path $env:LOCALAPPDATA "Programs\ServiceHost"
New-Item -ItemType Directory -Force -Path $installDir | Out-Null

gh release download --repo mbundgaard/ServiceHost --dir $installDir --clobber --pattern "ServiceHost*.exe"

Set-Content -Path (Join-Path $installDir "servicehost.cmd") -Value "@echo off`r`n`"%~dp0ServiceHost.exe`" %*" -Encoding ASCII
Set-Content -Path (Join-Path $installDir "servicehost-tui.cmd") -Value "@echo off`r`n`"%~dp0ServiceHost.Tui.exe`" %*" -Encoding ASCII

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if (($userPath -split ";") -notcontains $installDir) {
    [Environment]::SetEnvironmentVariable("Path", (($userPath.TrimEnd(";"), $installDir) -join ";"), "User")
}
```

Existing terminals/panes may not see PATH changes until restarted. In an already-open terminal, temporarily add it with:

```powershell
$env:Path += ";$env:LOCALAPPDATA\Programs\ServiceHost"
```

Do not keep runtime exe copies in project roots long-term; use the canonical install folder to avoid PATH/version confusion.

Future improvement: add an `install.ps1` script that performs the download, shim creation, unblock, and PATH registration automatically.
