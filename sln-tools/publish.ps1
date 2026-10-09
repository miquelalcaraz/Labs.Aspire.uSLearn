<#
.SYNOPSIS
  Packs the shared building blocks (Core.*) as NuGet packages and optionally pushes them to a feed.

.DESCRIPTION
  Packages are written to <repo>/artifacts/packages (ignored by git).
  Pushing only happens when -Source is given; the API key is read from the NUGET_API_KEY
  environment variable (or -ApiKey), never stored in the repository.

.EXAMPLE
  ./publish.ps1                                   # pack only
  ./publish.ps1 -Version 1.2.0                    # pack with an explicit version
  ./publish.ps1 -LocalFeed D:\.nuget\local        # pack and copy to a local folder feed
  $env:NUGET_API_KEY = '...'; ./publish.ps1 -Source https://api.nuget.org/v3/index.json
#>
[CmdletBinding()]
param
(
  [string]$Solution = (Join-Path $PSScriptRoot '..\uSLearn.slnx'),
  [string]$ProjectFilter = 'Core.*',
  [string]$Configuration = 'Release',
  [string]$Version,
  [string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\packages'),
  [string]$LocalFeed,
  [string]$Source,
  [string]$ApiKey = $env:NUGET_API_KEY
)

$ErrorActionPreference = 'Stop'
$Solution = (Resolve-Path -LiteralPath $Solution).Path
$solutionDir = Split-Path $Solution

# `dotnet sln list` understands both .sln and .slnx
$projects = dotnet sln $Solution list |
  Where-Object { $_ -match '\.csproj$' } |
  ForEach-Object { Join-Path $solutionDir $_ } |
  Where-Object { [IO.Path]::GetFileNameWithoutExtension($_) -like $ProjectFilter }

if (-not $projects) {
  Write-Warning "No projects matching '$ProjectFilter' found in $Solution."
  exit 1
}

if (Test-Path -LiteralPath $OutputPath) {
  Remove-Item -LiteralPath $OutputPath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null

$packArgs = @('--configuration', $Configuration, '--output', $OutputPath)
if ($Version) { $packArgs += "-p:Version=$Version" }

foreach ($project in $projects) {
  Write-Host "Packing $([IO.Path]::GetFileNameWithoutExtension($project))..."
  dotnet pack $project @packArgs
  if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed for $project" }
}

$packages = Get-ChildItem -LiteralPath $OutputPath -Filter '*.nupkg'
Write-Host "$($packages.Count) package(s) created in $OutputPath"

if ($LocalFeed) {
  New-Item -ItemType Directory -Force -Path $LocalFeed | Out-Null
  $packages | Copy-Item -Destination $LocalFeed -Force
  Write-Host "Copied packages to local feed $LocalFeed"
}

if ($Source) {
  if (-not $ApiKey) { throw 'Pushing requires -ApiKey or the NUGET_API_KEY environment variable.' }
  foreach ($package in $packages) {
    dotnet nuget push $package.FullName --source $Source --api-key $ApiKey --skip-duplicate
    if ($LASTEXITCODE -ne 0) { throw "dotnet nuget push failed for $($package.Name)" }
  }
}

Write-Host 'Publish completed.'
