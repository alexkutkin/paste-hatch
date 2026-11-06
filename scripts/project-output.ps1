function Get-PasteHatchExecutable {
    param(
        [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
        [ValidateSet('x64', 'ARM64')][string]$Platform = 'x64',
        [string]$ArtifactsPath
    )

    $project = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\PasteHatch.App\PasteHatch.App.csproj'
    $arguments = @('msbuild', $project, '-nologo', '-getProperty:TargetDir,TargetName',
        "-p:Configuration=$Configuration", "-p:Platform=$Platform")
    if ($ArtifactsPath) {
        $arguments += @('-p:UseArtifactsOutput=true', "-p:ArtifactsPath=$([IO.Path]::GetFullPath($ArtifactsPath))")
    }
    $output = & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the application output from MSBuild.' }
    $properties = ($output -join [Environment]::NewLine | ConvertFrom-Json).Properties
    if (-not $properties.TargetDir -or -not $properties.TargetName) {
        throw 'MSBuild did not return the application output directory and name.'
    }
    Join-Path $properties.TargetDir ($properties.TargetName + '.exe')
}
