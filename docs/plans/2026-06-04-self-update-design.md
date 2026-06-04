# Self-updating ServiceHost — Design

**Date:** 2026-06-04
**Status:** Approved (design); implementation plan to follow

## Goal

Make ServiceHost install its own updates, mirroring the UI-nudge model already
proven in `D:\Source\MunerisIpPrinter`. A background poll downloads new releases
silently; a clickable nudge in the window installs + restarts; any update
downloaded but not yet applied is auto-applied on next startup.

Updates become a **purely UI concern** — the HTTP API stops reporting versions
entirely.

## Approach

Port MunerisIpPrinter's three updater services and wire them to ServiceHost's
existing status-bar version label. This is a faithful port of the user's own
proven code, with a small number of ServiceHost-specific adaptations.

### Decisions (from brainstorming)

- **Trigger model:** UI nudge + click, like MunerisIpPrinter (background download,
  manual click to install, plus auto-apply pending update on startup). *Not*
  fully automatic, *not* an API-triggered update.
- **API update info:** removed entirely. `GET /` says nothing about versions;
  `VersionChecker` is deleted.
- **`POST /shutdown`:** kept as a generic control endpoint (also wired to App's
  `ShutdownRequested`), just without update framing.

## Components

### 1. New services (`src/Services/`)

Ported from MunerisIpPrinter, adapted:

- **`UpdateChecker.cs`** — queries `api.github.com/repos/mbundgaard/ServiceHost/releases/latest`,
  compares versions, finds the stable-named asset.
- **`UpdateApplier.cs`** — streams the asset to a `.partial` temp file, atomic
  rename, then `ApplyAndExit` hands off to the relauncher and shuts down.
- **`Relauncher.cs`** — detached PowerShell that `Wait-Process`es on the old PID,
  `Move-Item`s the downloaded exe over the current one, then relaunches.

**ServiceHost-specific adaptations:**

- **Single-file path:** ServiceHost is `PublishSingleFile=true`, so
  `Assembly.Location` is empty. Use **`Environment.ProcessPath`** for the current
  exe path everywhere (as the old `VersionChecker` already did).
- **Integer version tags:** ServiceHost tags are bare majors (`v15`), and
  `Version.TryParse("15")` fails (needs ≥2 components). `UpdateChecker` pads a
  bare-integer tag to `"15.0.0"` before parsing, so `v15` → `Version(15,0,0)` and
  compares against the running assembly version (`15.0.0`).
- **Asset name:** stable `ServiceHost.exe` (what `release.yml` already publishes).
- **Temp file:** `%TEMP%\ServiceHost-update-<version>.exe`. User-Agent
  `ServiceHost-Updater`.
- **No TLS-1.2 workaround:** net8 defaults to TLS 1.2; drop the net462 static-ctor
  block.

### 2. Startup: apply pending update first (`App.xaml.cs`)

At the very top of `Application_Startup`, before config load / API bind on 9500 /
child-service adoption: scan `%TEMP%` for a `ServiceHost-update-*.exe` newer than
the running version. If found, call `Relauncher.RelaunchAfterExit(swapFromPath)`
and `Shutdown()` immediately. The relauncher waits for the old PID to exit
(releasing port 9500 cleanly), swaps the exe, and relaunches. Child services keep
running detached and are re-adopted by the new instance via port adoption — no
orphans.

### 3. UI nudge (`MainWindow.xaml` + `.xaml.cs` + `MainViewModel`)

The status bar already shows `VersionText` (`v15`) bottom-right. Make that
`TextBlock` clickable, driven through three states:

- **Normal:** `v15` (no hand cursor).
- **Update available** (newer found; download in flight or no asset):
  `v16 ↗` — click opens the release page.
- **Update ready** (background download landed): `Update ready — v16` in accent
  color — click calls `UpdateApplier.ApplyAndExit` → installs + restarts.

A background poll (once shortly after launch, then ~hourly) runs silently, never
blocks the UI, and is silent on every failure. New `MainViewModel` observable
properties (`UpdateText`, `UpdateState`, `UpdateUrl`) + a click handler in
`MainWindow.xaml.cs`.

### 4. Deletions / API cleanup

- **Delete `src/Services/VersionChecker.cs`** (and `VersionInfo`).
- **`ApiHost.cs`:** remove the `_versionChecker` field + ctor param, the `update`
  object in `GET /`, and update-related text in the `POST /shutdown` description.
  Keep `POST /shutdown` itself.
- **`App.xaml.cs`:** drop `_versionChecker` construction/disposal; fix the
  `ApiHost` ctor call.
- **`claude.md`:** update the HTTP API table (no update section) and Key Behaviors
  (self-update replaces "Update Check").

## Error handling

Every update path is silent-on-failure (offline, rate-limited, AV lock on the exe,
disk full, malformed JSON). An update problem must never disturb service
management or crash the host.

## Testing

No automated test project exists in this repo. Verify by:

1. Build via `.\dev.ps1`.
2. Manual smoke test: launch with a locally-lowered version, confirm the nudge
   appears, click-to-install swaps the exe and relaunches, and child services are
   re-adopted (no orphans, port 9500 rebinds cleanly).
