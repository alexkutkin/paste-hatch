# Builds the PasteHatch WinUI app and prints the path to the executable.
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',
    [string]$ArtifactsPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\PasteHatch.App\PasteHatch.App.csproj'

Push-Location $root
try {
    $buildArguments = @('build', $project, '-c', $Configuration, "-p:Platform=$Platform")
    if ($ArtifactsPath) { $buildArguments += @('--artifacts-path', $ArtifactsPath) }
    dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    $runtime = if ($Platform -eq 'x64') { 'win-x64' } else { 'win-arm64' }
    $exe = if ($ArtifactsPath) {
        Join-Path $ArtifactsPath "bin\PasteHatch.App\$($Configuration.ToLowerInvariant())_$runtime\PasteHatch.exe"
    } else {
        Join-Path $root "src\PasteHatch.App\bin\$Platform\$Configuration\net10.0-windows10.0.19041.0\$runtime\PasteHatch.exe"
    }
    Write-Host ""
    Write-Host "Built: $exe"
    Write-Host "Run:   powershell -ExecutionPolicy Bypass -File scripts\run.ps1"
}
finally {
    Pop-Location
}
