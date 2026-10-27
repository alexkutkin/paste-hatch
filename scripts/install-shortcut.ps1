# Creates (or removes) a desktop shortcut to the built PasteHatch executable.
# The shortcut points at the Release build (self-contained Windows App SDK, .NET 10 runtime).
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\install-shortcut.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\install-shortcut.ps1 -Uninstall
param(
    [switch]$Uninstall,
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$runtime = if ($Platform -eq 'x64') { 'win-x64' } else { 'win-arm64' }
$exe = Join-Path $root "src\PasteHatch.App\bin\$Platform\$Configuration\net10.0-windows10.0.19041.0\$runtime\PasteHatch.exe"
$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktop 'PasteHatch.lnk'

if ($Uninstall) {
    if (Test-Path $shortcutPath) {
        Remove-Item $shortcutPath -Force
        Write-Host "Shortcut removed: $shortcutPath"
    }
    else {
        Write-Host "Shortcut not found: $shortcutPath"
    }
    exit 0
}

if (-not (Test-Path $exe)) {
    throw "Executable not found: $exe. Run scripts\build.ps1 first."
}

$iconPath = Join-Path (Split-Path $exe -Parent) 'Assets\PasteHatch.ico'
$iconLocation = if (Test-Path $iconPath) { "$iconPath,0" } else { "$exe,0" }

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = Split-Path $exe -Parent
$shortcut.IconLocation = $iconLocation
$shortcut.Description = 'PasteHatch - context baskets for local AI agents'
$shortcut.WindowStyle = 1
$shortcut.Save()

Write-Host "Shortcut created: $shortcutPath"
Write-Host "Target: $exe"
