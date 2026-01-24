# Publish ServiceHost as single-file executable
$srcDir = Join-Path $PSScriptRoot "src"
$publishDir = Join-Path $PSScriptRoot "publish"

Push-Location $srcDir
try {
    dotnet publish -c Release -r win-x64 -o $publishDir

    Write-Host ""
    Write-Host "Published to: $publishDir" -ForegroundColor Green
    Write-Host "Executable: ServiceHost.exe"
}
finally {
    Pop-Location
}
