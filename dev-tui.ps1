# Build and run ServiceHost TUI in development mode
Push-Location (Join-Path $PSScriptRoot "src")
try {
    dotnet run --project ServiceHost.Tui\ServiceHost.Tui.csproj
}
finally {
    Pop-Location
}
