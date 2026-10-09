<#
.SYNOPSIS
  Removes build output (bin/obj) and empty directories under the sources folder.

.EXAMPLE
  ./cleanup.ps1 -SourcesPath ../src
  ./cleanup.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param
(
  [string]$SourcesPath = (Join-Path $PSScriptRoot '..\src')
)

$ErrorActionPreference = 'Stop'
$SourcesPath = (Resolve-Path -LiteralPath $SourcesPath).Path

# Never touch tooling / VCS folders
$excluded = '\\(\.git|\.vs|\.vscode|\.idea|node_modules)(\\|$)'

function Remove-BuildOutput {
  Get-ChildItem -LiteralPath $SourcesPath -Directory -Recurse -Force -Include bin, obj |
    Where-Object { $_.FullName -notmatch $excluded } |
    Sort-Object { $_.FullName.Length } |
    ForEach-Object {
      # A nested bin/obj may already be gone together with its parent
      if ((Test-Path -LiteralPath $_.FullName) -and $PSCmdlet.ShouldProcess($_.FullName, 'Remove')) {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force
        Write-Host "removed $($_.FullName)"
      }
    }
}

function Remove-EmptyFolders {
  # Deepest first, so parents that become empty are removed too
  Get-ChildItem -LiteralPath $SourcesPath -Directory -Recurse -Force |
    Where-Object { $_.FullName -notmatch $excluded } |
    Sort-Object { $_.FullName.Length } -Descending |
    ForEach-Object {
      if (-not (Get-ChildItem -LiteralPath $_.FullName -Force | Select-Object -First 1) -and
          $PSCmdlet.ShouldProcess($_.FullName, 'Remove empty folder')) {
        Remove-Item -LiteralPath $_.FullName -Force
        Write-Host "removed empty $($_.FullName)"
      }
    }
}

Remove-BuildOutput
Remove-EmptyFolders
Write-Host 'Cleanup completed.'
