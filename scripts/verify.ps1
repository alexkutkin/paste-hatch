param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$PSDefaultParameterValues['Set-Content:Encoding'] = 'UTF8'
$PSDefaultParameterValues['Add-Content:Encoding'] = 'UTF8'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Run with powershell -STA.' }
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo "src/PasteHatch.App/bin/x64/$Configuration/net9.0-windows10.0.19041.0/win-x64/PasteHatch.exe"
if (Get-Process PasteHatch -ErrorAction SilentlyContinue) { throw 'Exit PasteHatch through the tray first; its single-instance mutex is shared.' }
if (!(Test-Path $exe)) { throw "Build $Configuration x64 first using scripts/build.ps1." }
$run = Join-Path $repo ('.scratch/verify-pastehatch/' + [guid]::NewGuid().ToString('N'))
$data = Join-Path $run 'data'
New-Item -ItemType Directory -Path $data -Force | Out-Null
$keys = @('PASTEHATCH_DATA_ROOT','PASTEHATCH_TEST_OFFSCREEN','PASTEHATCH_TEST_NO_TRAY')
$saved = @{}
foreach ($key in $keys) { $saved[$key] = [Environment]::GetEnvironmentVariable($key,'Process') }
$app = $null
$second = $null
$passed = $false
$clipboardSaved = $false
$checks = [Collections.Generic.List[string]]::new()
function Wait-For([string]$Name, [scriptblock]$Check) {
    $until = (Get-Date).AddSeconds(15)
    do {
        if (& $Check) { $checks.Add($Name); Write-Host "PASS $Name"; return }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $until)
    throw "Timed out: $Name"
}
function Find-Control([string]$Id) {
    $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$Id))
}
function Invoke-Control([string]$Id) {
    "ACTION invoke $Id" | Add-Content (Join-Path $run 'actions.txt')
    $control = Find-Control $Id
    if (!$control) { throw "Missing control: $Id" }
    $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Find-Node([string]$Name) {
    (Find-Control 'FileTree').FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$Name),
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::TreeItem)))
}
function Select-Node([string]$Name) {
    "ACTION select tree item $Name" | Add-Content (Join-Path $run 'actions.txt')
    $node = Find-Node $Name
    if (!$node) { throw "Missing tree item: $Name" }
    $node.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function Snapshot([string]$Name) {
    $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object {
        '{0}|{1}|{2}' -f $_.Current.AutomationId,$_.Current.Name,$_.Current.ControlType.ProgrammaticName
    } | Set-Content (Join-Path $run "$Name.ui.txt")
    Copy-Item (Join-Path $data 'state.json') (Join-Path $run "$Name.state.json")
}
function Doctor {
    $app.Refresh()
    if ($app.HasExited -or $app.Path -ne $exe -or !(Find-Control 'FileTree') -or !(Find-Control 'QuickNoteTextBox')) { throw 'Doctor failed.' }
}
try {
    $env:PASTEHATCH_DATA_ROOT = $data
    $env:PASTEHATCH_TEST_OFFSCREEN = '1'
    $env:PASTEHATCH_TEST_NO_TRAY = '1'
    $app = Start-Process $exe -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $run 'stdout.txt') -RedirectStandardError (Join-Path $run 'stderr.txt')
    $script:window = $null
    Wait-For 'ready from empty data root' {
        $app.Refresh()
        if ($app.HasExited) { throw 'Owned process exited before readiness.' }
        $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,[int]$app.Id)
        $script:window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,$condition)
        $null -ne $script:window -and $null -ne (Find-Control 'QuickNoteTextBox') -and (Test-Path (Join-Path $data 'state.json'))
    }
    Doctor
    @{pid=$app.Id; started=$app.StartTime.ToString('o'); exe=$app.Path; sha256=(Get-FileHash $exe).Hash; data=$data} | ConvertTo-Json | Set-Content (Join-Path $run 'doctor.json')
    $basket = @(Get-ChildItem (Join-Path $data 'Baskets') -Directory)
    if ($basket.Count -ne 1) { throw 'Expected one initial basket.' }
    $basket = $basket[0].FullName

    $expected = 'verification-' + [guid]::NewGuid().ToString('N')
    "ACTION set QuickNoteTextBox=$expected" | Add-Content (Join-Path $run 'actions.txt')
    (Find-Control 'QuickNoteTextBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($expected)
    Invoke-Control 'QuickNoteSaveButton'
    Wait-For 'note saved exactly and input cleared' {
        @(Get-ChildItem $basket -Filter '*.txt' | Where-Object { [IO.File]::ReadAllText($_.FullName) -eq $expected }).Count -eq 1 -and
        (Find-Control 'QuickNoteTextBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq ''
    }
    Snapshot 'note'

    # Fixtures are external filesystem changes, not substitutes for the UI actions under test.
    "ACTION external fixture: folder2/inside.txt and folder10" | Add-Content (Join-Path $run 'actions.txt')
    New-Item -ItemType Directory -Path (Join-Path $basket 'folder2'),(Join-Path $basket 'folder10') | Out-Null
    [IO.File]::WriteAllText((Join-Path $basket 'folder2/inside.txt'),'inside content')
    Wait-For 'watcher exposes external folders without refresh' { $null -ne (Find-Node 'folder2') -and $null -ne (Find-Node 'folder10') }
    Select-Node 'folder2'
    "ACTION expand folder2" | Add-Content (Join-Path $run 'actions.txt')
    (Find-Node 'folder2').GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Wait-For 'expanded folder exposes child' { $null -ne (Find-Node 'inside.txt') }
    Select-Node 'inside.txt'
    Wait-For 'preview displays selected text' {
        (Find-Control 'PreviewTitle').Current.Name -eq 'inside.txt' -and
        (Find-Control 'PreviewText').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq 'inside content'
    }
    Snapshot 'preview'
    # Materialize formats before modifying the shared clipboard, then restore in finally.
    $originalClipboard = [Windows.Forms.DataObject]::new()
    $clipboard = [Windows.Forms.Clipboard]::GetDataObject()
    if ($clipboard) { foreach ($format in $clipboard.GetFormats($false)) { $originalClipboard.SetData($format,$false,$clipboard.GetData($format,$false)) } }
    $clipboardSaved = $true
    Invoke-Control 'PreviewCopyPathButton'
    Wait-For 'copy path equals selected file path' { [Windows.Forms.Clipboard]::GetText() -eq (Join-Path $basket 'folder2\inside.txt') }
    $expectedPaste = 'pasted-' + [guid]::NewGuid().ToString('N')
    "ACTION clipboard text=$expectedPaste" | Add-Content (Join-Path $run 'actions.txt')
    [Windows.Forms.Clipboard]::SetText($expectedPaste)
    Invoke-Control 'PasteButton'
    Wait-For 'paste writes exact text into selected folder' {
        @(Get-ChildItem (Join-Path $basket 'folder2') -Filter '*.txt' | Where-Object { [IO.File]::ReadAllText($_.FullName) -eq $expectedPaste }).Count -eq 1
    }
    $source = Join-Path $data 'source-folder'
    New-Item -ItemType Directory -Path (Join-Path $source 'deep') | Out-Null
    [IO.File]::WriteAllText((Join-Path $source 'deep/nested.txt'),'nested content')
    $drop = [Collections.Specialized.StringCollection]::new()
    [void]$drop.Add($source)
    "ACTION clipboard file drop=$source" | Add-Content (Join-Path $run 'actions.txt')
    [Windows.Forms.Clipboard]::SetFileDropList($drop)
    Invoke-Control 'PasteButton'
    Wait-For 'folder import preserves nested contents' {
        $target = Join-Path $basket 'folder2/source-folder/deep/nested.txt'
        (Test-Path $target) -and [IO.File]::ReadAllText($target) -eq 'nested content'
    }
    # Selecting the containing folder explicitly avoids relying on post-paste selection.
    Select-Node 'folder2'
    Wait-For 'selected parent persisted exactly' {
        $persisted = Get-Content (Join-Path $data 'state.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $persisted.tabs[0].selectedFolder -eq 'folder2'
    }
    $before = @(Get-ChildItem (Join-Path $basket 'folder2') -Directory).FullName
    Invoke-Control 'NewFolderButton'
    Wait-For 'new folder created in selected parent' { @(Get-ChildItem (Join-Path $basket 'folder2') -Directory | Where-Object { $_.FullName -notin $before }).Count -eq 1 }
    Snapshot 'workspace'

    Invoke-Control 'AddBasketButton'
    Wait-For 'new basket persisted and active' {
        $state = Get-Content (Join-Path $data 'state.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        @($state.tabs).Count -eq 2 -and $state.activeUid -eq $state.tabs[1].uid -and
        @(Get-ChildItem (Join-Path $data 'Baskets') -Directory).Count -eq 2
    }
    Snapshot 'baskets'
    "ACTION second launch" | Add-Content (Join-Path $run 'actions.txt')
    $second = Start-Process $exe -PassThru -WindowStyle Hidden
    Wait-For 'second instance exits while primary survives' { $second.Refresh(); $app.Refresh(); $second.HasExited -and !$app.HasExited }
    Doctor
    $passed = $true
} catch {
    $_ | Out-String | Set-Content (Join-Path $run 'failure.txt')
    throw
} finally {
    # Stop every process this run created, including a failing secondary launch.
    foreach ($owned in @($second,$app)) {
        if ($owned) { $owned.Refresh(); if (!$owned.HasExited) { Stop-Process -InputObject $owned -Force; $owned.WaitForExit() } }
    }
    foreach ($key in $keys) { [Environment]::SetEnvironmentVariable($key,$saved[$key],'Process') }
    if ($clipboardSaved) {
        if ($originalClipboard.GetFormats($false).Count -gt 0) { [Windows.Forms.Clipboard]::SetDataObject($originalClipboard,$true) } else { [Windows.Forms.Clipboard]::Clear() }
    }
    $checks | Set-Content (Join-Path $run 'checks.txt')
    # Retain actual bytes on success AND failure, outside disposable storage.
    Copy-Item -LiteralPath $data -Destination (Join-Path $run 'observed-data') -Recurse
    if ((Split-Path $data -Parent) -ne $run -or (Split-Path $data -Leaf) -ne 'data' -or !([IO.Path]::GetFullPath($data).StartsWith([IO.Path]::GetFullPath((Join-Path $repo '.scratch/verify-pastehatch/'))))) { throw 'Unsafe cleanup target.' }
    Remove-Item -LiteralPath $data -Recurse -Force
    @{passed=$passed; processStopped=(!$app -or $app.HasExited); secondaryStopped=(!$second -or $second.HasExited); dataRemoved=!(Test-Path $data); checks=$checks.Count} | ConvertTo-Json | Set-Content (Join-Path $run 'result.json')
    if (!(Test-Path (Join-Path $run 'observed-data')) -or !(Test-Path (Join-Path $run 'checks.txt'))) { throw 'Evidence lost during cleanup.' }
    Write-Host "Evidence: $run"
}
