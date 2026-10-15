@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-FunClipper.ps1"
if errorlevel 1 pause
