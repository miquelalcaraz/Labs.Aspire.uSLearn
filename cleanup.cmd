@echo off
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%~dp0sln-tools\cleanup.ps1" -sourcesPath "%~dp0src"
