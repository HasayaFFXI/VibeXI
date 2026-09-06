@echo off
rem Double-click launcher. Keeps the window open if the script errors out.
python "%~dp0damage-meter.py" %*
if errorlevel 1 pause
