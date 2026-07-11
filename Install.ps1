<#
    DarkTaskMgr installer (no administrator rights required).

    Copies DarkTaskMgr.exe into %LOCALAPPDATA%\DarkTaskMgr, creates a Start Menu shortcut and a
    Desktop shortcut so it launches like any other app, and starts it. Run it by right-clicking and
    choosing "Run with PowerShell", or from a console:

        powershell -ExecutionPolicy Bypass -File .\Install.ps1
#>

$ErrorActionPreference = 'Stop'

$InstallDir = Join-Path $env:LOCALAPPDATA 'DarkTaskMgr'
$ExeName    = 'DarkTaskMgr.exe'

function Find-Source($name) {
    $here = Split-Path -Parent $MyInvocation.MyCommand.Definition
    $candidates = @(
        (Join-Path $here $name),
        (Join-Path $here "DarkTaskMgr\bin\x64\Release\$name"),
        (Join-Path $here "DarkTaskMgr\bin\x64\Debug\$name")
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    return $null
}

function New-Shortcut($linkPath, $target) {
    $shell = New-Object -ComObject WScript.Shell
    $sc = $shell.CreateShortcut($linkPath)
    $sc.TargetPath = $target
    $sc.WorkingDirectory = Split-Path -Parent $target
    $sc.IconLocation = "$target,0"
    $sc.Description = 'DarkTaskMgr - a dark-theme Task Manager'
    $sc.Save()
}

Write-Host 'Installing DarkTaskMgr...' -ForegroundColor Cyan

# 1. Stop any running instance so the file is not locked.
Get-Process -Name 'DarkTaskMgr' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

# 2. Copy the executable into the per-user install folder.
$src = Find-Source $ExeName
if (-not $src) { throw "Could not find $ExeName. Build Release|x64 in Visual Studio first (Ctrl+Shift+B), or place $ExeName next to this script." }
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Force -Path $src -Destination (Join-Path $InstallDir $ExeName)
$exe = Join-Path $InstallDir $ExeName

# 3. Create the launcher shortcuts (Start Menu and Desktop).
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\DarkTaskMgr.lnk'
$desktop   = Join-Path ([Environment]::GetFolderPath('Desktop')) 'DarkTaskMgr.lnk'
New-Shortcut $startMenu $exe
New-Shortcut $desktop $exe

# 4. Launch now.
Start-Process -FilePath $exe
Write-Host "Installed to $InstallDir. A shortcut is on your Start Menu and Desktop." -ForegroundColor Green
