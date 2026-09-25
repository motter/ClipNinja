#requires -version 5
# ClipNinja installer — creates Start Menu (and optionally Desktop) shortcuts
# pointing at the ClipNinja.exe sitting next to this script. Run via the
# Install-ClipNinja.cmd shim so users don't have to deal with execution policy.

$ErrorActionPreference = 'Stop'

# Locate the .exe — it must be in the same folder as this script.
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe  = Join-Path $here 'ClipNinja.exe'

Write-Host ''
Write-Host '  ClipNinja Installer' -ForegroundColor Cyan
Write-Host '  -------------------'
Write-Host ''

if (-not (Test-Path $exe)) {
    Write-Host "  ERROR: ClipNinja.exe was not found in:" -ForegroundColor Red
    Write-Host "  $here" -ForegroundColor Red
    Write-Host ''
    Write-Host '  Make sure Install-ClipNinja.cmd is in the SAME folder as ClipNinja.exe.'
    Write-Host ''
    Read-Host '  Press Enter to close'
    exit 1
}

Write-Host '  This will create shortcuts so you can launch ClipNinja from:'
Write-Host '    - Start Menu (search "ClipNinja")'
Write-Host '    - Desktop (optional)'
Write-Host ''
Write-Host "  Your data lives in:  $env:APPDATA\ClipNinja\"
Write-Host "  The .exe stays here: $here"
Write-Host ''

$shell = New-Object -ComObject WScript.Shell

# Always create the Start Menu shortcut
$startMenu = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs'
$startLnk  = Join-Path $startMenu 'ClipNinja.lnk'

$sc = $shell.CreateShortcut($startLnk)
$sc.TargetPath       = $exe
$sc.WorkingDirectory = $here
$sc.IconLocation     = "$exe,0"
$sc.Description      = 'ClipNinja - clipboard manager with a ninja'
$sc.Save()
Write-Host '  [OK] Start Menu shortcut created.' -ForegroundColor Green

# Ask about Desktop shortcut
$reply = Read-Host '  Also add a Desktop shortcut? (y/n)'
if ($reply -match '^[Yy]') {
    $desk    = [Environment]::GetFolderPath('Desktop')
    $deskLnk = Join-Path $desk 'ClipNinja.lnk'
    $sc2 = $shell.CreateShortcut($deskLnk)
    $sc2.TargetPath       = $exe
    $sc2.WorkingDirectory = $here
    $sc2.IconLocation     = "$exe,0"
    $sc2.Description      = 'ClipNinja - clipboard manager with a ninja'
    $sc2.Save()
    Write-Host '  [OK] Desktop shortcut created.' -ForegroundColor Green
}

# Offer to launch now
$reply2 = Read-Host '  Launch ClipNinja now? (y/n)'
if ($reply2 -match '^[Yy]') {
    Start-Process $exe
    Write-Host '  [OK] Launched. Look for the ninja in your system tray.' -ForegroundColor Green
}

Write-Host ''
Write-Host '  Done! You can close this window.' -ForegroundColor Cyan
Write-Host ''
Read-Host '  Press Enter to close'
