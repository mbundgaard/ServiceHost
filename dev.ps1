# Build and run ServiceHost in development mode
Push-Location (Join-Path $PSScriptRoot "src")
try {
    dotnet run --project ServiceHost.Wpf\ServiceHost.csproj
}
finally {
    Pop-Location
}
