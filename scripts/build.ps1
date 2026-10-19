# Builds the PasteHatch WinUI app and prints the path to the executable.
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\PasteHatch.App\PasteHatch.App.csproj'

Push-Location $root
try {
    dotnet build $project -c $Configuration -p:Platform=$Platform
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    $runtime = if ($Platform -eq 'x64') { 'win-x64' } else { 'win-arm64' }
    $exe = Join-Path $root "src\PasteHatch.App\bin\$Platform\$Configuration\net9.0-windows10.0.19041.0\$runtime\PasteHatch.exe"
    Write-Host ""
    Write-Host "Built: $exe"
    Write-Host "Run:   powershell -ExecutionPolicy Bypass -File scripts\run.ps1"
}
finally {
    Pop-Location
}
