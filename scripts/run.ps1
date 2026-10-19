# Builds PasteHatch if needed and starts it, then waits for the window to appear.
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\PasteHatch.App\PasteHatch.App.csproj'
$runtime = if ($Platform -eq 'x64') { 'win-x64' } else { 'win-arm64' }
$exe = Join-Path $root "src\PasteHatch.App\bin\$Platform\$Configuration\net9.0-windows10.0.19041.0\$runtime\PasteHatch.exe"

Push-Location $root
try {
    if (-not $NoBuild) {
        dotnet build $project -c $Configuration -p:Platform=$Platform
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }

    if (-not (Test-Path $exe)) {
        throw "Executable not found: $exe (run scripts\build.ps1 first)"
    }

    Start-Process -FilePath $exe
    Write-Host "PasteHatch started: $exe"
}
finally {
    Pop-Location
}
