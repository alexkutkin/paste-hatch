param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [string]$ExecutablePath
)
$ErrorActionPreference = 'Stop'
$PSDefaultParameterValues['Set-Content:Encoding'] = 'UTF8'
$PSDefaultParameterValues['Add-Content:Encoding'] = 'UTF8'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Run with powershell -STA.' }
. (Join-Path $PSScriptRoot 'project-output.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$exe = if ($ExecutablePath) { (Resolve-Path -LiteralPath $ExecutablePath).Path } else {
    Get-PasteHatchExecutable -Configuration $Configuration -Platform x64
}
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
function Find-BasketRow([string]$Name) {
    (Find-Control 'BasketsList').FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$Name),
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem)))
}
function Resw([string]$Language, [string]$Key) {
    $doc = [xml](Get-Content (Join-Path $repo "src/PasteHatch.App/Strings/$Language/Resources.resw") -Raw -Encoding UTF8)
    ($doc.root.data | Where-Object { $_.name -eq $Key }).value
}
function Find-Settings([string]$Id) {
    $script:settingsWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$Id))
}
function Find-SettingsItems {
    $script:settingsWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem))
}
function Select-Node([string]$Name) {
    "ACTION select tree item $Name" | Add-Content (Join-Path $run 'actions.txt')
    $node = Find-Node $Name
    if (!$node) { throw "Missing tree item: $Name" }
    $node.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function Snapshot([string]$Name, [System.Windows.Automation.AutomationElement]$Root = $window) {
    $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object {
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
    Wait-For 'owned window stays outside the desktop' { $window.Current.BoundingRectangle.Right -lt 0 -and $window.Current.BoundingRectangle.Bottom -lt 0 }
    @{pid=$app.Id; started=$app.StartTime.ToString('o'); exe=$app.Path; sha256=(Get-FileHash $exe).Hash; data=$data} | ConvertTo-Json | Set-Content (Join-Path $run 'doctor.json')

    # ---- Interface language: English is the default, switching is live and persisted ----------
    $settingsPath = Join-Path $data 'settings.json'
    $englishSettingsTitle = Resw 'en-US' 'Settings_Title'
    $russianSettingsTitle = Resw 'ru-RU' 'Settings_Title'
    $russianPaste = Resw 'ru-RU' 'Command_Paste'
    $russianBaskets = Resw 'ru-RU' 'Header_Baskets'
    $englishPaste = Resw 'en-US' 'Command_Paste'
    Wait-For 'starts in English' {
        (Find-Control 'PasteButton').Current.Name -eq $englishPaste -and
        (Find-Control 'BasketsList').Current.Name -eq (Resw 'en-US' 'Header_Baskets')
    }
    "ACTION open settings and switch language to Russian" | Add-Content (Join-Path $run 'actions.txt')
    Wait-For 'settings command on the toolbar' { $null -ne (Find-Control 'SettingsButton') }
    (Find-Control 'SettingsButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $script:settingsWindow = $null
    Wait-For 'settings window opened in English' {
        $script:settingsWindow = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.AndCondition]::new(
                [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,[int]$app.Id),
                [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$englishSettingsTitle)))
        $null -ne $script:settingsWindow -and $null -ne (Find-Settings 'LanguageCombo')
    }
    (Find-Settings 'LanguageCombo').GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Wait-For 'language options listed' { (Find-SettingsItems).Count -ge 2 }
    $languageItems = Find-SettingsItems
    $englishName = $languageItems[0].Current.Name
    ($languageItems | Where-Object { $_.Current.Name -ne $englishName } | Select-Object -First 1).GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Wait-For 'main window switched to Russian' {
        (Find-Control 'PasteButton').Current.Name -eq $russianPaste -and
        (Find-Control 'BasketsList').Current.Name -eq $russianBaskets
    }
    Wait-For 'settings window switched to Russian' {
        $script:settingsWindow.Current.Name -eq $russianSettingsTitle -and
        $null -ne (Find-Settings 'LanguageCombo')
    }
    Wait-For 'Russian choice persisted' { (Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json).language -eq 'ru' }
    "ACTION switch language back to English" | Add-Content (Join-Path $run 'actions.txt')
    (Find-Settings 'LanguageCombo').GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Wait-For 'language options listed again' { (Find-SettingsItems).Count -ge 2 }
    (Find-SettingsItems)[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Wait-For 'main window switched back to English' {
        (Find-Control 'PasteButton').Current.Name -eq $englishPaste -and
        (Find-Control 'BasketsList').Current.Name -eq (Resw 'en-US' 'Header_Baskets')
    }
    Wait-For 'English choice persisted' { (Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json).language -eq 'en' }
    Wait-For 'basket deletion confirmation is enabled by default' {
        (Find-Settings 'ConfirmBasketDeletionToggle').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On
    }
    (Find-Settings 'ConfirmBasketDeletionToggle').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Wait-For 'basket deletion preference is persisted' {
        (Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json).confirmBasketDeletion -eq $false
    }
    $script:settingsWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    $script:settingsWindow = $null
    Invoke-Control 'SettingsButton'
    Wait-For 'settings reopen with saved confirmation preference' {
        $script:settingsWindow = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.AndCondition]::new(
                [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,[int]$app.Id),
                [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$englishSettingsTitle)))
        $null -ne $script:settingsWindow -and
        (Find-Settings 'ConfirmBasketDeletionToggle').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [System.Windows.Automation.ToggleState]::Off
    }
    (Find-Settings 'ConfirmBasketDeletionToggle').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Wait-For 'confirmation can be enabled again' {
        (Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json).confirmBasketDeletion -eq $true
    }
    $script:settingsWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    $script:settingsWindow = $null

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
        $title = Find-Control 'PreviewTitle'
        $preview = Find-Control 'PreviewText'
        $null -ne $title -and $null -ne $preview -and $title.Current.Name -eq 'inside.txt' -and
        $preview.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq 'inside content'
    }
    Wait-For 'preview uses available panel height' {
        $previewBounds = (Find-Control 'PreviewText').Current.BoundingRectangle
        $treeBounds = (Find-Control 'FileTree').Current.BoundingRectangle
        $previewBounds.Height -gt 0 -and [Math]::Abs($previewBounds.Bottom - $treeBounds.Bottom) -le 24
    }
    Snapshot 'preview'
    # Materialize formats before modifying the shared clipboard, then restore in finally.
    $originalClipboard = [Windows.Forms.DataObject]::new()
    $clipboard = [Windows.Forms.Clipboard]::GetDataObject()
    if ($clipboard) { foreach ($format in $clipboard.GetFormats($false)) { $originalClipboard.SetData($format,$false,$clipboard.GetData($format,$false)) } }
    $clipboardSaved = $true
    foreach ($entry in @(@{name='inside.txt'; path=(Join-Path $basket 'folder2\inside.txt')}, @{name='folder2'; path=(Join-Path $basket 'folder2')})) {
        "ACTION invoke row copy path: $($entry.name)" | Add-Content (Join-Path $run 'actions.txt')
        Wait-For "row actions are ready for $($entry.name)" {
            $node = Find-Node $entry.name
            if (!$node) { return $false }
            $script:rowCopy = $node.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'EntryCopyPathButton'))
            $script:rowOpen = $node.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'EntryOpenButton'))
            $null -ne $rowCopy -and $null -ne $rowOpen -and $rowOpen.Current.IsEnabled
        }
        Wait-For "row actions have right padding for $($entry.name)" {
            (Find-Node $entry.name).Current.BoundingRectangle.Right - $rowCopy.Current.BoundingRectangle.Right -ge 8
        }
        $rowContent = (Find-Node $entry.name).FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'EntryCopyContentButton'))
        Wait-For "copy path has a separate whitespace gap for $($entry.name)" {
            $previous = if ($rowContent -and $rowContent.Current.BoundingRectangle.Width -gt 0) { $rowContent } else { $rowOpen }
            $rowCopy.Current.BoundingRectangle.Left - $previous.Current.BoundingRectangle.Right -ge 12
        }
        $rowCopy.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Wait-For "row copy path equals $($entry.name) path" { [Windows.Forms.Clipboard]::GetText() -eq $entry.path }
    }
    Wait-For 'filesystem copy history persists folder and file events newest first' {
        $history = @((Get-Content (Join-Path $data 'state.json') -Raw -Encoding UTF8 | ConvertFrom-Json).recentCopiedPaths)
        $history.Count -eq 2 -and $history[0] -eq (Join-Path $basket 'folder2') -and
        $history[1] -eq (Join-Path $basket 'folder2\inside.txt')
    }
    $expectedPaste = 'Первые пять слов для имени шестое ' + [guid]::NewGuid().ToString('N')
    "ACTION clipboard text=$expectedPaste" | Add-Content (Join-Path $run 'actions.txt')
    [Windows.Forms.Clipboard]::SetText($expectedPaste)
    Invoke-Control 'PasteButton'
    Wait-For 'paste writes exact text into selected folder' {
        $pasted = Join-Path $basket 'folder2/Первые_пять_слов_для_имени.txt'
        (Test-Path -LiteralPath $pasted) -and [IO.File]::ReadAllText($pasted) -eq $expectedPaste
    }
    Invoke-Control 'PasteButton'
    Wait-For 'repeat paste keeps both exact contents with numbered collision suffix' {
        $pasted = Join-Path $basket 'folder2/Первые_пять_слов_для_имени (2).txt'
        (Test-Path -LiteralPath $pasted) -and [IO.File]::ReadAllText($pasted) -eq $expectedPaste
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
    $clipboardFile = Join-Path $basket 'folder2/clipboard-copy.bin'
    $clipboardCopy = Join-Path $basket 'folder2/clipboard-copy (2).bin'
    [IO.File]::WriteAllBytes($clipboardFile,[byte[]](0,1,2,127,128,255))
    $clipboardFileHash = (Get-FileHash -LiteralPath $clipboardFile).Hash
    "ACTION external binary fixture: $clipboardFile" | Add-Content (Join-Path $run 'actions.txt')
    Wait-For 'binary clipboard fixture appears in the tree' { $null -ne (Find-Node 'clipboard-copy.bin') }
    Select-Node 'clipboard-copy.bin'
    $script:rowCopyContent = $null
    Wait-For 'binary file exposes its content copy command' {
        $node = Find-Node 'clipboard-copy.bin'
        if (!$node) { return $false }
        $script:rowCopyContent = $node.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'EntryCopyContentButton'))
        $null -ne $rowCopyContent -and $rowCopyContent.Current.IsEnabled
    }
    "ACTION invoke binary row content copy" | Add-Content (Join-Path $run 'actions.txt')
    $rowCopyContent.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Wait-For 'local file copy writes paths, a transfer identity and the Windows copy effect together' {
        $nativeClipboard = [Windows.Forms.Clipboard]::GetDataObject()
        if (!$nativeClipboard) { return $false }
        $paths = @($nativeClipboard.GetData([Windows.Forms.DataFormats]::FileDrop))
        $token = [guid]::Empty
        $hasToken = [guid]::TryParse([string]$nativeClipboard.GetData('PasteHatch.LocalTransferToken.v1',$false),[ref]$token)
        $effect = $nativeClipboard.GetData('Preferred DropEffect',$false)
        @($paths).Count -eq 1 -and $paths[0] -eq $clipboardFile -and $hasToken -and $token -ne [guid]::Empty -and
        $effect -is [IO.MemoryStream] -and $effect.Length -ge 4 -and [BitConverter]::ToUInt32($effect.ToArray(),0) -eq 1
    }
    [Windows.Forms.Clipboard]::GetDataObject().GetFormats($false) | Set-Content (Join-Path $run 'clipboard-local.formats.txt')
    Invoke-Control 'PasteButton'
    Wait-For 'local file copy pastes an exact second file into the same folder' {
        (Test-Path -LiteralPath $clipboardCopy -PathType Leaf) -and
        (Get-FileHash -LiteralPath $clipboardCopy).Hash -eq $clipboardFileHash -and
        (Get-FileHash -LiteralPath $clipboardFile).Hash -eq $clipboardFileHash
    }
    $externalFiles = [Collections.Specialized.StringCollection]::new()
    [void]$externalFiles.Add($clipboardFile)
    "ACTION replace the clipboard externally with the identical file path" | Add-Content (Join-Path $run 'actions.txt')
    [Windows.Forms.Clipboard]::SetFileDropList($externalFiles)
    Invoke-Control 'PasteButton'
    Wait-For 'external replacement of the same paths uses import without another copy or history event' {
        (Find-Control 'StatusText').Current.Name -eq (Resw 'en-US' 'Status_AlreadyInBasket') -and
        @(Get-ChildItem (Join-Path $basket 'folder2') -Filter 'clipboard-copy*.bin' -File).Count -eq 2 -and
        (Get-FileHash -LiteralPath $clipboardFile).Hash -eq $clipboardFileHash -and
        (Get-FileHash -LiteralPath $clipboardCopy).Hash -eq $clipboardFileHash -and
        @((Get-Content (Join-Path $data 'state.json') -Raw -Encoding UTF8 | ConvertFrom-Json).recentCopiedPaths).Count -eq 2
    }
    Snapshot 'clipboard-transfer'
    # Selecting the containing folder explicitly avoids relying on post-paste selection.
    Select-Node 'folder2'
    Wait-For 'selected parent persisted exactly' {
        $persisted = Get-Content (Join-Path $data 'state.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $persisted.tabs[0].selectedFolder -eq 'folder2'
    }
    $before = @(Get-ChildItem (Join-Path $basket 'folder2') -Directory).FullName
    $filesBeforeRename = @(Get-ChildItem -LiteralPath $basket -File -Recurse | Sort-Object FullName | ForEach-Object {
        '{0}|{1}' -f $_.FullName,(Get-FileHash -LiteralPath $_.FullName).Hash
    })
    $filesBeforeRename | Set-Content (Join-Path $run 'rename-unchanged.files.txt')
    Invoke-Control 'NewFolderButton'
    Wait-For 'new folder created in selected parent' { @(Get-ChildItem (Join-Path $basket 'folder2') -Directory | Where-Object { $_.FullName -notin $before }).Count -eq 1 }
    $newFolder = Get-ChildItem (Join-Path $basket 'folder2') -Directory | Where-Object { $_.FullName -notin $before }
    $saveName = Resw 'en-US' 'Action_SaveName'
    $script:renameSave = $null
    Wait-For 'new folder enters inline rename with its original name' {
        $node = Find-Node $newFolder.Name
        if (!$node) { return $false }
        $editor = $node.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Edit))
        $script:renameSave = $node.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$saveName))
        $null -ne $editor -and $null -ne $renameSave -and
        $editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq $newFolder.Name
    }
    Snapshot 'rename-unchanged-editing' (Find-Node $newFolder.Name)
    $renameNode = Find-Node $newFolder.Name
    if (!$renameNode) { throw 'The new folder row disappeared before unchanged-name submission.' }
    $renameEditor = $renameNode.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Edit))
    $renameSave = $renameNode.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$saveName))
    if (!$renameEditor -or !$renameSave -or $renameEditor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $newFolder.Name) {
        throw 'The unchanged-name editor was no longer active immediately before submission.'
    }
    $renameRowRuntime = $renameNode.GetRuntimeId() -join ','
    $renameStateWritten = [IO.File]::GetLastWriteTimeUtc((Join-Path $data 'state.json')).ToString('o')
    "ACTION submit unchanged inline name: $($newFolder.FullName)" | Add-Content (Join-Path $run 'actions.txt')
    $renameSave.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Wait-For 'unchanged name submission closes the inline editor without changing the folder or files' {
        $node = Find-Node $newFolder.Name
        if (!$node -or !(Test-Path -LiteralPath $newFolder.FullName -PathType Container)) { return $false }
        $script:renameObservedRuntime = $node.GetRuntimeId() -join ','
        $save = $node.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$saveName))
        $label = $node.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'EntryNameText'))
        $filesAfterRename = @(Get-ChildItem -LiteralPath $basket -File -Recurse | Sort-Object FullName | ForEach-Object {
            '{0}|{1}' -f $_.FullName,(Get-FileHash -LiteralPath $_.FullName).Hash
        })
        $renameObservedRuntime -eq $renameRowRuntime -and
        $null -eq $save -and $null -ne $label -and $label.Current.Name -eq $newFolder.Name -and
        @((Get-ChildItem (Join-Path $basket 'folder2') -Directory | Where-Object { $_.FullName -notin $before })).Count -eq 1 -and
        @((Compare-Object $filesBeforeRename $filesAfterRename)).Count -eq 0
    }
    "OBSERVATION unchanged rename: row runtime $renameRowRuntime -> $renameObservedRuntime; state written $renameStateWritten -> $([IO.File]::GetLastWriteTimeUtc((Join-Path $data 'state.json')).ToString('o'))" | Add-Content (Join-Path $run 'actions.txt')
    Snapshot 'rename-unchanged'
    Snapshot 'workspace'

    Wait-For 'both basket creation commands are available' {
        (Find-Control 'AddBasketButton').Current.IsEnabled -and (Find-Control 'AddBasketFooterButton').Current.IsEnabled
    }
    Invoke-Control 'AddBasketFooterButton'
    Wait-For 'new basket persisted and active' {
        $state = Get-Content (Join-Path $data 'state.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        @($state.tabs).Count -eq 2 -and $state.activeUid -eq $state.tabs[1].uid -and
        @(Get-ChildItem (Join-Path $data 'Baskets') -Directory).Count -eq 2
    }
    Invoke-Control 'AddBasketButton'
    Wait-For 'upper basket command uses the same creation and activation flow' {
        $state = Get-Content (Join-Path $data 'state.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        @($state.tabs).Count -eq 3 -and $state.activeUid -eq $state.tabs[2].uid
    }
    Snapshot 'baskets'
    "ACTION copy an external named folder directly into Baskets" | Add-Content (Join-Path $run 'actions.txt')
    $externalSource = Join-Path $run 'repo-grooming-skill'
    New-Item -ItemType Directory -Path $externalSource | Out-Null
    [IO.File]::WriteAllText((Join-Path $externalSource 'SKILL.md'),'external folder content')
    Copy-Item -LiteralPath $externalSource -Destination (Join-Path $data 'Baskets') -Recurse
    Wait-For 'root watcher discovers named external basket without refresh' {
        $state = Get-Content (Join-Path $data 'state.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        @($state.tabs | Where-Object { $_.directoryName -eq 'repo-grooming-skill' -and $_.name -eq 'repo-grooming-skill' }).Count -eq 1
    }
    $externalRow = $null
    Wait-For 'external basket appears in basket rail' {
        $script:externalRow = (Find-Control 'BasketsList').FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.AndCondition]::new(
                [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'repo-grooming-skill'),
                [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem)))
        $null -ne $script:externalRow
    }
    $externalRow.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Wait-For 'copied basket contents appear in tree' { $null -ne (Find-Node 'SKILL.md') }
    Wait-For 'basket switch selects and previews its first file automatically' {
        $node = Find-Node 'SKILL.md'
        $preview = Find-Control 'PreviewText'
        $null -ne $node -and $node.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected -and
        $null -ne $preview -and $preview.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq 'external folder content'
    }
    $externalBasketPath = Join-Path $data 'Baskets\repo-grooming-skill'
    [IO.File]::WriteAllText((Join-Path $externalBasketPath 'Z-last.txt'),'manual selection content')
    Wait-For 'second file appears in the current basket' { $null -ne (Find-Node 'Z-last.txt') }
    Select-Node 'Z-last.txt'
    Wait-For 'manual file selection displays its contents' {
        $preview = Find-Control 'PreviewText'
        $null -ne $preview -and $preview.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq 'manual selection content'
    }
    [IO.File]::WriteAllText((Join-Path $externalBasketPath 'A-first.txt'),'automatic first file content')
    Wait-For 'same basket watcher refresh retains the manually selected file' {
        $node = Find-Node 'Z-last.txt'
        $preview = Find-Control 'PreviewText'
        $selection = $null
        $value = $null
        $hasSelection = $null -ne $node -and $node.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selection)
        $hasValue = $null -ne $preview -and $preview.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$value)
        $null -ne (Find-Node 'A-first.txt') -and $hasSelection -and $selection.Current.IsSelected -and
        $hasValue -and $value.Current.Value -eq 'manual selection content'
    }
    $otherBasketRow = (Find-Control 'BasketsList').FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem)) |
        Where-Object { $_.Current.Name -ne 'repo-grooming-skill' } | Select-Object -First 1
    $otherBasketRow.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Wait-For 'switching to the original basket selects its first root folder' {
        $state = Get-Content (Join-Path $data 'state.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $firstFolder = Find-Node 'folder2'
        ($state.tabs | Where-Object uid -EQ $state.activeUid).directoryName -ne 'repo-grooming-skill' -and
        $null -ne $firstFolder -and $firstFolder.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected
    }
    (Find-BasketRow 'repo-grooming-skill').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Wait-For 'returning to a basket selects its first file instead of the previous manual selection' {
        $node = Find-Node 'A-first.txt'
        $preview = Find-Control 'PreviewText'
        $null -ne $node -and $node.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected -and
        $null -ne $preview -and $preview.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq 'automatic first file content'
    }
    Invoke-Control 'CopyPathButton'
    Wait-For 'basket root copy enters history without basket activation history' {
        $history = @((Get-Content (Join-Path $data 'state.json') -Raw -Encoding UTF8 | ConvertFrom-Json).recentCopiedPaths)
        $history.Count -eq 3 -and $history[0] -eq (Join-Path $data 'Baskets\repo-grooming-skill') -and
        [Windows.Forms.Clipboard]::GetText() -eq $history[0]
    }
    Snapshot 'external-basket'
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
