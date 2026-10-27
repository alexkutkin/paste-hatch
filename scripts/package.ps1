# Builds and tests PasteHatch, then creates a self-contained Release x64 ZIP.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\PasteHatch.App\PasteHatch.App.csproj'
$version = [string]([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') {
    throw 'PasteHatch.App.csproj must declare a version such as 1.0.0.'
}
$name = "PasteHatch-$version-win-x64"
$run = Join-Path $root ('.scratch\package-' + [guid]::NewGuid().ToString('N'))
$artifacts = Join-Path $run 'artifacts'
$stage = Join-Path $run $name
$dist = Join-Path $root 'dist'
$zip = Join-Path $dist "$name.zip"
New-Item -ItemType Directory -Path $stage, $dist -Force | Out-Null

Push-Location $root
try {
    foreach ($configuration in @('Debug', 'Release')) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1 -Configuration $configuration -ArtifactsPath $artifacts
        if ($LASTEXITCODE -ne 0) { throw "$configuration build failed." }
    }
    dotnet test tests\PasteHatch.Core.Tests -c Release --artifacts-path $artifacts
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    dotnet publish $project -c Release -r win-x64 -p:Platform=x64 --self-contained true --artifacts-path $artifacts -o $stage -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }

    foreach ($required in @('PasteHatch.exe', 'PasteHatch.dll', 'PasteHatch.Core.dll', 'PasteHatch.pri', 'App.xbf', 'MainWindow.xbf', 'SettingsWindow.xbf', 'Assets\PasteHatch.ico', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'Microsoft.UI.Xaml.dll', 'Microsoft.WindowsAppRuntime.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $stage $required) -PathType Leaf)) {
            throw "Published package is missing $required."
        }
    }
    $config = Get-Content -LiteralPath (Join-Path $stage 'PasteHatch.runtimeconfig.json') -Raw | ConvertFrom-Json
    if ($config.runtimeOptions.framework -or $config.runtimeOptions.frameworks) {
        throw 'Published app still requires an installed .NET runtime.'
    }
    $unexpected = Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object {
        $_.Extension -in @('.pdb', '.cs', '.ps1', '.trx') -or $_.Name -in @('state.json', 'settings.json') -or $_.Name -like '.env*'
    }
    if ($unexpected) { throw 'Published output contains debug, source or personal-data files.' }
    $readme = @"
PasteHatch $version (Windows x64)

Extract this entire folder and run PasteHatch.exe. Keep its files together.
Windows 10 version 1809 or later, or Windows 11 is required.
The ZIP includes .NET and Windows App SDK; no runtime installation is needed.

Your baskets and settings stay in %LOCALAPPDATA%\PasteHatch, outside this folder.
To update, choose Exit in the tray, replace the program folder, then run it again.
To remove the program, choose Exit and delete its folder. Your baskets remain.
"@
    [IO.File]::WriteAllText((Join-Path $stage 'README.txt'), $readme, (New-Object Text.UTF8Encoding($false)))
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $temporaryZip = Join-Path $run "$name.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporaryZip, [IO.Compression.CompressionLevel]::Optimal, $true)
    Move-Item -LiteralPath $temporaryZip -Destination $zip -Force
    Write-Host "Packaged: $zip"
    Write-Host "Build files: $run"
}
finally { Pop-Location }
