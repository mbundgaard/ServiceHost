# Build and run ServiceHost in development mode
Push-Location (Join-Path $PSScriptRoot "src")
try {
    dotnet run
}
finally {
    Pop-Location
}
