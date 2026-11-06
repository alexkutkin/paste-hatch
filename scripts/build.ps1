# Builds the PasteHatch WinUI app and prints the path to the executable.
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',
    [string]$ArtifactsPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'project-output.ps1')
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

    $exe = Get-PasteHatchExecutable -Configuration $Configuration -Platform $Platform -ArtifactsPath $ArtifactsPath
    Write-Host ""
    Write-Host "Built: $exe"
    Write-Host "Run:   powershell -ExecutionPolicy Bypass -File scripts\run.ps1"
}
finally {
    Pop-Location
}
