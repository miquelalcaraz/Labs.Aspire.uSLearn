

echo on

Powershell.exe -ExecutionPolicy ByPass -NoProfile  -file  "%~dp0\cleanup.ps1" -sourcesPath "%~dp0\..\src"