@echo off
rem Adds an EF Core migration for IdentityContext. Usage: buildschema.bat <MigrationName>
rem https://learn.microsoft.com/ef/core/cli/dotnet
if "%~1"=="" (
  echo Usage: %~nx0 ^<MigrationName^>
  exit /b 1
)
pushd "%~dp0"
dotnet ef migrations add %1 -c IdentityContext -o Infrastructure/Migrations
popd
