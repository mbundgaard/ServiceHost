param(
    [ValidateSet("Local", "Release")]
    [string]$Source = "Local",

    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\ServiceHost"),

    [string]$Repository = "mbundgaard/ServiceHost"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Add-ToUserPath {
    param([Parameter(Mandatory)][string]$PathToAdd)

    $normalizedPath = [System.IO.Path]::GetFullPath($PathToAdd).TrimEnd('\')
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if ($null -eq $userPath) {
        $userPath = ""
    }

    $entries = $userPath -split ';' | Where-Object { $_ -and $_.Trim() }
    $alreadyPresent = $entries | Where-Object {
        try {
            [System.IO.Path]::GetFullPath($_).TrimEnd('\') -ieq $normalizedPath
        }
        catch {
            $_.TrimEnd('\') -ieq $normalizedPath
        }
    }

    if (-not $alreadyPresent) {
        $newPath = (($entries + $normalizedPath) -join ';')
        [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
        Write-Host "Added to User PATH: $normalizedPath" -ForegroundColor Green
    }
    else {
        Write-Host "User PATH already contains: $normalizedPath" -ForegroundColor DarkGray
    }

    $currentEntries = ($env:Path -split ';') | Where-Object { $_ -and $_.Trim() }
    $currentHasPath = $currentEntries | Where-Object {
        try {
            [System.IO.Path]::GetFullPath($_).TrimEnd('\') -ieq $normalizedPath
        }
        catch {
            $_.TrimEnd('\') -ieq $normalizedPath
        }
    }

    if (-not $currentHasPath) {
        $env:Path = (($currentEntries + $normalizedPath) -join ';')
        Write-Host "Added to current process PATH: $normalizedPath" -ForegroundColor Green
    }
}

function Write-Shim {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ExecutableName
    )

    $content = "@echo off`r`n`"%~dp0$ExecutableName`" %*`r`n"
    Set-Content -Path $Path -Value $content -Encoding ASCII
}

$installDirFull = [System.IO.Path]::GetFullPath($InstallDir)
New-Item -ItemType Directory -Force -Path $installDirFull | Out-Null

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ServiceHost-install-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

try {
    if ($Source -eq "Release") {
        if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
            throw "GitHub CLI ('gh') is required for -Source Release. Install gh or run .\install.ps1 -Source Local."
        }

        Write-Host "Downloading latest ServiceHost release from $Repository..."
        gh release download --repo $Repository --dir $tempDir --clobber --pattern "ServiceHost*.exe"
    }
    else {
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
            throw ".NET SDK is required for -Source Local. Install the SDK or run .\install.ps1 -Source Release."
        }

        Write-Host "Publishing ServiceHost from local source..."
        dotnet publish (Join-Path $PSScriptRoot "src\ServiceHost.Wpf\ServiceHost.csproj") -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $tempDir
        dotnet publish (Join-Path $PSScriptRoot "src\ServiceHost.Tui\ServiceHost.Tui.csproj") -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $tempDir
    }

    $wpfExe = Join-Path $tempDir "ServiceHost.exe"
    $tuiExe = Join-Path $tempDir "ServiceHost.Tui.exe"

    if (-not (Test-Path $wpfExe)) {
        throw "ServiceHost.exe was not found in install source."
    }
    if (-not (Test-Path $tuiExe)) {
        throw "ServiceHost.Tui.exe was not found in install source."
    }

    Copy-Item -Path $wpfExe -Destination (Join-Path $installDirFull "ServiceHost.exe") -Force
    Copy-Item -Path $tuiExe -Destination (Join-Path $installDirFull "ServiceHost.Tui.exe") -Force

    Get-ChildItem -Path $installDirFull -Filter "ServiceHost*.exe" | ForEach-Object {
        Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue
    }

    Write-Shim -Path (Join-Path $installDirFull "servicehost.cmd") -ExecutableName "ServiceHost.exe"
    Write-Shim -Path (Join-Path $installDirFull "servicehost-tui.cmd") -ExecutableName "ServiceHost.Tui.exe"

    Add-ToUserPath -PathToAdd $installDirFull

    Write-Host ""
    Write-Host "Installed ServiceHost to: $installDirFull" -ForegroundColor Green
    Write-Host "Commands available in new sessions:" -ForegroundColor Green
    Write-Host "  servicehost"
    Write-Host "  servicehost-tui"
    Write-Host ""
    Write-Host "Existing sessions may need this once:" -ForegroundColor Yellow
    Write-Host '  $env:Path += ";$env:LOCALAPPDATA\Programs\ServiceHost"'
}
finally {
    Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}
