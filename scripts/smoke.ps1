# Basic smoke test for PasteHatch.
# Checks only the essentials:
#   1. the app starts and stays alive;
#   2. it creates the first basket folder and the tab state;
#   3. the main window exists and exposes UI Automation;
#   4. the "Paste" command adds clipboard text as a material file;
#   5. launching the app again does not start a second instance.
#
# The window is started off-screen without activation and without a tray icon, so the smoke
# test never pops up over the user's desktop.
#
# Usage: powershell -ExecutionPolicy Bypass -File scripts\smoke.ps1
param(
    [switch]$NoBuild,
    [switch]$KeepData
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class SmokeWin32
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);

    public static IntPtr FindWindow()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var buffer = new StringBuilder(256);
            GetWindowText(hWnd, buffer, buffer.Capacity);
            if (buffer.ToString().IndexOf("PasteHatch", StringComparison.Ordinal) < 0) return true;
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            try
            {
                if (string.Equals(Process.GetProcessById((int)pid).ProcessName, "PasteHatch", StringComparison.OrdinalIgnoreCase))
                {
                    found = hWnd;
                    return false;
                }
            }
            catch { }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\PasteHatch.App\PasteHatch.App.csproj'
$exe = Join-Path $repo 'src\PasteHatch.App\bin\x64\Debug\net9.0-windows10.0.19041.0\win-x64\PasteHatch.exe'
$dataRoot = Join-Path $repo '.scratch\smoke-data'
$basketsRoot = Join-Path $dataRoot 'Baskets'
$stateFile = Join-Path $dataRoot 'state.json'
$results = New-Object System.Collections.Generic.List[string]

function Add-Result([string]$name, [bool]$passed, [string]$detail) {
    $label = if ($passed) { 'PASS' } else { 'FAIL' }
    $line = '{0,-4} {1} - {2}' -f $label, $name, $detail
    $results.Add($line)
    Write-Host $line
}

function Stop-App {
    Get-Process PasteHatch -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

function Wait-Until([scriptblock]$condition, [int]$timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            if (& $condition) { return $true }
        }
        catch { }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

Push-Location $repo
try {
    if (-not $NoBuild) {
        dotnet build $project -c Debug -p:Platform=x64 -v m
        if ($LASTEXITCODE -ne 0) {
            Write-Host 'Build failed.'
            exit 1
        }
    }

    Stop-App
    if (Test-Path $dataRoot) { Remove-Item $dataRoot -Recurse -Force }
    $env:PASTEHATCH_DATA_ROOT = $dataRoot
    $env:PASTEHATCH_TEST_OFFSCREEN = '1'
    $env:PASTEHATCH_TEST_NO_TRAY = '1'

    $app = Start-Process -FilePath $exe -PassThru `
        -RedirectStandardOutput (Join-Path $repo '.scratch\smoke.out.log') `
        -RedirectStandardError (Join-Path $repo '.scratch\smoke.err.log')

    # 1. the app process starts and stays alive
    $running = Wait-Until { $null -ne (Get-Process -Id $app.Id -ErrorAction SilentlyContinue) } 10
    Add-Result 'app starts' $running "pid=$($app.Id)"

    # 2. it creates the first basket folder and the tab state
    $stateCreated = Wait-Until {
        (Test-Path $stateFile) -and
        ((Get-ChildItem $basketsRoot -Directory -ErrorAction SilentlyContinue | Measure-Object).Count -eq 1)
    } 15
    $basketCount = (Get-ChildItem $basketsRoot -Directory -ErrorAction SilentlyContinue | Measure-Object).Count
    Add-Result 'basket and state created' $stateCreated "baskets=$basketCount state=$(Test-Path $stateFile)"

    # 3. the WinUI window exists and exposes UI Automation
    $script:appWindowHandle = [IntPtr]::Zero
    $windowFound = Wait-Until {
        [void]($script:appWindowHandle = [SmokeWin32]::FindWindow())
        [bool]($script:appWindowHandle -ne [IntPtr]::Zero)
    } 15
    Add-Result 'window created' $windowFound "hwnd=$($script:appWindowHandle)"

    # 4. adding clipboard text through the Paste command creates a material file
    $materialAdded = $false
    if ($windowFound) {
        try {
            $expectedText = 'smoke ' + (Get-Date -Format 'HH:mm:ss')
            Set-Clipboard -Value $expectedText
            $window = [System.Windows.Automation.AutomationElement]::FromHandle($script:appWindowHandle)
            $script:pasteButton = $null
            Wait-Until {
                $condition = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'PasteButton')
                $script:pasteButton = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
                [bool]($script:pasteButton -ne $null)
            } 10 | Out-Null

            if ($null -ne $script:pasteButton) {
                $invoke = $script:pasteButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                $invoke.Invoke()
                $materialAdded = Wait-Until {
                    $files = Get-ChildItem $basketsRoot -Recurse -File -Filter '*.txt' -ErrorAction SilentlyContinue
                    ($files | Where-Object { (Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue) -eq $expectedText }).Count -ge 1
                } 10
            }
        }
        catch {
            Write-Host "  paste check error: $($_.Exception.Message)"
        }
    }
    Add-Result 'clipboard text added as .txt' $materialAdded 'material file content matches clipboard'

    # 5. a second launch must not start a second instance
    $second = Start-Process -FilePath $exe -PassThru `
        -RedirectStandardOutput (Join-Path $repo '.scratch\smoke-second.out.log') `
        -RedirectStandardError (Join-Path $repo '.scratch\smoke-second.err.log')
    $singleInstance = Wait-Until {
        $processes = @(Get-Process PasteHatch -ErrorAction SilentlyContinue)
        ($processes.Count -eq 1) -and ($null -eq (Get-Process -Id $second.Id -ErrorAction SilentlyContinue))
    } 10
    $processCount = @(Get-Process PasteHatch -ErrorAction SilentlyContinue).Count
    Add-Result 'single instance' $singleInstance "processes=$processCount"
}
catch {
    Add-Result 'unexpected error' $false $_.Exception.Message
}
finally {
    Stop-App
    Remove-Item Env:PASTEHATCH_TEST_OFFSCREEN -ErrorAction SilentlyContinue
    Remove-Item Env:PASTEHATCH_TEST_NO_TRAY -ErrorAction SilentlyContinue
    Remove-Item Env:PASTEHATCH_DATA_ROOT -ErrorAction SilentlyContinue
    if (-not $KeepData -and (Test-Path $dataRoot)) {
        Remove-Item $dataRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    Pop-Location
}

$passed = ($results | Where-Object { $_ -like 'PASS*' }).Count
$failed = ($results | Where-Object { $_ -like 'FAIL*' }).Count
Write-Host ''
Write-Host "SMOKE: $passed passed, $failed failed"
if ($failed -gt 0) { exit 1 }
exit 0
