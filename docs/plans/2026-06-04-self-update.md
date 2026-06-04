# Self-updating ServiceHost Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make ServiceHost download and install its own GitHub releases via a clickable status-bar nudge, with pending updates auto-applied on startup — mirroring the model already shipped in MunerisIpPrinter.

**Architecture:** Port three small services from `D:\Source\MunerisIpPrinter` (`UpdateChecker`, `UpdateApplier`, `Relauncher`), adapted for ServiceHost's single-file publish and bare-integer version tags. Wire them to a background poll in `MainViewModel` that drives a three-state clickable version label, and apply any pending temp-file update at the very top of `App.Application_Startup`. Remove the API's version/update reporting and delete `VersionChecker`.

**Tech Stack:** .NET 8 WPF (single-file publish), CommunityToolkit.Mvvm, ASP.NET Core minimal API, GitHub Releases API.

**Testing note (read first):** This repo has **no test project** and the auto-release workflow publishes `src/ServiceHost.csproj` on every push to `master`. Per the approved design (`2026-06-04-self-update-design.md`), we verify each task by **building** (`dotnet build src/ServiceHost.csproj`) and finish with a **manual smoke test**. This is a deliberate, approved deviation from strict TDD: the risky logic (version-tag parsing) is pure and small, and is exercised in the smoke test by lowering the local version. Do **not** add a test project.

**Reference source files (read before porting):**
- `D:\Source\MunerisIpPrinter\Services\UpdateChecker.cs`
- `D:\Source\MunerisIpPrinter\Services\UpdateApplier.cs`
- `D:\Source\MunerisIpPrinter\Services\Relauncher.cs`
- `D:\Source\MunerisIpPrinter\App.xaml.cs` (TryApplyPendingUpdate)
- `D:\Source\MunerisIpPrinter\UI\MainWindow.xaml.cs:140-249` (poll + nudge flow)

**Reminder:** Do NOT commit or push at any point — the project rule forbids it until the user explicitly says so. The "Commit" steps below are intentionally omitted for that reason; instead each task ends at a green build.

---

## Task 1: Port `Relauncher` (no behavioral change needed)

**Files:**
- Create: `src/Services/Relauncher.cs`

**Step 1: Copy the file**

Copy `D:\Source\MunerisIpPrinter\Services\Relauncher.cs` verbatim, changing only the namespace to `ServiceHost.Services`. The logic (detached PowerShell `Wait-Process` → optional `Move-Item` → `Start-Process`, with `QuotePs` escaping) is platform logic with no MunerisIpPrinter-specific assumptions, so no other change is needed.

**Step 2: Build**

Run: `dotnet build src/ServiceHost.csproj -c Debug`
Expected: SUCCESS (file compiles in isolation; not yet referenced).

---

## Task 2: Port `UpdateChecker`, adapted for ServiceHost

**Files:**
- Create: `src/Services/UpdateChecker.cs`

**Step 1: Copy and adapt**

Copy `D:\Source\MunerisIpPrinter\Services\UpdateChecker.cs`, namespace `ServiceHost.Services`, with these changes:

1. **Drop the TLS-1.2 static-ctor workaround** (net8 defaults to TLS 1.2). Keep the User-Agent + Accept header setup, but rename the UA to `ServiceHost-Updater`. Remove the `using System.Net;` if `ServicePointManager` is the only consumer.

2. **Adapt tag parsing for bare-integer tags.** ServiceHost tags are `v15` and `Version.TryParse("15")` returns false. Replace the version-parse block with a helper that pads a bare major:

```csharp
// Tags are conventionally "v15" (bare major) or "v1.2.3". Strip the leading v,
// then pad a bare major to "15.0" so Version.TryParse accepts it.
var versionPart = tag!.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag!.Substring(1) : tag!;
if (!versionPart.Contains('.')) versionPart += ".0";
if (!Version.TryParse(versionPart, out var latest)) return null;
```

3. **Asset lookup:** the stable asset name is `ServiceHost.exe`. Replace the MunerisIpPrinter asset block with:

```csharp
var assetUrl = FindAssetDownloadUrl(json, "ServiceHost.exe");
return new UpdateInfo(latest, releaseUrl!, assetUrl);
```

Keep `UpdateInfo`, `Normalize`, `FindAssetDownloadUrl`, and `ExtractFirstStringField` unchanged. (We keep the hand-rolled JSON extractor rather than `System.Text.Json` so this matches the proven source and avoids re-deriving edge cases.)

**Step 2: Build**

Run: `dotnet build src/ServiceHost.csproj -c Debug`
Expected: SUCCESS.

