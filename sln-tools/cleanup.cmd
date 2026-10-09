@echo off
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%~dp0cleanup.ps1" -SourcesPath "%~dp0..\src" %*
