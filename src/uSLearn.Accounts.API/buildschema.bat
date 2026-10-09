@echo off
rem Adds an EF Core migration for AccountContext. Usage: buildschema.bat <MigrationName>
rem https://learn.microsoft.com/ef/core/cli/dotnet
if "%~1"=="" (
  echo Usage: %~nx0 ^<MigrationName^>
  exit /b 1
)
pushd "%~dp0"
dotnet ef migrations add %1 -c AccountContext -o Infrastructure/Migrations
popd