---

## Task 3: Port `UpdateApplier`, adapted for single-file path

**Files:**
- Create: `src/Services/UpdateApplier.cs`

**Step 1: Copy and adapt**

Copy `D:\Source\MunerisIpPrinter\Services\UpdateApplier.cs`, namespace `ServiceHost.Services`, with:

1. Rename the download UA to `ServiceHost-Updater`.
2. Drop the TLS-1.2 try/catch block (net8 default).
3. `DownloadAsync` and `ApplyAndExit` are otherwise unchanged. `ApplyAndExit(downloadedExe, currentExe)` already takes the current exe as a parameter, so the single-file concern is handled by the *caller* passing `Environment.ProcessPath` (Tasks 4 & 6), not here.

**Step 2: Build**

Run: `dotnet build src/ServiceHost.csproj -c Debug`
Expected: SUCCESS.

---

## Task 4: Auto-apply pending update on startup

**Files:**
- Modify: `src/App.xaml.cs` (top of `Application_Startup`, line 18-19)

**Step 1: Add the pending-update check as the very first action**

At the start of `Application_Startup`, *before* the exception-handler wiring, insert:

```csharp
// If a previous session downloaded a newer build, swap it in now — before we
// bind the API port or adopt child services — so the new version just comes up
// on next launch. Child services keep running detached and are re-adopted.
if (TryApplyPendingUpdate())
{
    Shutdown();
    return;
}
```

**Step 2: Add the helper method to the `App` class**

Port `TryApplyPendingUpdate` from `D:\Source\MunerisIpPrinter\App.xaml.cs`, with these single-file adaptations:

```csharp
/// <summary>True if a pending update was found and the relauncher was started
/// (caller must Shutdown immediately so the swap can complete).</summary>
private static bool TryApplyPendingUpdate()
{
    try
    {
        var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var currentExe = Environment.ProcessPath; // single-file safe; Assembly.Location is empty
        if (current == null || string.IsNullOrEmpty(currentExe)) return false;

        const string prefix = "ServiceHost-update-";
        var candidates = Directory.GetFiles(Path.GetTempPath(), prefix + "*.exe");
        Version? bestVer = null;
        string? bestPath = null;
        foreach (var path in candidates)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var verStr = name.Substring(prefix.Length);
            if (!Version.TryParse(verStr, out var ver)) continue;
            if (ver <= current) continue;
            if (bestVer == null || ver > bestVer) { bestVer = ver; bestPath = path; }
        }

        if (bestPath == null) return false;
        Services.Relauncher.RelaunchAfterExit(currentExe!, swapFromPath: bestPath);
        return true;
    }
    catch { return false; } // detection must never block startup
}
```

Note: `Application_Startup` is `async void` and already references `System.IO` (`using System.IO;` is at the top). Confirm the `using` is present (it is, line 1).

**Step 3: Build**

Run: `dotnet build src/ServiceHost.csproj -c Debug`
Expected: SUCCESS.

---

## Task 5: Remove `VersionChecker` and all API update reporting

**Files:**
- Delete: `src/Services/VersionChecker.cs`
- Modify: `src/Api/ApiHost.cs` (field 17, ctor 26-34, GET / 78-107, endpoints 182)
- Modify: `src/App.xaml.cs` (field 14, construction 72, ctor call 75, disposal 118)

**Step 1: Delete the file**

Delete `src/Services/VersionChecker.cs`.

**Step 2: Strip `VersionChecker` from `ApiHost.cs`**

- Remove field: `private readonly VersionChecker _versionChecker;` (line 17).
- Remove the ctor param `, VersionChecker versionChecker` and the body line `_versionChecker = versionChecker;` (lines 26, 32).
- In `GET /`: delete `var versionInfo = await _versionChecker.CheckForUpdateAsync();` (line 81) and the entire `object? update = ...` block (lines 93-101).
- In the `manifest` object: change `version = versionInfo.CurrentVersion,` to read the assembly version directly, and delete the `update,` line:

```csharp
var manifest = new
{
    name = "ServiceHost",
    version = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.Major ?? 0}",
    description = "Service manager with HTTP API for AI assistants",
    // ... rest unchanged (no `update,` line)
```

- In `endpoints` (line 182): change `["POST /shutdown"] = "Shutdown the application (for updates)"` to `["POST /shutdown"] = "Shutdown the application"`.
- Leave the `POST /shutdown` handler itself (lines 547-562) unchanged.

**Step 3: Strip `VersionChecker` from `App.xaml.cs`**

