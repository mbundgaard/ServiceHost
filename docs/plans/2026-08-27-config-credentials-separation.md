# Config/Credentials Separation Design

## Goal

ServiceHost should allow project service definitions to be checked into source control while keeping real credentials out of the repo. Agents and humans should be able to verify that all required credentials are available without seeing secret values.

## Desired Project Model

Projects check in non-secret service configuration:

```text
<Project>/ServiceHost.json
```

Projects do **not** check in real credentials. Credentials should live in a project-local gitignored file:

```text
<Project>/ServiceHost.credentials.json
```

The checked-in config may contain placeholders in service environment values, args, URLs, or other string fields:

```json
{
  "apiPort": 9500,
  "logDirectory": "./logs",
  "services": [
    {
      "name": "kiosk-api",
      "command": "dotnet",
      "args": ["run"],
      "workingDirectory": "./src/Kiosk.Api",
      "port": 5000,
      "url": "${MUNERIS_KIOSK_TRANSACTION_URL}",
      "environment": {
        "ConnectionStrings__Default": "${MUNERIS_KIOSK_DB}",
        "TransactionApi__BaseUrl": "${MUNERIS_KIOSK_TRANSACTION_URL}"
      }
    }
  ]
}
```

A local credentials file maps placeholder names to real values:

```json
{
  "MUNERIS_KIOSK_DB": "Server=...;Database=...;User Id=...;Password=...",
  "MUNERIS_KIOSK_TRANSACTION_URL": "https://..."
}
```

## CLI/API Inputs

ServiceHost should support both config and credentials paths:

```powershell
servicehost-tui --config .\ServiceHost.json --credentials .\ServiceHost.credentials.json
```

Environment variable fallback:

```text
SERVICEHOST_CONFIG
SERVICEHOST_CREDENTIALS
SERVICEHOST_PORT
```

Recommended resolution order for credentials:

1. `--credentials <path>`
2. `SERVICEHOST_CREDENTIALS`
3. `ServiceHost.credentials.json` next to the resolved config file
4. no credentials file

## Placeholder Resolution

Placeholder syntax:

```text
${NAME}
```

Rules:

- Placeholders may appear in string values in `ServiceHost.json`.
- Initial support resolves service `command`, `args`, `workingDirectory`, `url`, and `environment` values.
- Resolution happens in memory at runtime before a process is started.
- `ServiceHost.json` must not be rewritten with resolved secret values.
- Credentials file values must never be logged or returned by the API.
- Unknown placeholders are tracked as unresolved credentials.

## Session-Only API Credentials

Agents may provide temporary credentials for the current ServiceHost process via the HTTP API:

```http
POST /credentials/session
Content-Type: application/json

{
  "MUNERIS_KIOSK_DB": "Server=...;Password=..."
}
```

Rules:

- Values are kept in memory only.
- Values are never written to disk.
- Values are never returned by API/TUI/logs.
- Values override file credentials with the same key for the current process only.
- Values are cleared on shutdown.
- `DELETE /credentials/session` clears all session-only credentials immediately.

## API Manifest Requirements

The HTTP API manifest should expose credential status without exposing values:

```json
{
  "credentials": {
    "credentialsPath": "D:/Source/Project/ServiceHost.credentials.json",
    "credentialsLoaded": true,
    "sessionCredentialCount": 0,
    "allResolved": false,
    "required": [
      { "name": "MUNERIS_KIOSK_DB", "resolved": true },
      { "name": "MUNERIS_KIOSK_TRANSACTION_URL", "resolved": false }
    ],
    "unresolved": ["MUNERIS_KIOSK_TRANSACTION_URL"]
  }
}
```

Security constraints:

- Never expose credential values.
- Avoid exposing surrounding resolved strings if they may contain embedded secrets.
- It is acceptable to expose placeholder names and boolean resolved/unresolved status.
- Agents should be able to assert: `credentials.allResolved == true`.

## TUI Requirements

The TUI should show a concise non-secret credential indicator:

```text
creds:ok
creds:missing:2
creds:none
```

If unresolved credentials exist, the TUI can list placeholder names only, not values, for example in a status/detail pane:

```text
Unresolved credentials:
- MUNERIS_KIOSK_TRANSACTION_URL
- MUNERIS_KIOSK_DB
```

## Service Start Behavior

Recommended behavior:

- If a service has unresolved credentials, starting that service should fail fast with a clear error.
- Error message may include unresolved placeholder names.
- Error message must not include credential values.
- Services without unresolved credentials should still be startable even if other services have missing credentials.

## Files Likely to Change

```text
src/ServiceHost.Core/ServiceHostOptions.cs
src/ServiceHost.Core/ServiceHostRuntime.cs
src/ServiceHost.Core/Services/ConfigurationService.cs
src/ServiceHost.Core/Services/ProcessManager.cs
src/ServiceHost.Core/Api/ApiHost.cs
src/ServiceHost.Core/Models/CredentialStatus.cs
src/ServiceHost.Tui/Program.cs
src/ServiceHost.Wpf/App.xaml.cs
README.md
AGENTS.md
```

Likely new files:

```text
src/ServiceHost.Core/Services/CredentialService.cs
src/ServiceHost.Core/Models/CredentialStatus.cs
```

## Implementation Milestones

1. Add CLI option parsing for `--credentials` and `SERVICEHOST_CREDENTIALS`.
2. Load credentials JSON into memory without logging values.
3. Scan config for `${NAME}` placeholders and expose required/resolved/unresolved status.
4. Resolve placeholders into process start configuration in memory only.
5. Fail service start if that service has unresolved placeholders.
6. Add API manifest credential status.
7. Add TUI credential summary.
8. Add session-only API credential upload/clear endpoints.
9. Update docs and project `AGENTS.md` snippets.
