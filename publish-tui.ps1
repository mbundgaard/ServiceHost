# Publish ServiceHost TUI as a console executable
$srcDir = Join-Path $PSScriptRoot "src"
$publishDir = Join-Path $PSScriptRoot "publish-tui"

Push-Location $srcDir
try {
    dotnet publish ServiceHost.Tui\ServiceHost.Tui.csproj -c Release -r win-x64 -o $publishDir

    Write-Host ""
    Write-Host "Published to: $publishDir" -ForegroundColor Green
    Write-Host "Executable: ServiceHost.Tui.exe"
}
finally {
    Pop-Location
}