- Remove field `private VersionChecker? _versionChecker;` (line 14).
- Remove `_versionChecker = new VersionChecker();` and its comment (lines 71-72).
- Change the `ApiHost` construction (line 75) to drop the last arg:
  `_apiHost = new ApiHost(_configService.Config.ApiPort, _processManager, _logManager, _configService);`
- Remove `_versionChecker?.Dispose();` from `Application_Exit` (line 118).

**Step 4: Build**

Run: `dotnet build src/ServiceHost.csproj -c Debug`
Expected: SUCCESS, no references to `VersionChecker`/`VersionInfo` remain.

---

## Task 6: Background poll + three-state nudge in the ViewModel

**Files:**
- Modify: `src/ViewModels/MainViewModel.cs` (VersionText 65/76-77, ctor, new members)

**Step 1: Replace the read-only `VersionText` with observable update state**

`VersionText` is currently a get-only string (line 65, set at 76-77). Replace it with observable properties so the nudge can change at runtime. Add near the other `[ObservableProperty]` fields:

```csharp
[ObservableProperty] private string _versionText = "v0";
[ObservableProperty] private bool _updateClickable;   // hand cursor + click enabled
private string? _updateUrl;                            // release page (available state)
private string? _downloadedUpdatePath;                 // set once download lands (ready state)
```

Remove the old `public string VersionText { get; }` declaration (line 65) and its assignment in the ctor (76-77). In the ctor, initialize:

```csharp
var version = Assembly.GetExecutingAssembly().GetName().Version;
VersionText = version != null ? $"v{version.Major}" : "v0";
```

**Step 2: Add the poll loop, started from the ctor**

Add a field `private readonly CancellationTokenSource _updateCts = new();` and, at the end of the ctor, start the poll:

```csharp
_ = PollForUpdatesAsync(version, _updateCts.Token);
```

Add the methods (port from MunerisIpPrinter `MainWindow.xaml.cs:138-196`, adapted to the ViewModel + ServiceHost naming):

```csharp
private static readonly TimeSpan UpdatePollInterval = TimeSpan.FromHours(1);
private const string GitHubRepo = "mbundgaard/ServiceHost";

private async Task PollForUpdatesAsync(Version? current, CancellationToken ct)
{
    if (current == null) return;
    // Small initial delay so startup isn't competing with the first network call.
    try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (TaskCanceledException) { return; }

    while (!ct.IsCancellationRequested)
    {
        if (_downloadedUpdatePath == null || !System.IO.File.Exists(_downloadedUpdatePath))
        {
            try
            {
                var info = await UpdateChecker.CheckAsync(GitHubRepo, current, ct);
                if (info != null) await ApplyUpdateInfoAsync(info);
            }
            catch { /* update poll must never crash the app */ }
        }
        try { await Task.Delay(UpdatePollInterval, ct); } catch (TaskCanceledException) { return; }
    }
}

private async Task ApplyUpdateInfoAsync(UpdateInfo info)
{
    var versionStr = $"v{info.LatestVersion.Major}";
    // Phase 1: available — link to the release page while the download streams.
    Application.Current?.Dispatcher.Invoke(() =>
    {
        _updateUrl = info.ReleaseUrl;
        VersionText = $"{versionStr} ↗";   // "v16 ↗"
        UpdateClickable = true;
    });

    if (string.IsNullOrEmpty(info.AssetUrl)) return; // manual-only; keep Phase 1 link

    // Phase 2: background download to %TEMP%\ServiceHost-update-<full version>.exe
    var dlPath = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"ServiceHost-update-{info.LatestVersion}.exe");
    bool ok = await UpdateApplier.DownloadAsync(info.AssetUrl!, dlPath);
    if (!ok) return; // keep Phase 1 link so the user can still reach the release page

    // Phase 3: ready — click now installs + restarts.
    Application.Current?.Dispatcher.Invoke(() =>
    {
        _downloadedUpdatePath = dlPath;
        VersionText = $"Update ready — {versionStr}";
        UpdateClickable = true;
    });
}

/// <summary>Invoked by the status-bar click. Available → open release page; ready → install + restart.</summary>
public void OnVersionClicked()
{
    if (!string.IsNullOrEmpty(_downloadedUpdatePath) && System.IO.File.Exists(_downloadedUpdatePath))
    {
        var currentExe = Environment.ProcessPath; // single-file safe
        if (string.IsNullOrEmpty(currentExe)) return;
        UpdateApplier.ApplyAndExit(_downloadedUpdatePath!, currentExe!);
        return;
    }
    if (string.IsNullOrEmpty(_updateUrl)) return;
    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_updateUrl) { UseShellExecute = true }); }
    catch { /* opening the browser is a nicety; never crash over it */ }
}
```

