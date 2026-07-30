@echo off
rem Double-click launcher. Keeps the window open if the script errors out.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0damage-meter.ps1" %*
if errorlevel 1 pause
