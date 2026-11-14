@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-EasyConnect.ps1" %*
if errorlevel 1 pause