**Step 3: Cancel the poll on dispose**

In `Dispose()` (line 263), add `_updateCts.Cancel();` before the existing cleanup. Add `using ServiceHost.Services;` if not already present (it is — line 8).

**Step 4: Build**

Run: `dotnet build src/ServiceHost.csproj -c Debug`
Expected: SUCCESS.

---

## Task 7: Make the status-bar version label clickable

**Files:**
- Modify: `src/MainWindow.xaml` (status bar version `TextBlock`, lines 354-358)
- Modify: `src/MainWindow.xaml.cs` (add click handler)

**Step 1: Wire the XAML**

Replace the version `TextBlock` (lines 354-358) with a clickable version. Bind `Cursor` to `UpdateClickable` and add a hover underline + the click handler:

```xml
<TextBlock Grid.Column="1"
           Text="{Binding VersionText}"
           Foreground="{StaticResource TextSecondaryBrush}"
           FontSize="11"
           VerticalAlignment="Center"
           MouseLeftButtonUp="Version_Click">
    <TextBlock.Style>
        <Style TargetType="TextBlock">
            <Style.Triggers>
                <DataTrigger Binding="{Binding UpdateClickable}" Value="True">
                    <Setter Property="Cursor" Value="Hand"/>
                    <Setter Property="Foreground" Value="{StaticResource AccentBrush}"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </TextBlock.Style>
</TextBlock>
```

**Step 2: Add the code-behind handler**

In `src/MainWindow.xaml.cs`, alongside the other handlers (e.g. after `GitHub_Click`, line 98):

```csharp
private void Version_Click(object sender, MouseButtonEventArgs e)
{
    if (DataContext is MainViewModel vm) vm.OnVersionClicked();
}
```

**Step 3: Build**

Run: `dotnet build src/ServiceHost.csproj -c Debug`
Expected: SUCCESS.

---

## Task 8: Update `claude.md`

**Files:**
- Modify: `claude.md` (HTTP API table line 57; Key Behaviors lines 84-95)

**Step 1: Edit docs**

- In the HTTP API block, change the `POST /shutdown` line description from `→ Shutdown application (for updates)` to `→ Shutdown application`.
- In **Key Behaviors**, replace the `**Update Check**` bullet with:
  `- **Self-Update**: Polls GitHub releases hourly; downloads new builds to %TEMP% in the background. The status-bar version label becomes a clickable nudge to install + restart, and any pending download is auto-applied on next startup. Child services persist and are re-adopted across the restart.`

**Step 2: Build (sanity, no code change)**

Run: `dotnet build src/ServiceHost.csproj -c Debug`
Expected: SUCCESS.

---

## Task 9: Manual smoke test

**Files:** none (verification only).

**Step 1: Force an "update available" state locally**

Temporarily lower the running version so the live `v15` release looks newer. Edit `src/ServiceHost.csproj` line 12 to `<Version>1.0.0</Version>`, then run `.\dev.ps1`.

**Step 2: Verify the nudge**

Within ~5–10 seconds the status-bar label should change to `v15 ↗` and then `Update ready — v15` once the background download of the real `ServiceHost.exe` asset completes. Confirm `%TEMP%\ServiceHost-update-15.0.0.exe` exists.

**Step 3: Verify install + restart**

Start a child service first (so adoption is exercised). Click the nudge. Confirm: the app exits, the exe at the running location is swapped, a new instance launches, port 9500 rebinds, and the child service is re-adopted as Running (not orphaned/duplicated).

**Step 4: Verify auto-apply-on-startup path**

With a fresh `%TEMP%\ServiceHost-update-<higher>.exe` present (e.g. copy a newer build there) and a lowered local version, launch the app; confirm it immediately relaunches into the swapped build with no click.

**Step 5: Revert the version**

Restore `src/ServiceHost.csproj` line 12 to its real value (`15.0.0`, or whatever master holds). The release workflow sets the real version at publish time, so the committed value only matters for local runs — but leave it correct.

---

## Done criteria

- `dotnet build src/ServiceHost.csproj` is green.
- No references to `VersionChecker`/`VersionInfo` remain; `GET /` has no `update` field.
- Status-bar label nudges through available → ready and installs on click.
- Pending temp updates auto-apply on startup; child services survive the restart.
- `claude.md` reflects self-update.
- **Nothing committed or pushed** (awaiting explicit user go-ahead).
