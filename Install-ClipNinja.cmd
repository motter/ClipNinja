@echo off
REM ClipNinja installer shim — this just runs Install-ClipNinja.ps1 with
REM ExecutionPolicy Bypass so users don't need to mess with PowerShell
REM security settings. All the actual install logic is in the .ps1 file.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-ClipNinja.ps1"
