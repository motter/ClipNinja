#requires -version 5
# ClipNinja uninstaller — removes Start Menu and Desktop shortcuts.
# Optionally wipes %AppData%\ClipNinja.
# Does NOT delete the .exe itself — user deletes it manually.

$ErrorActionPreference = 'Stop'

Write-Host ''
Write-Host '  ClipNinja Uninstaller' -ForegroundColor Cyan
Write-Host '  ---------------------'
Write-Host ''

$startMenu = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs'
$desk      = [Environment]::GetFolderPath('Desktop')
$startLnk  = Join-Path $startMenu 'ClipNinja.lnk'
$deskLnk   = Join-Path $desk      'ClipNinja.lnk'

if (Test-Path $startLnk) {
    Remove-Item $startLnk
    Write-Host '  [OK] Removed Start Menu shortcut.' -ForegroundColor Green
} else {
    Write-Host '  [--] No Start Menu shortcut found.' -ForegroundColor DarkGray
}

if (Test-Path $deskLnk) {
    Remove-Item $deskLnk
    Write-Host '  [OK] Removed Desktop shortcut.' -ForegroundColor Green
} else {
    Write-Host '  [--] No Desktop shortcut found.' -ForegroundColor DarkGray
}

$reply = Read-Host '  Also wipe your saved slots and settings? (y/n)'
if ($reply -match '^[Yy]') {
    $data = Join-Path $env:APPDATA 'ClipNinja'
    if (Test-Path $data) {
        Remove-Item -Recurse -Force $data
        Write-Host "  [OK] Wiped $data" -ForegroundColor Green
    } else {
        Write-Host '  [--] No data folder to wipe.' -ForegroundColor DarkGray
    }
} else {
    Write-Host "  [--] Kept your saved data at $env:APPDATA\ClipNinja" -ForegroundColor DarkGray
}

Write-Host ''
Write-Host '  Done. You can now delete ClipNinja.exe and these installer files.' -ForegroundColor Cyan
Write-Host ''
Read-Host '  Press Enter to close'
