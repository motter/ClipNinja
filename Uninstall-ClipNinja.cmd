@echo off
REM ClipNinja uninstaller shim — invokes Uninstall-ClipNinja.ps1.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-ClipNinja.ps1"
