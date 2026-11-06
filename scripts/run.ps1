# Builds PasteHatch unless -NoBuild is set, then starts it.
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'project-output.ps1')
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\PasteHatch.App\PasteHatch.App.csproj'

Push-Location $root
try {
    if (-not $NoBuild) {
        dotnet build $project -c $Configuration -p:Platform=$Platform
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }

    $exe = Get-PasteHatchExecutable -Configuration $Configuration -Platform $Platform
    if (-not (Test-Path $exe)) {
        throw "Executable not found: $exe (run scripts\build.ps1 first)"
    }

    Start-Process -FilePath $exe
    Write-Host "PasteHatch started: $exe"
}
finally {
    Pop-Location
}
